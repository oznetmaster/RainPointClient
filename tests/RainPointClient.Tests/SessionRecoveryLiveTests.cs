// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using RainPointClient.Protocol;

namespace RainPointClient.Tests;

[TestFixture, NonParallelizable, Category ("Live")]
public sealed class SessionRecoveryLiveTests
	{
	[Test, Explicit ("Signs in twice to the same private account, verifies rejection, then allows one credential recovery. No device writes.")]
	public Task AnotherLoginInvalidatesSessionAndOneRecoveryRestoresMonitoring () => RunRecovery (true);

	[Test, Explicit ("Uses an immediately returning credential policy after another login. No device writes.")]
	public Task AnotherLoginWithImmediateCredentialsRestoresMonitoring () => RunRecovery (false);

	private static async Task RunRecovery (bool delayCredentials)
		{
		Account account = LoadAccount ();
		using CancellationTokenSource timeout = new (TimeSpan.FromMinutes (9));
		using HttpClient http = new (new LoginResultObserver ())
			{
			Timeout = TimeSpan.FromSeconds (30),
			MaxResponseContentBufferSize = 4 * 1024 * 1024
			};
		using RainPointCloudClient client = new (http);
		using RainPointCloudClient other = new ();
		RainPointMonitor? monitor = null;
		RainPointSessionRecovery? recovery = null;
		CountingTransport transport = new ();
		int credentialRequests = 0;
		TaskCompletionSource<bool> allowRecovery = new (TaskCreationOptions.RunContinuationsAsynchronously);
		try
			{
			await client.LoginAsync (account.Email, account.Password, account.AreaCode, timeout.Token);
			DateTimeOffset firstLogin = DateTimeOffset.UtcNow;
			RainPointHub hub = await FindHub (client, timeout.Token);
			monitor = CreateMonitor (client, hub, transport);
			Task monitorRun = monitor.RunAsync (timeout.Token);
			await WaitUntil (() => transport.Connections >= 1 && Decoded (monitor), TimeSpan.FromSeconds (90), timeout.Token,
				 "Initial authenticated MQTT connection and decoded status were not observed.");
			recovery = new RainPointSessionRecovery (client, async token =>
				{
					Interlocked.Increment (ref credentialRequests);
					if (delayCredentials)
						{
						Task cancelled = Task.Delay (Timeout.Infinite, token);
						await Task.WhenAny (allowRecovery.Task, cancelled);
						token.ThrowIfCancellationRequested ();
						await allowRecovery.Task;
						}
					return new RainPointCredentials (account.Email, account.Password, account.AreaCode);
				});
			recovery.StateChanged += (_, args) => TestContext.Progress.WriteLine ("Recovery state: " + args.State);
			Task recoveryRun = recovery.RunAsync (timeout.Token);
			TestContext.Progress.WriteLine ("Initial monitor connected. Spacing the deliberate second login by two minutes.");
			await DelayUntil (firstLogin.AddMinutes (2), timeout.Token);
			int connectionsBeforeRecovery = transport.Connections;
			await other.LoginAsync (account.Email, account.Password, account.AreaCode, timeout.Token);
			DateTimeOffset displacedAt = DateTimeOffset.UtcNow;
			bool rejected = await ObserveRejection (client, () => Volatile.Read (ref credentialRequests) > 0, timeout.Token);
			Assert.That (rejected, Is.True, "The cloud did not demonstrate invalidation of the original session; do not report recovery as tested.");
			TestContext.Progress.WriteLine (delayCredentials ? "Original cloud session rejected. Waiting two minutes before the single credential recovery." : "Original cloud session rejected. Credentials are returned immediately, as in the Windows app.");
			if (delayCredentials)
				await DelayUntil (displacedAt.AddMinutes (2), timeout.Token);
			DateTimeOffset releasedAt = delayCredentials ? DateTimeOffset.UtcNow : displacedAt;
			allowRecovery.TrySetResult (true);
			await WaitUntil (() => recovery.State == RainPointSessionState.AuthenticationRequired
				 || recovery.State == RainPointSessionState.Healthy && client.HasValidSession
				 && transport.Connections > connectionsBeforeRecovery && Decoded (monitor)
				 && monitor.Current!.LastSuccessfulPollAt > releasedAt,
				 TimeSpan.FromMinutes (3), timeout.Token, "Credential recovery did not restore a new MQTT connection and fresh decoded status.");
			Assert.That (recovery.State, Is.EqualTo (RainPointSessionState.Healthy), "Automatic recovery exhausted its one login attempt.");
			Assert.That (await client.GetHomesAsync (timeout.Token), Has.Some.Property (nameof (RainPointHome.Id)).EqualTo (hub.HomeId));
			await Task.Delay (TimeSpan.FromSeconds (30), timeout.Token);
			Assert.That (credentialRequests, Is.EqualTo (1));
			Assert.That (recovery.State, Is.EqualTo (RainPointSessionState.Healthy));
			Assert.That (monitorRun.IsCompleted || recoveryRun.IsCompleted, Is.False, "A background worker ended unexpectedly.");
			TestContext.Progress.WriteLine ("Server rejection, exactly one credential recovery, new MQTT connection and fresh status confirmed. No device writes.");
			}
		catch (Exception error) when (error is not AssertionException && error is not SuccessException && error is not IgnoreException)
			{
			Assert.Fail ("Live recovery failed (" + error.GetType ().Name + "). Private account and transport details omitted.");
			}
		finally
			{
			if (recovery is not null)
				await recovery.StopAsync ();
			if (monitor is not null)
				await monitor.StopAsync ();
			// Never log out the displaced client's stale session after recovery; it might affect the recovered account.
			await Logout (client.HasValidSession ? client : other);
			}
		}

	[Test, Explicit ("Observes real MQTT renewal for twelve minutes using a private account. No clock injection, credential fallback or device writes.")]
	public async Task ObserverCredentialsRenewDuringTwelveMinuteReadOnlyRun ()
		{
		Account account = LoadAccount ();
		using CancellationTokenSource timeout = new (TimeSpan.FromMinutes (16));
		using RainPointCloudClient client = new ();
		RainPointMonitor? monitor = null;
		RainPointSessionRecovery? recovery = null;
		CountingTransport transport = new ();
		try
			{
			await client.LoginAsync (account.Email, account.Password, account.AreaCode, timeout.Token);
			DateTimeOffset originalExpiry = client.SessionExpiresAt!.Value;
			TestContext.Progress.WriteLine ($"Initial cloud session remaining: {(originalExpiry - DateTimeOffset.UtcNow).TotalMinutes:F1} minutes. Observer renewal is measured separately.");
			RainPointHub hub = await FindHub (client, timeout.Token);
			monitor = CreateMonitor (client, hub, transport);
			recovery = new RainPointSessionRecovery (client);
			Task recoveryRun = recovery.RunAsync (timeout.Token);
			Task monitorRun = monitor.RunAsync (timeout.Token);
			await WaitUntil (() => transport.Connections >= 1 && Decoded (monitor), TimeSpan.FromSeconds (90), timeout.Token,
				 "Initial authenticated MQTT connection and decoded status were not observed.");
			DateTimeOffset began = DateTimeOffset.UtcNow;
			for (int minute = 1; minute <= 12; minute++)
				{
				await DelayUntil (began.AddMinutes (minute), timeout.Token);
				Assert.That (monitorRun.IsCompleted || recoveryRun.IsCompleted, Is.False, "A background worker ended unexpectedly.");
				Assert.That (monitor.Current!.LastSuccessfulPollAt, Is.GreaterThan (DateTimeOffset.UtcNow.AddMinutes (-2)), "Decoded status became stale.");
				Assert.That (recovery.State, Is.Not.EqualTo (RainPointSessionState.AuthenticationRequired));
				TestContext.Progress.WriteLine ($"Read-only minute {minute}/12: MQTT connections={transport.Connections}; recovery={recovery.State}; status current.");
				}
			Assert.That (transport.Connections, Is.GreaterThanOrEqualTo (2), "A second real MQTT connection was not observed.");
			Assert.That (transport.LongestConnectedDuration, Is.GreaterThan (TimeSpan.FromMinutes (7)), "Repeated short connections do not establish observer-lifetime rollover.");
			await WaitUntil (() => monitor.State == RainPointMonitorState.PushConnected && Decoded (monitor), TimeSpan.FromSeconds (90), timeout.Token,
				 "MQTT was not connected at the end of observation.");
			Assert.That (await client.GetHomesAsync (timeout.Token), Has.Some.Property (nameof (RainPointHome.Id)).EqualTo (hub.HomeId));
			TestContext.Progress.WriteLine ($"Twelve-minute observation complete. Original cloud-session expiry crossed: {DateTimeOffset.UtcNow > originalExpiry}. No device writes or watering transition claim.");
			}
		catch (Exception error) when (error is not AssertionException && error is not SuccessException && error is not IgnoreException)
			{
			Assert.Fail ("Live observer endurance failed (" + error.GetType ().Name + "). Private account and transport details omitted.");
			}
		finally
			{
			if (recovery is not null)
				await recovery.StopAsync ();
			if (monitor is not null)
				await monitor.StopAsync ();
			await Logout (client);
			}
		}

	private static async Task<bool> ObserveRejection (RainPointCloudClient client, Func<bool> recoveryStarted, CancellationToken token)
		{
		DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds (90);
		while (DateTimeOffset.UtcNow < deadline)
			{
			if (!client.HasValidSession || recoveryStarted ())
				return true;
			try
				{
				await client.GetHomesAsync (token);
				}
			catch (RainPointException error) when (error.ApiCode is 1001 or 1004 || error.HttpStatus == System.Net.HttpStatusCode.Unauthorized) { return true; }
			catch (InvalidOperationException) when (!client.HasValidSession) { return true; }
			catch (Exception error) when (!token.IsCancellationRequested && error is HttpRequestException or OperationCanceledException)
				{
				// Polling already tolerates unavailable reads. Do not turn a timeout into presumed authentication rejection.
				TestContext.Progress.WriteLine ("Displacement read unavailable (" + error.GetType ().Name + "); retrying only the read.");
				}
			await Task.Delay (TimeSpan.FromSeconds (5), token);
			}
		return !client.HasValidSession || recoveryStarted ();
		}

	private static Account LoadAccount ()
		{
		string? path = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS");
		if (string.IsNullOrWhiteSpace (path))
			Assert.Ignore ("Set RAINPOINT_LIVE_SETTINGS to ignored private settings.");
		Account? account = JsonSerializer.Deserialize<Account> (File.ReadAllText (path!));
		Assert.That (account, Is.Not.Null);
		return account!;
		}

	private static async Task<RainPointHub> FindHub (RainPointCloudClient client, CancellationToken token)
		{
		List<RainPointHub> hubs = [];
		foreach (RainPointHome home in await client.GetHomesAsync (token))
			hubs.AddRange ((await client.GetHubsAsync (home.Id, token)).Where (hub => hub.Model is "HWG023WBRF" or "HWG023WBRF-V2"
				 && hub.Devices.Any (device => device.Model == "HTV345FRF")));
		Assert.That (hubs, Has.Count.EqualTo (1), "Exactly one matching hub is required.");
		return hubs[0];
		}

	private static RainPointMonitor CreateMonitor (RainPointCloudClient client, RainPointHub hub, CountingTransport transport)
		 => new (client, hub, new RainPointMonitorOptions { PollInterval = TimeSpan.FromSeconds (30) }, transport, Task.Delay, () => DateTimeOffset.UtcNow);

	private static bool Decoded (RainPointMonitor monitor) => monitor.Current?.LastSuccessfulPollAt.HasValue == true
		 && monitor.Current.Status.Timers.Any (timer => timer.Availability == TimerReadingAvailability.Decoded);

	private static async Task WaitUntil (Func<bool> condition, TimeSpan limit, CancellationToken token, string failure)
		{
		DateTimeOffset until = DateTimeOffset.UtcNow + limit;
		while (!condition () && DateTimeOffset.UtcNow < until)
			await Task.Delay (TimeSpan.FromSeconds (1), token);
		Assert.That (condition (), Is.True, failure);
		}

	private static async Task DelayUntil (DateTimeOffset until, CancellationToken token)
		{
		TimeSpan remaining = until - DateTimeOffset.UtcNow;
		if (remaining > TimeSpan.Zero)
			await Task.Delay (remaining, token);
		}

	private static async Task Logout (RainPointCloudClient client)
		{
		if (!client.HasValidSession)
			return;
		using CancellationTokenSource cleanup = new (TimeSpan.FromSeconds (15));
		try
			{
			await client.LogoutAsync (cleanup.Token);
			}
		catch (Exception error) when (error is not OutOfMemoryException) { TestContext.Progress.WriteLine ("Remote logout did not complete; client disposed."); }
		}

	private sealed class CountingTransport : IObserverTransport
		{
		private readonly MqttObserverTransport _inner = new ();
		private int _connections;
		private long _longestTicks;
		internal int Connections => Volatile.Read (ref _connections);
		internal TimeSpan LongestConnectedDuration => TimeSpan.FromTicks (Interlocked.Read (ref _longestTicks));
		public async Task RunAsync (ObserverCredentials credentials, Action connected, Action<byte[]> received, CancellationToken token)
			{
			DateTimeOffset? began = null;
			try
				{
				await _inner.RunAsync (credentials, () =>
					{
						began = DateTimeOffset.UtcNow;
						Interlocked.Increment (ref _connections);
						connected ();
					}, received, token);
				}
			finally
				{
				if (began.HasValue)
					{
					long ticks = (DateTimeOffset.UtcNow - began.Value).Ticks;
					if (ticks > Interlocked.Read (ref _longestTicks))
						Interlocked.Exchange (ref _longestTicks, ticks);
					}
				}
			}
		}

	private sealed class LoginResultObserver : DelegatingHandler
		{
		internal LoginResultObserver () : base (new HttpClientHandler { AllowAutoRedirect = false }) { }
		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken token)
			{
			HttpResponseMessage response;
			try
				{
				response = await base.SendAsync (request, token);
				}
			catch (Exception error) when (error is not OutOfMemoryException)
				{
				if (request.RequestUri!.AbsolutePath == "/auth/basic/app/login")
					TestContext.Progress.WriteLine ("Login transport failed: " + error.GetType ().Name + ". Private details omitted.");
				throw;
				}
			if (request.RequestUri!.AbsolutePath == "/auth/basic/app/login")
				{
				// Deserialize only the attributed envelope code; never print or persist tokens, headers or response bodies.
				try
					{
					ApiResult? result = JsonSerializer.Deserialize<ApiResult> (await response.Content.ReadAsStringAsync ());
					TestContext.Progress.WriteLine ($"Login result: HTTP {(int)response.StatusCode}; API code {result?.Code.ToString () ?? "absent"}.");
					}
				catch (JsonException) { TestContext.Progress.WriteLine ("Login response was not a decodable API envelope."); }
				}
			return response;
			}
		}

	private sealed class Account
		{
		[JsonPropertyName ("email")] public string Email { get; set; } = string.Empty;
		[JsonPropertyName ("password")] public string Password { get; set; } = string.Empty;
		[JsonPropertyName ("areaCode")] public string AreaCode { get; set; } = "44";
		}
	}
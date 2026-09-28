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
public sealed class SharedAccountLiveTests
	{
	[Test, Explicit ("Reads the paired home's current account role; no configuration or watering writes.")]
	public async Task PairedHomeReportsAccountRole ()
		{
		Account account = Load ("RAINPOINT_LIVE_SETTINGS");
		using RainPointCloudClient client = new ();
		using CancellationTokenSource timeout = new (TimeSpan.FromSeconds (90));
		try
			{
			await client.LoginAsync (account.Email, account.Password, account.AreaCode, timeout.Token);
			RainPointHub hub = await FindHub (client, timeout.Token);
			RainPointHomeDetails home = await client.GetHomeAsync (hub.HomeId, timeout.Token);
			Assert.That (home.IsOwner.HasValue && home.Role.HasValue, Is.True, "Home access must be explicitly reported.");
			TestContext.Progress.WriteLine ($"Paired-home access: owner={home.IsOwner}; role={home.Role}; app permits plan editing={home.IsOwner == true || home.Role == RainPointMemberRole.Administrator}.");
			}
		catch (Exception error) when (error is not AssertionException && error is not MultipleAssertException && error is not IgnoreException)
			{
			Assert.Fail ("Home-access observation failed (" + error.GetType ().Name + "). Private details omitted.");
			}
		finally
			{
			await Logout (client);
			}
		}

	[Test, Explicit ("Signs in two distinct, already shared accounts and observes the same hub concurrently. Requires RAINPOINT_LIVE_SECONDARY_SETTINGS. No writes, invites or watering.")]
	public async Task TwoAccountsKeepSharedHubSessionsAndObservers ()
		{
		Account primary = Load ("RAINPOINT_LIVE_SETTINGS");
		Account secondary = Load ("RAINPOINT_LIVE_SECONDARY_SETTINGS");
		Assert.That (string.Equals (primary.Email.Trim (), secondary.Email.Trim (), StringComparison.OrdinalIgnoreCase), Is.False,
			"Distinct accounts required; do not displace a session by repeating one account.");
		using HttpClient diagnosticHttp = new (new DiscoveryDiagnostics { InnerHandler = new HttpClientHandler { AllowAutoRedirect = false } })
			{
			Timeout = TimeSpan.FromSeconds (30),
			MaxResponseContentBufferSize = 4 * 1024 * 1024
			};
		using RainPointCloudClient first = new (diagnosticHttp);
		using RainPointCloudClient second = new ();
		using CancellationTokenSource timeout = new (TimeSpan.FromMinutes (7));
		RainPointMonitor? firstMonitor = null, secondMonitor = null;
		bool secondLoggedOut = false;
		try
			{
			TestContext.Progress.WriteLine ("Starting the first-account login.");
			await first.LoginAsync (primary.Email, primary.Password, primary.AreaCode, timeout.Token);
			RainPointHub firstHub = await FindHub (first, timeout.Token);
			RainPointHomeDetails homeDetails = await first.GetHomeAsync (firstHub.HomeId, timeout.Token);
			RainPointTimerStatus clock = await first.GetTimerStatusAsync (firstHub, firstHub.Devices.Single (item => item.Model == "HTV345FRF").Address, timeout.Token);
			TestContext.Progress.WriteLine ($"Read-only clock diagnostics: home timezone={homeDetails.TimeZoneName}; configured offset minutes={homeDetails.TimeZoneOffsetMinutes}; automatic broadcast={firstHub.AutomaticTimeBroadcastEnabled}; device local report={clock.ReportedAtLocal:O}; cloud change={clock.LastDataChange:O}.");
			TestContext.Progress.WriteLine ("First account authenticated. Spacing the distinct second-account login by two minutes to test concurrency independently of rapid-login throttling.");
			await Task.Delay (TimeSpan.FromMinutes (2), timeout.Token);
			TestContext.Progress.WriteLine ("Starting the second distinct-account login.");
			await second.LoginAsync (secondary.Email, secondary.Password, secondary.AreaCode, timeout.Token);
			RainPointHub secondHub = await FindHub (second, timeout.Token);
			Assert.That (secondHub.Id == firstHub.Id && secondHub.HomeId == firstHub.HomeId, Is.True, "Accounts must already share the same home and hub.");
			RainPointDevice firstTimer = firstHub.Devices.Single (item => item.Model == "HTV345FRF");
			RainPointDevice secondTimer = secondHub.Devices.Single (item => item.Model == "HTV345FRF");

			Assert.That (firstTimer.Id == secondTimer.Id && firstTimer.Address == secondTimer.Address, Is.True);
			Assert.That (await first.GetHomesAsync (timeout.Token), Has.Some.Property (nameof (RainPointHome.Id)).EqualTo (firstHub.HomeId),
				"Second-account sign-in must not invalidate the original account.");
			firstMonitor = new RainPointMonitor (first, firstHub, new RainPointMonitorOptions { PollInterval = TimeSpan.FromSeconds (15) });
			secondMonitor = new RainPointMonitor (second, secondHub, new RainPointMonitorOptions { PollInterval = TimeSpan.FromSeconds (15) });
			Task firstRun = firstMonitor.RunAsync (timeout.Token), secondRun = secondMonitor.RunAsync (timeout.Token);
			for (int i = 0; i < 90 && !(Ready (firstMonitor) && Ready (secondMonitor)); i++)
				await Task.Delay (TimeSpan.FromSeconds (1), timeout.Token);
			Assert.That (Ready (firstMonitor) && Ready (secondMonitor), Is.True, "Both accounts need simultaneous authenticated MQTT and decoded readings.");
			TestContext.Progress.WriteLine ("Both distinct accounts see the same paired hub/timer; both MQTT observers connected.");
			for (int round = 0; round < 8; round++)
				{
				await Task.Delay (TimeSpan.FromSeconds (10), timeout.Token);
				RainPointTimerStatus[] statuses = await Task.WhenAll (
					first.GetTimerStatusAsync (firstHub, firstTimer.Address, timeout.Token),
					second.GetTimerStatusAsync (secondHub, secondTimer.Address, timeout.Token));
				Assert.That (statuses.Select (item => item.Availability), Is.All.EqualTo (TimerReadingAvailability.Decoded));
				Assert.That (statuses.All (item => item.Zones.Count == 3), Is.True);
				Assert.That (first.HasValidSession && second.HasValidSession, Is.True);
				Assert.That (firstRun.IsCompleted || secondRun.IsCompleted, Is.False);
				Assert.That (Ready (firstMonitor) && Ready (secondMonitor), Is.True, "One account lost its simultaneous observer.");
				TestContext.Progress.WriteLine ($"Concurrent read round {round + 1}/8 passed for all three zones; both observers connected.");
				}
			await secondMonitor.StopAsync ();
			await second.LogoutAsync (timeout.Token);
			secondLoggedOut = true;
			await Task.Delay (TimeSpan.FromSeconds (15), timeout.Token);
			Assert.That (await first.GetHomesAsync (timeout.Token), Has.Some.Property (nameof (RainPointHome.Id)).EqualTo (firstHub.HomeId));
			Assert.That (Ready (firstMonitor), Is.True, "Logging out the other account must not end this observer.");
			TestContext.Progress.WriteLine ($"Other-account logout preserved the first session and observer. Accepted decoded pushes: first={firstMonitor.AcceptedPushCount}, second={secondMonitor.AcceptedPushCount}. No device or administration writes.");
			}
		catch (Exception error) when (error is not AssertionException && error is not MultipleAssertException && error is not IgnoreException)
			{
			if (error is RainPointException protocol)
				TestContext.Progress.WriteLine ($"Sanitized cloud error: API {protocol.ApiCode}, HTTP {protocol.HttpStatus}.");
			Assert.Fail ("Concurrent-account check failed (" + error.GetType ().Name + "). Private account and transport details omitted.");
			}
		finally
			{
			if (secondMonitor is not null)
				await secondMonitor.StopAsync ();
			if (firstMonitor is not null)
				await firstMonitor.StopAsync ();
			if (!secondLoggedOut)
				await Logout (second);
			await Logout (first);
			}
		}

	private static bool Ready (RainPointMonitor monitor) => monitor.State == RainPointMonitorState.PushConnected
		&& monitor.Current?.LastSuccessfulPollAt > DateTimeOffset.UtcNow.AddSeconds (-45)
		&& monitor.Current.Status.Timers.Any (item => item.Availability == TimerReadingAvailability.Decoded);

	private static async Task<RainPointHub> FindHub (RainPointCloudClient client, CancellationToken token)
		{
		List<RainPointHub> hubs = [];
		foreach (RainPointHome home in await client.GetHomesAsync (token))
			hubs.AddRange ((await client.GetHubsAsync (home.Id, token)).Where (hub => hub.Model is "HWG023WBRF" or "HWG023WBRF-V2"
				&& hub.Devices.Any (device => device.Model == "HTV345FRF")));
		Assert.That (hubs, Has.Count.EqualTo (1), "Exactly one matching shared hub required.");
		return hubs.Single ();
		}

	private static Account Load (string variable)
		{
		string? path = Environment.GetEnvironmentVariable (variable);
		if (string.IsNullOrWhiteSpace (path))
			Assert.Ignore ("Both private settings paths must be supplied explicitly.");
		Account account = JsonSerializer.Deserialize<Account> (File.ReadAllText (path!))!;
		Assert.That (!string.IsNullOrWhiteSpace (account.Email) && !string.IsNullOrWhiteSpace (account.Password), Is.True, "Private account settings are incomplete.");
		return account;
		}

	private static async Task Logout (RainPointCloudClient client)
		{
		if (!client.HasValidSession)
			return;
		using CancellationTokenSource timeout = new (TimeSpan.FromSeconds (15));
		try
			{
			await client.LogoutAsync (timeout.Token);
			}
		catch (Exception error) when (error is not OutOfMemoryException) { TestContext.Progress.WriteLine ("Remote logout unavailable; local client disposed."); }
		}

	private sealed class DiscoveryDiagnostics : DelegatingHandler
		{
		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken token)
			{
			HttpResponseMessage response = await base.SendAsync (request, token);
			if (request.RequestUri?.AbsolutePath == "/app/device/getDeviceByHid" && response.IsSuccessStatusCode)
				{
				ApiResult<List<EnabledHub>>? envelope = JsonSerializer.Deserialize<ApiResult<List<EnabledHub>>> (await response.Content.ReadAsStringAsync ());
				foreach (EnabledHub hub in envelope?.Data ?? [])
					if (hub.Model is "HWG023WBRF" or "HWG023WBRF-V2")
						foreach (EnabledDevice device in hub.Devices.Where (item => item.Model == "HTV345FRF"))
							TestContext.Progress.WriteLine ($"Discovery flags: hub enabled={hub.Enabled?.ToString () ?? "missing"}; timer enabled={device.Enabled?.ToString () ?? "missing"}.");
				}
			return response;
			}
		}
	private sealed class EnabledHub
		{
		[JsonPropertyName ("model")] public string Model { get; set; } = string.Empty;
		[JsonPropertyName ("enabled")]
		public int? Enabled
			{
			get; set;
			}
		[JsonPropertyName ("subDevices")] public List<EnabledDevice> Devices { get; set; } = [];
		}
	private sealed class EnabledDevice
		{
		[JsonPropertyName ("model")] public string Model { get; set; } = string.Empty;
		[JsonPropertyName ("enabled")]
		public int? Enabled
			{
			get; set;
			}
		}

	private sealed class Account
		{
		[JsonPropertyName ("email")] public string Email { get; set; } = string.Empty;
		[JsonPropertyName ("password")] public string Password { get; set; } = string.Empty;
		[JsonPropertyName ("areaCode")] public string AreaCode { get; set; } = "44";
		}
	}
// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
namespace RainPointClient.Tests;

[TestFixture]
public sealed class SessionRecoveryTests
	{
	private ScriptedHandler _handler = null!;
	private HttpClient _http = null!;
	private RainPointCloudClient _client = null!;
	private DateTimeOffset _now;
	private int _credentials;
	[SetUp]
	public void Setup ()
		{
		_handler = new ();
		_http = new (_handler, false);
		_client = new (_http);
		_now = DateTimeOffset.UtcNow;
		_credentials = 0;
		}
	[TearDown]
	public void Teardown ()
		{
		_client.Dispose ();
		_http.Dispose ();
		_handler.Dispose ();
		}
	private async Task Login (bool refresh = true)
		{
		_handler.Reply ("{\"code\":0,\"data\":{\"token\":\"fixture\",\"tokenExpired\":120" + (refresh ? ",\"refreshToken\":\"refresh\"" : "") + "}}");
		await _client.LoginAsync ("fixture@example.invalid", "password", "44");
		_now = _client.SessionExpiresAt!.Value.AddSeconds (-30);
		}
	private Task<RainPointCredentials?> Credentials (CancellationToken token)
		{
		token.ThrowIfCancellationRequested ();
		_credentials++;
		return Task.FromResult<RainPointCredentials?> (new ("fixture@example.invalid", "password", "44"));
		}
	private RainPointSessionRecovery Worker (bool credentials = false) => new (_client, credentials ? Credentials : null, () => _now, Task.Delay);
	private void Fresh () => _handler.Reply ("{\"code\":0,\"data\":{\"token\":\"fresh\",\"refreshToken\":\"new-refresh\",\"tokenExpired\":3600}}");
	[Test]
	public async Task RefreshNearExpiryRotatesWithoutPasswordOrDeviceRequests ()
		{
		await Login ();
		var worker = Worker (true);
		Fresh ();
		await worker.CheckAsync ();
		Assert.That (worker.State, Is.EqualTo (RainPointSessionState.Healthy));
		Assert.That (_handler.Requests.Last ().Path, Is.EqualTo ("/auth/basic/app/token/refresh"));
		Assert.That (_credentials, Is.Zero);
		_handler.Reply ("{\"code\":0,\"data\":[]}");
		await _client.GetHomesAsync ();
		Assert.That (_handler.Requests.Last ().Token, Is.EqualTo ("fresh"));
		}
	[Test]
	public async Task HealthySessionDoesNotRefreshOrRequestCredentials ()
		{
		await Login ();
		_now = _client.SessionExpiresAt!.Value.AddMinutes (-2);
		var worker = Worker (true);
		await worker.CheckAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		Assert.That (_credentials, Is.Zero);
		}
	[Test]
	public async Task ConcurrentChecksPerformOneRefresh ()
		{
		await Login ();
		Fresh ();
		var worker = Worker ();
		await Task.WhenAll (Enumerable.Range (0, 8).Select (_ => worker.CheckAsync ()));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}
	[Test]
	public async Task ValidSessionWithoutRefreshDoesNotLogInEarly ()
		{
		await Login (false);
		var worker = Worker (true);
		await worker.CheckAsync ();
		Assert.That (_credentials, Is.Zero);
		Assert.That (worker.State, Is.EqualTo (RainPointSessionState.Healthy));
		}
	[Test]
	public async Task MissingCredentialPolicyRequiresAuthenticationAfterExpiry ()
		{
		await Login (false);
		_now = _client.SessionExpiresAt!.Value.AddSeconds (1);
		var worker = Worker ();
		await worker.CheckAsync ();
		Assert.That (worker.State, Is.EqualTo (RainPointSessionState.AuthenticationRequired));
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		}
	[Test]
	public async Task RejectedSessionAllowsOnlyOnePasswordRecoveryPerWorker ()
		{
		await Login ();
		_handler.Reply ("{\"code\":1004}");
		var worker = Worker (true);
		await worker.CheckAsync ();
		_now = _now.AddMinutes (3);
		await worker.CheckAsync ();
		Assert.That (worker.State, Is.EqualTo (RainPointSessionState.CoolingDown));
		Assert.That (_credentials, Is.Zero);
		_now = _now.AddMinutes (2);
		Fresh ();
		await worker.CheckAsync ();
		Assert.That (_credentials, Is.EqualTo (1));
		Assert.That (_handler.Requests.Last ().Path, Is.EqualTo ("/auth/basic/app/login"));
		_handler.Reply ("{\"code\":1004}");
		await Assert.ThrowsAsync<RainPointException> (async () => await _client.GetHomesAsync ());
		await worker.CheckAsync ();
		await worker.CheckAsync ();
		Assert.That (worker.State, Is.EqualTo (RainPointSessionState.AuthenticationRequired));
		Assert.That (_credentials, Is.EqualTo (1));
		Assert.That (_handler.Requests, Has.Count.EqualTo (4));
		}
	[Test]
	public async Task RejectedWriteIsNeverReplayedByRecovery ()
		{
		await Login ();
		var hub = new RainPointHub { Id = 101, Model = "HWG023WBRF", HomeId = 5, DeviceName = "fixture", ProductKey = "fixture", Devices = new[] { new RainPointDevice { Address = 2, Model = "HTV345FRF" } } };
		_handler.Reply ("{\"code\":1004}");
		await Assert.ThrowsAsync<RainPointException> (async () => await _client.StartWateringAsync (hub, 2, 1, TimeSpan.FromMinutes (1)));
		Fresh ();
		var worker = Worker (true);
		await worker.CheckAsync ();
		Assert.That (_credentials, Is.Zero);
		_now = _now.AddMinutes (2);
		await worker.CheckAsync ();
		Assert.That (_handler.Requests.Count (x => x.Path.Contains ("controlWorkMode")), Is.EqualTo (1));
		Assert.That (_handler.Requests.Last ().Path, Is.EqualTo ("/auth/basic/app/login"));
		}
	[TestCase (1001)]
	[TestCase (1004)]
	public async Task RefreshRejectionWithoutPolicyNeverLogsIn (int code)
		{
		await Login ();
		_handler.Reply ("{\"code\":" + code + "}");
		var worker = Worker ();
		await worker.CheckAsync ();
		_now = _now.AddMinutes (10);
		await worker.CheckAsync ();
		Assert.That (worker.State, Is.EqualTo (RainPointSessionState.AuthenticationRequired));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}
	[Test]
	public async Task ThrottleHonorsRetryAfterBeforeAnotherRefresh ()
		{
		await Login ();
		_handler.Steps.Enqueue ((_, _) => { var response = new HttpResponseMessage ((HttpStatusCode)429); response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue (TimeSpan.FromMinutes (10)); return Task.FromResult (response); });
		var worker = Worker ();
		await worker.CheckAsync ();
		_now = _now.AddMinutes (5);
		await worker.CheckAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		Assert.That (worker.State, Is.EqualTo (RainPointSessionState.CoolingDown));
		_now = _now.AddMinutes (6);
		Fresh ();
		await worker.CheckAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (3));
		}
	[Test]
	public async Task TransportFailureBacksOffWithoutFallingBackToPassword ()
		{
		await Login ();
		_handler.Steps.Enqueue ((_, _) => throw new HttpRequestException ("private details"));
		var worker = Worker (true);
		await worker.CheckAsync ();
		await worker.CheckAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		Assert.That (_credentials, Is.Zero);
		_now = _now.AddMinutes (1);
		Fresh ();
		await worker.CheckAsync ();
		Assert.That (worker.State, Is.EqualTo (RainPointSessionState.Healthy));
		}
	[TestCase (false)]
	[TestCase (true)]
	public async Task MissingOrFailingCredentialProviderIsNotRepeated (bool throws)
		{
		await Login (false);
		_now = _client.SessionExpiresAt!.Value.AddSeconds (1);
		var worker = new RainPointSessionRecovery (_client, _ => { _credentials++; if (throws) throw new InvalidOperationException ("private"); return Task.FromResult<RainPointCredentials?> (null); }, () => _now, Task.Delay);
		await worker.CheckAsync ();
		_now = _now.AddMinutes (2);
		await worker.CheckAsync ();
		_now = _now.AddHours (1);
		await worker.CheckAsync ();
		Assert.That (_credentials, Is.EqualTo (1));
		Assert.That (worker.State, Is.EqualTo (RainPointSessionState.AuthenticationRequired));
		}
	[Test]
	public async Task FailedPasswordRecoveryDoesNotLoop ()
		{
		await Login (false);
		_now = _client.SessionExpiresAt!.Value.AddSeconds (1);
		_handler.Reply ("{\"code\":1004}");
		var worker = Worker (true);
		await worker.CheckAsync ();
		_now = _now.AddMinutes (2);
		await worker.CheckAsync ();
		_now = _now.AddHours (1);
		await worker.CheckAsync ();
		Assert.That (_credentials, Is.EqualTo (1));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		Assert.That (worker.State, Is.EqualTo (RainPointSessionState.AuthenticationRequired));
		}
	[Test]
	public async Task CancellationBeforeCheckSendsNothing ()
		{
		await Login ();
		using var stop = new CancellationTokenSource ();
		stop.Cancel ();
		var worker = Worker ();
		await Assert.CatchAsync<OperationCanceledException> (async () => await worker.CheckAsync (stop.Token));
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		}
	[Test]
	public async Task FailingEventHandlerDoesNotPreventOtherNotificationsOrRefresh ()
		{
		await Login ();
		Fresh ();
		var worker = Worker ();
		var states = new List<RainPointSessionState> ();
		worker.StateChanged += (_, _) => throw new InvalidOperationException ();
		worker.StateChanged += (_, e) => states.Add (e.State);
		await worker.CheckAsync ();
		Assert.That (states, Is.EqualTo (new[] { RainPointSessionState.Renewing, RainPointSessionState.Healthy }));
		}
	[Test]
	public async Task StopCancelsPendingRefreshAndReleasesWorkerOwnership ()
		{
		await Login ();
		var entered = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		_handler.Steps.Enqueue (async (_, token) => { entered.TrySetResult (true); await Task.Delay (Timeout.Infinite, token); throw new InvalidOperationException (); });
		var worker = Worker ();
		Task run = worker.RunAsync ();
		try
			{
			Assert.That (await Task.WhenAny (entered.Task, Task.Delay (5000)), Is.SameAs (entered.Task));
			var second = Worker ();
			Assert.Throws<InvalidOperationException> (() => second.RunAsync ());
			Assert.Throws<InvalidOperationException> (() => _client.LoginAsync ("fixture@example.invalid", "password", "44"));
			await Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.LogoutAsync ());
			}
		finally { await worker.StopAsync (); await run; }
		Assert.That (worker.State, Is.EqualTo (RainPointSessionState.Stopped));
		Assert.Throws<InvalidOperationException> (() => worker.RunAsync ());
		_handler.Reply ("{\"code\":0}");
		await _client.LogoutAsync ();
		Assert.That (_client.HasValidSession, Is.False);
		}
	[Test]
	public void WorkerCannotStartBeforeLogin ()
		{
		var worker = Worker ();
		Assert.Throws<InvalidOperationException> (() => worker.RunAsync ());
		Assert.That (_handler.Requests, Is.Empty);
		}
	[Test]
	public async Task ServerInvalidationWaitsFullCooldownBeforeRequestingCredentials ()
		{
		await Login ();
		_now = DateTimeOffset.UtcNow;
		var worker = Worker (true);
		await worker.CheckAsync ();
		_handler.Reply ("{\"code\":1004}");
		await Assert.ThrowsAsync<RainPointException> (async () => await _client.GetHomesAsync ());
		await worker.CheckAsync ();
		Assert.That (worker.State, Is.EqualTo (RainPointSessionState.CoolingDown));
		_now = _now.AddSeconds (119);
		await worker.CheckAsync ();
		Assert.That (_credentials, Is.Zero, "Do not fetch the password while waiting.");
		Assert.That (_handler.Requests, Has.Count.EqualTo (2), "No login or replay during cooldown.");
		_now = _now.AddSeconds (1);
		Fresh ();
		await worker.CheckAsync ();
		Assert.That (worker.State, Is.EqualTo (RainPointSessionState.Healthy));
		Assert.That (_credentials, Is.EqualTo (1));
		Assert.That (_handler.Requests, Has.Count.EqualTo (3));
		Assert.That (_handler.Requests.Last ().Path, Is.EqualTo ("/auth/basic/app/login"));
		}

	[Test]
	public async Task StopDuringCredentialCooldownNeverFetchesCredentialsOrLogsIn ()
		{
		await Login (false);
		_now = _client.SessionExpiresAt!.Value.AddSeconds (1);
		var entered = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		var worker = new RainPointSessionRecovery (_client, Credentials, () => _now, (_, token) =>
			{
				entered.TrySetResult (true);
				return Task.Delay (Timeout.Infinite, token);
			});
		Task run = worker.RunAsync ();
		try
			{
			Assert.That (await Task.WhenAny (entered.Task, Task.Delay (5000)), Is.SameAs (entered.Task));
			Assert.That (worker.State, Is.EqualTo (RainPointSessionState.CoolingDown));
			}
		finally { await worker.StopAsync (); await run; }
		Assert.That (_credentials, Is.Zero);
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		Assert.That (worker.State, Is.EqualTo (RainPointSessionState.Stopped));
		_handler.Reply ("{\"code\":0}");
		await _client.LogoutAsync ();
		Assert.That (_client.HasValidSession, Is.False);
		}

	}
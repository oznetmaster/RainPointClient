// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using RainPointClient.Protocol;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class MonitorTests
	{
	private ScriptedHandler _handler = null!;
	private HttpClient _http = null!;
	private RainPointCloudClient _client = null!;
	private RainPointMonitor? _monitor;

	[SetUp]
	public async Task SetUp ()
		{
		_handler = new ScriptedHandler ();
		_http = new HttpClient (_handler, false);
		_client = new RainPointCloudClient (_http);
		_handler.Reply ("""{"code":0,"data":{"token":"fixture","tokenExpired":3600}}""");
		await _client.LoginAsync ("fixture@example.invalid", "fixture", "44");
		}

	[TearDown]
	public async Task TearDown ()
		{
		if (_monitor is not null)
			await _monitor.StopAsync ();
		_client.Dispose ();
		_http.Dispose ();
		_handler.Dispose ();
		}

	private const string Poll = """{"code":0,"data":[{"mid":236547,"status":[{"id":"D01","value":"11#19D800","time":1799999980000}]}]}""";
	private const string Observer = """{"code":0,"data":{"deviceName":"observer","productKey":"observer-key","deviceSecret":"fixture-secret","mqttHostUrl":"fixture.aliyuncs.com:1883"}}""";

	[Test]
	public async Task ObserverRegistrationUsesTypedFullEnvelopeAndOriginalAuth ()
		{
		_handler.Reply (Observer);
		ObserverCredentials credentials = await _client.GetObserverAsync (PushTests.Hub (), CancellationToken.None);
		CapturedRequest request = _handler.Requests.Last ();
		Assert.That (request.Path, Is.EqualTo ("/app/device/subscribeStatus"));
		Assert.That (request.Token, Is.EqualTo ("fixture"));
		Assert.That (request.Body, Does.Contain ("\"hid\":\"42\"").And.Contain ("\"hidList\":[\"42\"]").And.Contain ("\"unsubscribe\":[]").And.Contain ("\"mid\":236547"));
		Assert.That (credentials.SessionIdentity, Is.SameAs (_client.SessionIdentity));
		}

	[Test]
	public async Task PushAndManualPollMergeAndStopAwaitsTransport ()
		{
		_handler.Reply (Poll);
		_handler.Reply (Observer);
		FakeTransport transport = new ();
		_monitor = new RainPointMonitor (_client, PushTests.Hub (), new RainPointMonitorOptions (), transport,
			 (_, token) => Task.Delay (Timeout.Infinite, token), () => PushTests.Now);
		_monitor.StatusReceived += (_, _) => throw new InvalidOperationException ("Failing consumer must not stop other consumers.");
		int notifications = 0;
		_monitor.StatusReceived += (_, _) => Interlocked.Increment (ref notifications);
		Task running = _monitor.RunAsync ();
		await Within (transport.Started.Task);
		transport.Received! (PushTests.Frame (PushTests.TimerValue ()));
		Assert.That (_monitor.AcceptedPushCount, Is.EqualTo (1));
		Assert.That (_monitor.Current!.Status.Timers.Single ().Zones[0].IsOpen, Is.True);
		transport.Received (PushTests.Frame (PushTests.TimerValue (), mid: "136547"));
		Assert.That (_monitor.RejectedPushCount, Is.EqualTo (1));
		_handler.Reply (Poll);
		await _monitor.RefreshAsync ();
		Assert.That (_monitor.Current.Status.Timers.Single ().Zones[0].IsOpen, Is.True);
		Assert.That (_monitor.Current.Timers.Single ().Source, Is.EqualTo (RainPointUpdateSource.Push));
		await _monitor.StopAsync ();
		await Within (running);
		Assert.That (transport.Stopped, Is.True);
		Assert.That (_monitor.State, Is.EqualTo (RainPointMonitorState.Stopped));
		Assert.That (notifications, Is.EqualTo (3));
		Assert.That (_handler.Requests.Any (request => request.Path.Contains ("controlWorkMode")), Is.False);
		}

	[Test]
	public async Task DuplicateMonitorIsRejectedAndCancellationReleasesClient ()
		{
		_handler.Reply (Poll);
		_handler.Reply (Observer);
		FakeTransport transport = new ();
		_monitor = new RainPointMonitor (_client, PushTests.Hub (), new RainPointMonitorOptions (), transport,
			 (_, token) => Task.Delay (Timeout.Infinite, token), () => PushTests.Now);
		_ = _monitor.RunAsync ();
		await Within (transport.Started.Task);
		RainPointMonitor other = new (_client, PushTests.Hub (), new RainPointMonitorOptions { EnablePush = false });
		Assert.Throws<InvalidOperationException> (() => other.RunAsync ());
		await _monitor.StopAsync ();
		using CancellationTokenSource cancellation = new ();
		cancellation.Cancel ();
		await other.RunAsync (cancellation.Token);
		Assert.That (other.State, Is.EqualTo (RainPointMonitorState.Stopped));
		}

	[Test]
	public async Task ExpiredSessionWaitsForAuthenticationWithoutRepeatedRequests ()
		{
		_handler.Reply ("{\"code\":1001}");
		Assert.ThrowsAsync<RainPointException> (async () => await _client.GetHomesAsync ());
		TaskCompletionSource<bool> needsAuth = new (TaskCreationOptions.RunContinuationsAsynchronously);
		_monitor = new RainPointMonitor (_client, PushTests.Hub (), new RainPointMonitorOptions (), new FakeTransport (),
			 (_, token) => Task.Delay (Timeout.Infinite, token), () => PushTests.Now);
		_monitor.StateChanged += (_, args) => { if (args.State == RainPointMonitorState.AuthenticationRequired) needsAuth.TrySetResult (true); };
		_ = _monitor.RunAsync ();
		await Within (needsAuth.Task);
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[Test]
	public async Task StopCancelsAndDrainsConcurrentManualRefresh ()
		{
		_handler.Reply (Poll);
		TaskCompletionSource<bool> initial = new (TaskCreationOptions.RunContinuationsAsynchronously);
		_monitor = new RainPointMonitor (_client, PushTests.Hub (), new RainPointMonitorOptions { EnablePush = false }, new FakeTransport (),
			 (_, token) => { initial.TrySetResult (true); return Task.Delay (Timeout.Infinite, token); }, () => PushTests.Now);
		_ = _monitor.RunAsync ();
		await Within (initial.Task);
		TaskCompletionSource<bool> reading = new (TaskCreationOptions.RunContinuationsAsynchronously);
		_handler.Steps.Enqueue (async (_, token) =>
		{
			reading.TrySetResult (true);
			await Task.Delay (Timeout.Infinite, token);
			throw new InvalidOperationException ("Unreachable");
		});
		Task refresh = _monitor.RefreshAsync ();
		await Within (reading.Task);
		await Within (_monitor.StopAsync ());
		Assert.That (refresh.IsCompleted, Is.True);
		Assert.ThrowsAsync<TaskCanceledException> (async () => await refresh);
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _monitor.RefreshAsync ());
		}

	[Test]
	public async Task PollingHonoursServerCooldown ()
		{
		TaskCompletionSource<TimeSpan> waited = new (TaskCreationOptions.RunContinuationsAsynchronously);
		_handler.Steps.Enqueue ((_, _) => throw new RainPointException ("Throttled", retryAfter: TimeSpan.FromSeconds (120)));
		_monitor = new RainPointMonitor (_client, PushTests.Hub (), new RainPointMonitorOptions { EnablePush = false }, new FakeTransport (),
			 (delay, token) => { waited.TrySetResult (delay); return Task.Delay (Timeout.Infinite, token); }, () => PushTests.Now);
		_ = _monitor.RunAsync ();
		await Within (waited.Task);
		Assert.That (await waited.Task, Is.EqualTo (TimeSpan.FromSeconds (120)));
		}

	[Test]
	public async Task ObserverIsDisconnectedBeforeItsCredentialsExpire ()
		{
		_handler.Reply (Poll);
		_handler.Reply (Observer);
		FakeTransport transport = new ();
		DateTimeOffset now = PushTests.Now;
		TaskCompletionSource<bool> renewed = new (TaskCreationOptions.RunContinuationsAsynchronously);
		_monitor = new RainPointMonitor (_client, PushTests.Hub (), new RainPointMonitorOptions (), transport,
			 (delay, token) =>
			 {
				 if (delay == TimeSpan.FromSeconds (10))
					 {
					 now = now.AddMinutes (10);
					 return Task.CompletedTask;
					 }
				 if (transport.Stopped)
					 renewed.TrySetResult (true);
				 return Task.Delay (Timeout.Infinite, token);
			 }, () => now);
		_ = _monitor.RunAsync ();
		await Within (renewed.Task);
		Assert.That (transport.Stopped, Is.True);
		Assert.That (_monitor.State, Is.EqualTo (RainPointMonitorState.Reconnecting));
		}

	private static async Task Within (Task task)
		{
		Assert.That (await Task.WhenAny (task, Task.Delay (5000)), Is.SameAs (task), "The offline operation did not finish.");
		await task;
		}

	private sealed class FakeTransport : IObserverTransport
		{
		internal TaskCompletionSource<bool> Started { get; } = new (TaskCreationOptions.RunContinuationsAsynchronously);
		internal Action<byte[]>? Received
			{
			get; private set;
			}
		internal bool Stopped
			{
			get; private set;
			}
		public async Task RunAsync (ObserverCredentials credentials, Action connected, Action<byte[]> received, CancellationToken token)
			{
			Received = received;
			connected ();
			Started.TrySetResult (true);
			try
				{
				await Task.Delay (Timeout.Infinite, token);
				}
			finally { Stopped = true; }
			}
		}
	}
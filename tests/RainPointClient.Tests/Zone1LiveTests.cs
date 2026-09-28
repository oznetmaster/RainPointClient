// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using MQTTnet;

using NUnit.Framework;

using RainPointClient.Protocol;

namespace RainPointClient.Tests;

[TestFixture, NonParallelizable, Category ("Live"), Category ("Actuation")]
public sealed class Zone1LiveTests
	{
	[Test, Explicit ("Stop-only MQTT diagnostics; requires RAINPOINT_LIVE_ZONE1=stop. Never starts watering.")]
	public async Task StopOnlyObservesZone1Feedback ()
		{
		if (Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_ZONE1") != "stop")
			Assert.Ignore ("Stop-only opt-in is required.");
		string? path = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS");
		if (string.IsNullOrWhiteSpace (path))
			Assert.Ignore ("Private account settings are required.");
		Account account = JsonSerializer.Deserialize<Account> (File.ReadAllText (path!))!;
		using RainPointCloudClient client = new ();
		using CancellationTokenSource lifetime = new (TimeSpan.FromSeconds (100));
		await client.LoginAsync (account.Email, account.Password, account.AreaCode, lifetime.Token);
		RainPointHub? hub = null;
		foreach (RainPointHome home in await client.GetHomesAsync (lifetime.Token))
			foreach (RainPointHub item in await client.GetHubsAsync (home.Id, lifetime.Token))
				if (item.Model is "HWG023WBRF" or "HWG023WBRF-V2" && item.Devices.Any (device => device.Model == "HTV345FRF"))
					{
					Assert.That (hub, Is.Null);
					hub = item;
					}
		Assert.That (hub, Is.Not.Null);
		RainPointDevice timer = hub!.Devices.Single (item => item.Model == "HTV345FRF");
		ObserverCredentials credentials = await client.GetObserverAsync (hub, lifetime.Token);
		TaskCompletionSource<bool> connected = new (TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource<bool> packet = new (TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource<bool> decoded = new (TaskCreationOptions.RunContinuationsAsynchronously);
		string capturePath = Path.Combine (Path.GetDirectoryName (Path.GetFullPath (path!))!, "mqtt-stop-" + Guid.NewGuid ().ToString ("N") + ".jsonl");
		int count = 0;
		MqttObserverTransport transport = new (() =>
		{
			var mqtt = new MqttFactory ().CreateMqttClient ();
			mqtt.ApplicationMessageReceivedAsync += args =>
			  {
				  var payload = args.ApplicationMessage.PayloadSegment;
				  if (payload.Array is not null && payload.Count <= 8192 && Interlocked.Increment (ref count) <= 20)
					  {
					  byte[] bytes = new byte[payload.Count];
					  Array.Copy (payload.Array, payload.Offset, bytes, 0, payload.Count);
					  // Private diagnostics beside ignored settings; never attach frames to NUnit output.
					  File.AppendAllText (capturePath, JsonSerializer.Serialize (new Capture
						  {
						  ExpectedTopic = args.ApplicationMessage.Topic == $"/sys/{credentials.ProductKey}/{credentials.DeviceName}/thing/service/property/set",
						  Payload = System.Text.Encoding.UTF8.GetString (bytes),
						  Decodes = PushDecoder.Decode (bytes, hub, DateTimeOffset.UtcNow) is not null
						  }) + Environment.NewLine);
					  Report ("Captured bounded MQTT packet privately; no payload or account identifiers printed.");
					  packet.TrySetResult (true);
					  }
				  return Task.CompletedTask;
			  };
			return mqtt;
		});
		using CancellationTokenSource observer = CancellationTokenSource.CreateLinkedTokenSource (lifetime.Token);
		Task running = transport.RunAsync (credentials, () => connected.TrySetResult (true), bytes =>
		{
			PushReading? reading = PushDecoder.Decode (bytes, hub, DateTimeOffset.UtcNow);
			RainPointZoneStatus? zone = reading?.Timers.SingleOrDefault (item => item.Address == timer.Address)?.Zones.SingleOrDefault (item => item.Zone == 1);
			if (zone?.IsOpen == false)
				{
				Report ($"Typed MQTT zone-1 feedback: closed; usage={zone.LastWaterUsageLitres} L.");
				decoded.TrySetResult (true);
				}
		}, observer.Token);
		try
			{
			Assert.That (await Task.WhenAny (connected.Task, running, Task.Delay (30000, lifetime.Token)), Is.SameAs (connected.Task), "Observer did not connect; no command sent.");
			Assert.That (await StopZone1 (client, hub, timer), Is.True);
			Assert.That (await Task.WhenAny (packet.Task, Task.Delay (30000, lifetime.Token)), Is.SameAs (packet.Task), "No MQTT packet arrived after stop.");
			Assert.That (await Task.WhenAny (decoded.Task, Task.Delay (10000, lifetime.Token)), Is.SameAs (decoded.Task), "No typed zone-1 closed reading arrived on the expected MQTT topic.");
			await Task.Delay (5000, lifetime.Token);
			RainPointTimerStatus status = await client.GetTimerStatusAsync (hub, timer.Address, lifetime.Token);
			Report ($"Stop-only final zone-1 reading: open={status.Zones.Single (item => item.Zone == 1).IsOpen}.");
			Assert.That (status.Zones.Single (item => item.Zone == 1).IsOpen, Is.False);
			}
		finally
			{
			observer.Cancel ();
			try
				{
				await running;
				}
			catch (OperationCanceledException) { }
			using CancellationTokenSource cleanup = new (TimeSpan.FromSeconds (15));
			try
				{
				await client.LogoutAsync (cleanup.Token);
				}
			catch (Exception error) when (error is not OutOfMemoryException) { Report ("Remote logout did not complete."); }
			}
		}

	private sealed class Capture
		{
		[JsonPropertyName ("expectedTopic")]
		public bool ExpectedTopic
			{
			get; set;
			}
		[JsonPropertyName ("decodes")]
		public bool Decodes
			{
			get; set;
			}
		[JsonPropertyName ("payload")] public string Payload { get; set; } = string.Empty;
		}

	[Test, Explicit ("Physically waters zone 1 once for 60 seconds. Requires both private settings and RAINPOINT_LIVE_ZONE1=60.")]
	public Task OneMinuteZone1CycleReportsMqttTransitions () => RunBoundedZone (1);

	[Test, Explicit ("Starts zone-1 misting once with one configured minute and 10/20-second intervals; sends cleanup stop. Requires RAINPOINT_LIVE_ZONE1=misting60.")]
	public Task OneMinuteZone1MistingReportsMqttTransitions () => RunBoundedZone (2);

	[Test, Explicit ("Starts zone-1 cycle-and-soak once: five watering minutes, one-minute bursts/pauses, then stops at 150 seconds. Requires RAINPOINT_LIVE_ZONE1=cycle150. Physical pause/resumption needs observation.")]
	public Task BoundedZone1CycleAndSoakReportsMqttTransitions () => RunBoundedZone (3);

	[Test, Explicit ("Physically waters zone 2 once for 60 seconds. Requires RAINPOINT_LIVE_ZONE=zone2-60.")]
	public Task OneMinuteZone2ReportsMqttTransitions () => RunBoundedZone (1, 2);

	[Test, Explicit ("Physically waters zone 3 once for 60 seconds. Requires RAINPOINT_LIVE_ZONE=zone3-60.")]
	public Task OneMinuteZone3ReportsMqttTransitions () => RunBoundedZone (1, 3);

	private async Task RunBoundedZone (int mode, int zoneNumber = 1)
		{
		bool misting = mode == 2;
		bool cycle = mode == 3;
		int stopAfterSeconds = cycle ? 150 : 65;
		string optIn = cycle ? "cycle150" : misting ? "misting60" : "60";
		if (zoneNumber != 1)
			optIn = $"zone{zoneNumber}-60";
		if (Environment.GetEnvironmentVariable (zoneNumber == 1 ? "RAINPOINT_LIVE_ZONE1" : "RAINPOINT_LIVE_ZONE") != optIn)
			Assert.Ignore ($"Set the explicit zone-{zoneNumber} actuation opt-in for the selected mode.");
		string? path = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS");
		if (string.IsNullOrWhiteSpace (path))
			Assert.Ignore ("Set RAINPOINT_LIVE_SETTINGS to the private settings file.");
		Account account = JsonSerializer.Deserialize<Account> (File.ReadAllText (path!))!;
		using RainPointCloudClient client = new ();
		using CancellationTokenSource lifetime = new (TimeSpan.FromMinutes (5));
		RainPointMonitor? monitor = null;
		RainPointHub? hub = null;
		RainPointDevice? timer = null;
		bool startAttempted = false;
		bool stopAttempted = false;
		bool stopAccepted = false;
		long startedAt = long.MaxValue;
		long stopSentAt = long.MaxValue;
		TaskCompletionSource<bool> connected = new (TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource<bool> polled = new (TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource<DateTimeOffset> opened = new (TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource<DateTimeOffset> closed = new (TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource<DateTimeOffset> paused = new (TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource<DateTimeOffset> resumed = new (TaskCreationOptions.RunContinuationsAsynchronously);
		try
			{
			await client.LoginAsync (account.Email, account.Password, account.AreaCode, lifetime.Token);
			foreach (RainPointHome home in await client.GetHomesAsync (lifetime.Token))
				foreach (RainPointHub candidate in await client.GetHubsAsync (home.Id, lifetime.Token))
					if (candidate.Model is "HWG023WBRF" or "HWG023WBRF-V2" && candidate.Devices.Any (item => item.Model == "HTV345FRF"))
						{
						Assert.That (hub, Is.Null, "Actuation requires exactly one matching hub.");
						hub = candidate;
						}
			Assert.That (hub, Is.Not.Null, "No supported hub found; nothing will be actuated.");
			RainPointDevice[] timers = hub!.Devices.Where (item => item.Model == "HTV345FRF").ToArray ();
			Assert.That (timers, Has.Length.EqualTo (1), "Actuation requires exactly one matching timer.");
			timer = timers[0];
			MqttObserverTransport transport = new (() =>
			{
				var mqtt = new MqttFactory ().CreateMqttClient ();
				mqtt.DisconnectedAsync += args =>
					{
						Report ($"MQTT disconnect: {args.Reason}; error type={args.Exception?.GetType ().Name ?? "none"}.");
						return Task.CompletedTask;
					};
				mqtt.ApplicationMessageReceivedAsync += args =>
					{
						Report ($"MQTT packet received: {args.ApplicationMessage.PayloadSegment.Count} bytes.");
						return Task.CompletedTask;
					};
				return mqtt;
			});
			monitor = new RainPointMonitor (client, hub, new RainPointMonitorOptions { PollInterval = TimeSpan.FromSeconds (10) },
				 transport, Task.Delay, () => DateTimeOffset.UtcNow);
			monitor.StateChanged += (_, update) =>
			{
				Report ("Monitor: " + update.State);
				if (update.State == RainPointMonitorState.PushConnected)
					connected.TrySetResult (true);
			};
			monitor.StatusReceived += (_, update) =>
			{
				RainPointTimerObservation? observation = update.Timers.SingleOrDefault (item => item.Status.Address == timer.Address);
				RainPointZoneStatus? zone = observation?.Status.Zones.SingleOrDefault (item => item.Zone == zoneNumber);
				if (zone is null)
					return;
				Report ($"Zone {zoneNumber}: reported-active={zone.IsOpen?.ToString () ?? "unknown"}; mode={zone.WorkModeCode}; usage={zone.LastWaterUsageLitres?.ToString (System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"} L; source={observation!.Source}; changed={observation.Status.LastDataChange:O}.");
				if (update.LastSuccessfulPollAt.HasValue)
					polled.TrySetResult (true);
				DateTimeOffset? stamp = observation.Status.LastDataChange;
				if (update.Source != RainPointUpdateSource.Push || observation.Source != RainPointUpdateSource.Push
						 || !stamp.HasValue || stamp.Value.ToUnixTimeMilliseconds () < Interlocked.Read (ref startedAt))
					return;
				if (zone.IsOpen == true && zone.WorkModeCode == mode)
					opened.TrySetResult (stamp.Value);
				if (cycle && zone.WorkMode == RainPointWateringMode.CycleAndSoakPause && opened.Task.IsCompleted && stamp.Value > opened.Task.Result)
					paused.TrySetResult (stamp.Value);
				if (cycle && zone.WorkMode == RainPointWateringMode.CycleAndSoak && paused.Task.IsCompleted && stamp.Value > paused.Task.Result)
					resumed.TrySetResult (stamp.Value);
				if (zone.IsOpen == false && opened.Task.IsCompleted && stamp.Value > opened.Task.Result
						  && (mode == 1 || stamp.Value.ToUnixTimeMilliseconds () >= Interlocked.Read (ref stopSentAt)))
					closed.TrySetResult (stamp.Value);
			};
			_ = monitor.RunAsync (lifetime.Token);
			Task ready = Task.WhenAll (connected.Task, polled.Task);
			Assert.That (await Task.WhenAny (ready, Task.Delay (TimeSpan.FromSeconds (60), lifetime.Token)), Is.SameAs (ready),
				 "MQTT connection and decoded baseline were not ready; no start is permitted.");
			Assert.That (monitor.State, Is.EqualTo (RainPointMonitorState.PushConnected), "Observer must be connected before starting.");
			RainPointTimerStatus baseline = monitor.Current!.Status.Timers.Single (item => item.Address == timer.Address);
			Assert.That (baseline.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
			Assert.That (baseline.Zones.Count, Is.EqualTo (3));
			Report ($"Pre-start timer record: source={monitor.Current.Timers.Single (item => item.Status.Address == timer.Address).Source}; cloud last-change={baseline.LastDataChange:O}; device-local={baseline.ReportedAtLocal:O}.");
			foreach (RainPointZoneStatus item in baseline.Zones)
				Report ($"Pre-start zone {item.Zone}: reported-active={item.IsOpen}; mode={item.WorkModeCode}. This is not physical valve-position confirmation.");
			Assert.That (baseline.Zones.Single (item => item.Zone == zoneNumber).IsOpen, Is.False, $"Zone {zoneNumber} must report closed before this test can start it.");
			for (int number = 1; number <= 3; number++)
				{
				RainPointScheduleSnapshot snapshot = await client.GetTimerSchedulesAsync (hub, timer.Address, number, lifetime.Token);
				Assert.That (snapshot.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
				Assert.That (snapshot.Schedules, Is.Empty, "No saved plan may overlap this manual observation.");
				bool noSensorForUnreportedRule = snapshot.MoistureRuleAvailability == TimerReadingAvailability.NotReported
					&& snapshot.SoilSensorAvailability == TimerReadingAvailability.Decoded && snapshot.SoilSensorSettings is { SensorAddress: null };
				bool explicitlyInactive = snapshot.MoistureRuleAvailability == TimerReadingAvailability.Decoded && snapshot.MoistureWateringRule?.Enabled == false;
				Assert.That (explicitlyInactive || noSensorForUnreportedRule, Is.True, "No automatic moisture rule may overlap the manual observation.");
				}
			Interlocked.Exchange (ref startedAt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds ());
			Stopwatch elapsed = Stopwatch.StartNew ();
			startAttempted = true; // Set before the request: a timeout can still mean the valve received it.
			Report (cycle ? "Sending exactly one zone-1 cycle-and-soak start: five watering minutes, one-minute bursts/pauses. Cleanup stop at 150 seconds." : misting ? "Sending exactly one zone-1 misting start: one configured minute, ten-second bursts, twenty-second pauses. Cleanup stop at 65 seconds." : $"Sending exactly one zone-{zoneNumber} start, configured duration 60 seconds.");
			RainPointWateringCommandResult start = cycle
				 ? await client.StartCycleAndSoakAsync (hub, timer.Address, zoneNumber, TimeSpan.FromMinutes (5), TimeSpan.FromMinutes (1), TimeSpan.FromMinutes (1), lifetime.Token)
				 : misting
				 ? await client.StartMistingAsync (hub, timer.Address, zoneNumber, TimeSpan.FromMinutes (1), TimeSpan.FromSeconds (10), TimeSpan.FromSeconds (20), lifetime.Token)
				 : await client.StartWateringAsync (hub, timer.Address, zoneNumber, TimeSpan.FromSeconds (60), lifetime.Token);
			Report ("Start acknowledgement: " + start.Outcome);
			TimeSpan remaining = TimeSpan.FromSeconds (stopAfterSeconds) - elapsed.Elapsed;
			if (remaining > TimeSpan.Zero)
				await Task.Delay (remaining, lifetime.Token);
			stopAttempted = true;
			Interlocked.Exchange (ref stopSentAt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds ());
			stopAccepted = await StopZone (client, hub, timer, zoneNumber);
			await Task.WhenAny (closed.Task, Task.Delay (TimeSpan.FromSeconds (90), lifetime.Token));
			RainPointTimerStatus finalStatus = await client.GetTimerStatusAsync (hub, timer.Address, lifetime.Token);
			Assert.That (finalStatus.Zones.Single (item => item.Zone == zoneNumber).IsOpen, Is.False, $"Final cloud state must report zone {zoneNumber} closed.");
			Assert.That (finalStatus.Zones.Where (item => item.Zone != zoneNumber).Select (item => item.WorkModeCode), Is.EqualTo (baseline.Zones.Where (item => item.Zone != zoneNumber).Select (item => item.WorkModeCode)), "Other zones must retain their pre-existing reported modes; never stop or overwrite them for this test.");
			Report ("Final cloud state reports closed. No claim of physical burst timing or measured volume.");
			if (Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_REQUIRE_FLOW") == "1")
				{
				Assert.That (closed.Task.IsCompleted, Is.True, "Require a fresh completion event before interpreting final usage.");
				Assert.That (finalStatus.Zones.Single (item => item.Zone == zoneNumber).LastWaterUsageLitres, Is.GreaterThan (0m), "This authorized flow check requires nonzero device-reported usage; it does not calibrate the meter.");
				Report ("Fresh completion and nonzero device-reported usage verified.");
				}
			if (cycle)
				{
				Assert.That (paused.Task.IsCompleted && resumed.Task.IsCompleted, Is.True, "Require a new soaking pause and a later resumed cycle report before cleanup.");
				Assert.That ((paused.Task.Result - opened.Task.Result).TotalSeconds, Is.InRange (45, 85));
				Assert.That ((resumed.Task.Result - paused.Task.Result).TotalSeconds, Is.InRange (45, 85));
				Report ("One-minute reported watering/pause intervals and resumed cycle observed. Physical flow remains a separate owner observation.");
				}
			Report ($"Result: MQTT open={opened.Task.IsCompleted}; MQTT later closed={closed.Task.IsCompleted}; accepted pushes={monitor.AcceptedPushCount}; rejected/unchanged pushes={monitor.RejectedPushCount}; cleanup stop acknowledged={stopAccepted}.");
			using (Assert.EnterMultipleScope ())
				{
				Assert.That (stopAccepted, Is.True, "Cleanup stop was not acknowledged; inspect the valve. The configured duration is not independent physical confirmation.");
				Assert.That (opened.Task.IsCompleted, Is.True, $"No new zone-{zoneNumber} active reading arrived over MQTT.");
				Assert.That (closed.Task.IsCompleted, Is.True, $"No newer zone-{zoneNumber} idle reading arrived over MQTT within the observation window.");
				}
			}
		catch (Exception error) when (error is not AssertionException && error is not MultipleAssertException && error is not IgnoreException)
			{
			Assert.Fail ($"Zone-{zoneNumber} live check interrupted (" + error.GetType ().Name + "); cleanup still runs. Secret values and response bodies are omitted.");
			}
		finally
			{
			// Independent cancellation budget, even when start timed out or assertions failed. Never replay start.
			if (startAttempted && !stopAttempted && hub is not null && timer is not null)
				stopAccepted = await StopZone (client, hub, timer, zoneNumber);
			if (monitor is not null)
				await monitor.StopAsync ();
			if (client.HasValidSession)
				{
				using CancellationTokenSource cleanup = new (TimeSpan.FromSeconds (15));
				try
					{
					await client.LogoutAsync (cleanup.Token);
					}
				catch (Exception error) when (error is not OutOfMemoryException) { Report ("Remote logout did not complete; local client disposed."); }
				}
			Report ($"Finished. Start attempted={startAttempted}; stop acknowledged={stopAccepted}. Only zone {zoneNumber} was commanded. Physical flow requires owner observation.");
			}
		}

	private static Task<bool> StopZone1 (RainPointCloudClient client, RainPointHub hub, RainPointDevice timer) => StopZone (client, hub, timer, 1);

	private static async Task<bool> StopZone (RainPointCloudClient client, RainPointHub hub, RainPointDevice timer, int zoneNumber)
		{
		using CancellationTokenSource cleanup = new (TimeSpan.FromSeconds (30));
		Report ($"Sending zone-{zoneNumber} cleanup stop.");
		try
			{
			RainPointWateringCommandResult outcome = await client.StopWateringAsync (hub, timer.Address, zoneNumber, cleanup.Token);
			Report ("Stop acknowledgement: " + outcome.Outcome);
			return true;
			}
		catch (Exception error) when (error is not OutOfMemoryException)
			{
			Report ("Stop acknowledgement unavailable (" + error.GetType ().Name + "). No start retry will be attempted.");
			return false;
			}
		}

	private static void Report (string text)
		{
		string line = $"{DateTimeOffset.UtcNow:HH:mm:ss} UTC: {text}";
		TestContext.Progress.WriteLine (line);
		string? progressPath = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_PROGRESS");
		if (!string.IsNullOrWhiteSpace (progressPath))
			{
			try
				{
				File.AppendAllText (progressPath, line + Environment.NewLine);
				}
			catch (Exception error) when (error is IOException or UnauthorizedAccessException)
				{
				TestContext.Progress.WriteLine ("Optional local progress file unavailable; live cleanup remains active.");
				}
			}
		}

	private sealed class Account
		{
		[JsonPropertyName ("email")] public string Email { get; set; } = string.Empty;
		[JsonPropertyName ("password")] public string Password { get; set; } = string.Empty;
		[JsonPropertyName ("areaCode")] public string AreaCode { get; set; } = "44";
		}
	}
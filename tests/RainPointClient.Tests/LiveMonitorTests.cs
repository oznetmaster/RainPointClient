// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture, NonParallelizable, Category ("Live")]
public sealed class LiveMonitorTests
	{
	[Test, Explicit ("Uses a real cloud account; set RAINPOINT_LIVE_SETTINGS and run this test explicitly.")]
	public async Task ObserverConnectsAndPollsWithoutValveCommands ()
		{
		string? path = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS");
		if (string.IsNullOrWhiteSpace (path))
			Assert.Ignore ("Set RAINPOINT_LIVE_SETTINGS to an ignored private settings file.");
		Account? account = JsonSerializer.Deserialize<Account> (File.ReadAllText (path!));
		Assert.That (account, Is.Not.Null);
		using RainPointCloudClient client = new ();
		using CancellationTokenSource timeout = new (TimeSpan.FromSeconds (120));
		RainPointMonitor? monitor = null;
		try
			{
			await client.LoginAsync (account!.Email, account.Password, account.AreaCode, timeout.Token);
			RainPointHome[] homes = (await client.GetHomesAsync (timeout.Token)).ToArray ();
			RainPointHub? target = null;
			foreach (RainPointHome home in homes)
				foreach (RainPointHub hub in await client.GetHubsAsync (home.Id, timeout.Token))
					if (hub.Model is "HWG023WBRF" or "HWG023WBRF-V2" && hub.Devices.Any (device => device.Model == "HTV345FRF"))
						{
						Assert.That (target, Is.Null, "The read-only fixture requires exactly one matching hub.");
						target = hub;
						}
			Assert.That (target, Is.Not.Null, "No matching hub/timer was discovered.");
			TaskCompletionSource<bool> connected = new (TaskCreationOptions.RunContinuationsAsynchronously);
			TaskCompletionSource<bool> polled = new (TaskCreationOptions.RunContinuationsAsynchronously);
			monitor = new RainPointMonitor (client, target!);
			monitor.StateChanged += (_, args) =>
			{
				TestContext.Progress.WriteLine ("Monitor state: " + args.State);
				if (args.State == RainPointMonitorState.PushConnected)
					connected.TrySetResult (true);
			};
			monitor.StatusReceived += (_, update) =>
			{
				foreach (RainPointTimerObservation observation in update.Timers)
					foreach (RainPointZoneStatus zone in observation.Status.Zones)
						TestContext.Progress.WriteLine ($"Zone {zone.Zone}: active={zone.IsOpen}; mode={zone.WorkModeCode}; duration={zone.ConfiguredRunDuration}; usage={zone.LastWaterUsageLitres} L; source={observation.Source}; cloud-change={observation.Status.LastDataChange:O}; device-local={observation.Status.ReportedAtLocal:O}.");
				if (update.LastSuccessfulPollAt.HasValue && update.Status.Timers.Any (timer => timer.Availability == TimerReadingAvailability.Decoded))
					polled.TrySetResult (true);
			};
			_ = monitor.RunAsync (timeout.Token);
			Task ready = Task.WhenAll (connected.Task, polled.Task);
			Assert.That (await Task.WhenAny (ready, Task.Delay (TimeSpan.FromSeconds (60), timeout.Token)), Is.SameAs (ready),
				 "A TLS observer connection and decoded poll were not both observed within 60 seconds.");
			await ready;
			await Task.Delay (TimeSpan.FromSeconds (30), timeout.Token);
			TestContext.Progress.WriteLine ($"Read-only observer: accepted pushes={monitor.AcceptedPushCount}; rejected/unchanged pushes={monitor.RejectedPushCount}; decoded poll=yes. No valve commands sent.");
			// Idle hardware may send no changed timer data. This fixture does not claim a watering transition was observed.
			Assert.That (monitor.Current!.LastSuccessfulPollAt, Is.Not.Null);
			}
		catch (Exception error) when (error is not AssertionException && error is not SuccessException && error is not IgnoreException)
			{
			Assert.Fail ("Read-only live check failed (" + error.GetType ().Name + "). Account details and response bodies are omitted.");
			}
		finally
			{
			if (monitor is not null)
				await monitor.StopAsync ();
			if (client.HasValidSession)
				{
				using CancellationTokenSource cleanup = new (TimeSpan.FromSeconds (15));
				try
					{
					await client.LogoutAsync (cleanup.Token);
					}
				catch (Exception error) when (error is not OutOfMemoryException) { TestContext.Progress.WriteLine ("Remote logout did not complete; client will be disposed."); }
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
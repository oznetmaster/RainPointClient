// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using RainPointClient.Protocol;

namespace RainPointClient.Tests;

[TestFixture, NonParallelizable, Category ("Live"), Category ("Actuation")]
public sealed class ScheduledZone1LiveTests
	{
	[Test, Explicit ("Tests ONE 60-second zone-1 occurrence using the app-supported daily recurrence, then deletes the recurring plan and stops zone 1. Requires RAINPOINT_LIVE_SCHEDULE=daily-zone1-60. Cleanup is essential: a retained plan could recur.")]
	public Task DailyZone1PlanReportsStartAndAutomaticStop () => Run (false);

	[Test, Explicit ("Removes only this fixture's exact journaled plan and stops zone 1; never creates/enables a plan.")]
	public Task ReconcileScheduledZone1Plan () => Run (true);

	[Test, Explicit ("One zone-1 selected-weekday occurrence with a 1.0 L cap and three-minute duration cap, then deletes the plan and stops zone 1. Requires RAINPOINT_LIVE_SCHEDULE=volume-zone1-180. Reported volume is not independent physical measurement.")]
	public Task VolumeLimitedZone1PlanStopsBeforeDuration () => Run (false, true);

	[Test, Explicit ("One odd-day zone-1 plan: 120 seconds at 50% seasonal adjustment, observed by both shared accounts. Requires RAINPOINT_LIVE_SCHEDULE=odd-season-shared and secondary settings. Restores all settings.")]
	public Task OddDaySeasonalPlanReachesBothAccounts () => Run (false, seasonalShared: true);

	[Test, Explicit ("One interval-day zone-1 occurrence is suppressed by rain delay, then a later one-minute occurrence runs after clearing the delay. Requires RAINPOINT_LIVE_SCHEDULE=interval-rain-resume. Restores all settings.")]
	public Task IntervalPlanSkipsRainDelayThenResumes () => Run (false, rainResume: true);

	[Test, Explicit ("One 60-second zone-1 even-day occurrence on an actual even local date. Requires RAINPOINT_LIVE_SCHEDULE=even-zone1-60. Restores the original configuration.")]
	public Task EvenDayZone1PlanReportsStartAndAutomaticStop () => Run (false, evenDay: true);

	private static async Task Run (bool reconcileOnly, bool volumeLimited = false, bool seasonalShared = false, bool rainResume = false, bool evenDay = false)
		{
		string? optIn = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SCHEDULE");
		string requiredOptIn = evenDay ? "even-zone1-60" : rainResume ? "interval-rain-resume" : seasonalShared ? "odd-season-shared" : volumeLimited ? "volume-zone1-180" : "daily-zone1-60";
		int durationSeconds = seasonalShared ? 120 : volumeLimited ? 180 : 60;
		if (reconcileOnly ? optIn is not ("once-zone1-60" or "daily-zone1-60" or "volume-zone1-180" or "odd-season-shared" or "interval-rain-resume" or "even-zone1-60") : optIn != requiredOptIn)
			Assert.Ignore ("Explicit matching bounded zone-1 scheduling opt-in required.");
		string? settings = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS");
		if (string.IsNullOrWhiteSpace (settings))
			Assert.Ignore ("Private settings required.");
		string path = Path.GetFullPath (settings!) + ".scheduled-zone1.json";
		if (!reconcileOnly)
			{
			Assert.That (File.Exists (path), Is.False, "Reconcile the previous scheduled run first.");
			Assert.That (File.Exists (Path.GetFullPath (settings!) + ".all-zone-schedules.json"), Is.False, "Reconcile the disabled matrix first.");
			}
		Account account = JsonSerializer.Deserialize<Account> (File.ReadAllText (settings!))!;
		using RainPointCloudClient client = new ();
		using CancellationTokenSource lifetime = new (TimeSpan.FromMinutes (rainResume ? 22 : 15));
		using RainPointCloudClient secondary = new ();
		RainPointMonitor? secondaryMonitor = null;
		RainPointHub? secondaryHub = null;
		RainPointMonitor? monitor = null;
		RainPointHub? hub = null;
		RainPointDevice? timer = null;
		bool journalCreated = false;
		try
			{
			await client.LoginAsync (account.Email, account.Password, account.AreaCode, lifetime.Token);
			List<RainPointHub> matches = [];
			foreach (RainPointHome home in await client.GetHomesAsync (lifetime.Token))
				matches.AddRange ((await client.GetHubsAsync (home.Id, lifetime.Token)).Where (candidate => candidate.Model is "HWG023WBRF" or "HWG023WBRF-V2"
					&& candidate.Devices.Any (device => device.Model == "HTV345FRF")));
			Assert.That (matches, Has.Count.EqualTo (1));
			hub = matches.Single ();
			RainPointDevice[] timers = hub.Devices.Where (device => device.Model == "HTV345FRF").ToArray ();
			Assert.That (timers, Has.Length.EqualTo (1));
			timer = timers.Single ();
			if (reconcileOnly)
				{
				await RestoreAndStop (client, hub, timer, path);
				return;
				}
			if (seasonalShared)
				{
				string? secondPath = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SECONDARY_SETTINGS");
				Assert.That (string.IsNullOrWhiteSpace (secondPath), Is.False, "Supply the already shared secondary account settings.");
				Account other = JsonSerializer.Deserialize<Account> (File.ReadAllText (secondPath!))!;
				Assert.That (string.Equals (other.Email.Trim (), account.Email.Trim (), StringComparison.OrdinalIgnoreCase), Is.False);
				Report ("Spacing distinct-account login by two minutes before scheduling; no device writes yet.");
				await Task.Delay (TimeSpan.FromMinutes (2), lifetime.Token);
				await secondary.LoginAsync (other.Email, other.Password, other.AreaCode, lifetime.Token);
				secondaryHub = (await secondary.GetHubsAsync (hub.HomeId, lifetime.Token)).Single (item => item.Id == hub.Id);
				Assert.That (secondaryHub.Devices.Single (item => item.Address == timer.Address).Id, Is.EqualTo (timer.Id));
				}
			RainPointHomeDetails homeDetails = await client.GetHomeAsync (hub.HomeId, lifetime.Token);
			Assert.That (homeDetails.IsOwner == true || homeDetails.Role == RainPointMemberRole.Administrator, Is.True,
				"Use an owner/administrator account, matching the official app plan-edit prerequisite. No writes with a member account.");
			Report ($"Schedule account access: owner={homeDetails.IsOwner}; role={homeDetails.Role}.");
			Assert.That (homeDetails.TimeZoneName, Is.EqualTo ("Europe/London"), "This fixture is limited to the verified home timezone; never guess a schedule offset.");
			TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById ("GMT Standard Time");
			DateTime homeNow = DateTime.SpecifyKind (TimeZoneInfo.ConvertTimeFromUtc (DateTime.UtcNow, zone), DateTimeKind.Unspecified);
			// Six-minute lead also spaces consecutive deliberate runs by more than five minutes.
			DateTime localStart = homeNow.Date.AddHours (homeNow.Hour).AddMinutes (homeNow.Minute + 7);
			Assert.That (zone.IsInvalidTime (localStart) || zone.IsAmbiguousTime (localStart), Is.False);
			DateTimeOffset due = new (TimeZoneInfo.ConvertTimeToUtc (localStart, zone));
			if (seasonalShared)
				Assert.That (localStart.Day % 2, Is.EqualTo (1), "Odd-day execution needs an odd local day; do not change the device clock.");
			if (evenDay)
				Assert.That (localStart.Day % 2, Is.Zero, "Even-day execution needs an even local day; do not change the device clock.");
			RainPointScheduleSnapshot baseline = await client.GetTimerSchedulesAsync (hub, timer.Address, 1, lifetime.Token);
			Assert.That (baseline.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
			Assert.That (baseline.Schedules, Is.Empty);
			Assert.That (baseline.SeasonalAdjustmentAvailability, Is.EqualTo (TimerReadingAvailability.Decoded));
			Assert.That (baseline.SeasonalPercentages[localStart.Month - 1], Is.EqualTo (100), "Do not change seasonal settings to make this test pass.");
			Assert.That (baseline.RainDelayAvailability, Is.EqualTo (TimerReadingAvailability.Decoded));
			Assert.That (!baseline.RainDelayUntil.HasValue || baseline.RainDelayUntil.Value <= homeNow, Is.True, "Active rain delay prevents this execution check.");
			for (int number = 1; number <= 3; number++)
				{
				RainPointScheduleSnapshot snapshot = await client.GetTimerSchedulesAsync (hub, timer.Address, number, lifetime.Token);
				Assert.That (snapshot.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
				Assert.That (snapshot.Parameter, Is.EqualTo (baseline.Parameter));
				Assert.That (snapshot.Schedules, Is.Empty, "Other plans would make attribution ambiguous.");
				bool noSensorForUnreportedRule = snapshot.MoistureRuleAvailability == TimerReadingAvailability.NotReported
						&& snapshot.SoilSensorAvailability == TimerReadingAvailability.Decoded
						&& snapshot.SoilSensorSettings is { SensorAddress: null };
				bool explicitlyInactive = snapshot.MoistureRuleAvailability == TimerReadingAvailability.Decoded && snapshot.MoistureWateringRule?.Enabled == false;
				Assert.That (explicitlyInactive || noSensorForUnreportedRule, Is.True,
					"Require an explicitly inactive rule, or an unreported rule with a decoded, unassigned sensor association.");
				}
			TaskCompletionSource<DateTimeOffset> opened = new (TaskCreationOptions.RunContinuationsAsynchronously);
			TaskCompletionSource<DateTimeOffset> closed = new (TaskCreationOptions.RunContinuationsAsynchronously);
			decimal? completedUsage = null;
			TaskCompletionSource<DateTimeOffset> secondOpened = new (TaskCreationOptions.RunContinuationsAsynchronously);
			TaskCompletionSource<DateTimeOffset> secondClosed = new (TaskCreationOptions.RunContinuationsAsynchronously);
			TaskCompletionSource<bool> early = new (TaskCreationOptions.RunContinuationsAsynchronously);
			DateTimeOffset observationStart = DateTimeOffset.UtcNow;
			monitor = new RainPointMonitor (client, hub, new RainPointMonitorOptions { PollInterval = TimeSpan.FromSeconds (10) });
			monitor.StatusReceived += (_, update) =>
				{
					RainPointTimerObservation? observation = update.Timers.SingleOrDefault (item => item.Status.Address == timer.Address);
					RainPointZoneStatus? value = observation?.Status.Zones.SingleOrDefault (item => item.Zone == 1);
					DateTimeOffset? stamp = observation?.Status.LastDataChange;
					if (update.Source != RainPointUpdateSource.Push || observation?.Source != RainPointUpdateSource.Push || value is null || !stamp.HasValue || stamp <= observationStart)
						return;
					if (value.IsOpen == true && stamp < due.AddSeconds (-2))
						early.TrySetResult (true);
					if (value.IsOpen == true && value.WorkModeCode == 1 && stamp >= due.AddSeconds (-2))
						opened.TrySetResult (stamp.Value);
					if (value.IsOpen == false && opened.Task.IsCompleted && stamp > opened.Task.Result)
						{
						completedUsage = value.LastWaterUsageLitres;
						closed.TrySetResult (stamp.Value);
						}
					Report ($"Zone 1 push: active={value.IsOpen}; mode={value.WorkModeCode}; changed={stamp:O}; usage={value.LastWaterUsageLitres} L.");
				};
			Task monitorRun = monitor.RunAsync (lifetime.Token);
			for (int attempt = 0; attempt < 60 && (monitor.State != RainPointMonitorState.PushConnected || monitor.Current?.LastSuccessfulPollAt is null); attempt++)
				await Task.Delay (TimeSpan.FromSeconds (1), lifetime.Token);
			Assert.That (monitor.State, Is.EqualTo (RainPointMonitorState.PushConnected));
			RainPointTimerStatus status = monitor.Current!.Status.Timers.Single (item => item.Address == timer.Address);
			Assert.That (status.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
			Assert.That (status.Zones.Single (item => item.Zone == 1).IsOpen, Is.False, "Zone 1 must report closed; other zones retain their existing use.");
			if (seasonalShared)
				{
				secondaryMonitor = new RainPointMonitor (secondary, secondaryHub!, new RainPointMonitorOptions { PollInterval = TimeSpan.FromSeconds (15) });
				secondaryMonitor.StatusReceived += (_, update) =>
					{
						RainPointTimerObservation? observation = update.Timers.SingleOrDefault (item => item.Status.Address == timer.Address);
						RainPointZoneStatus? value = observation?.Status.Zones.SingleOrDefault (item => item.Zone == 1);
						DateTimeOffset? stamp = observation?.Status.LastDataChange;
						if (update.Source != RainPointUpdateSource.Push || observation?.Source != RainPointUpdateSource.Push || value is null || !stamp.HasValue || stamp < due.AddSeconds (-2))
							return;
						if (value.IsOpen == true && value.WorkModeCode == 1)
							secondOpened.TrySetResult (stamp.Value);
						if (value.IsOpen == false && secondOpened.Task.IsCompleted && stamp > secondOpened.Task.Result)
							secondClosed.TrySetResult (stamp.Value);
						Report ($"Secondary account push: active={value.IsOpen}; mode={value.WorkModeCode}; changed={stamp:O}; usage={value.LastWaterUsageLitres} L.");
					};
				_ = secondaryMonitor.RunAsync (lifetime.Token);
				for (int attempt = 0; attempt < 60 && (secondaryMonitor.State != RainPointMonitorState.PushConnected || secondaryMonitor.Current?.LastSuccessfulPollAt is null); attempt++)
					await Task.Delay (TimeSpan.FromSeconds (1), lifetime.Token);
				Assert.That (secondaryMonitor.State, Is.EqualTo (RainPointMonitorState.PushConnected));
				Assert.That (monitor.State, Is.EqualTo (RainPointMonitorState.PushConnected));
				Report ("Both shared accounts have connected observers before any settings write.");
				}
			Assert.That (due - DateTimeOffset.UtcNow, Is.GreaterThan (TimeSpan.FromMinutes (2)));
			RainPointIrrigationSchedule plan = new ()
				{
				Enabled = false,
				Repeat = evenDay ? RainPointScheduleRepeat.EvenDays : rainResume ? RainPointScheduleRepeat.IntervalDays : seasonalShared ? RainPointScheduleRepeat.OddDays : volumeLimited ? RainPointScheduleRepeat.Weekdays : RainPointScheduleRepeat.EveryDay,
				Interval = rainResume ? 2 : null,
				Weekdays = volumeLimited ? new[] { localStart.DayOfWeek } : Array.Empty<DayOfWeek> (),
				WaterLimitLitres = volumeLimited ? 1.0m : null,
				EffectiveDate = localStart.Date,
				StartTime = localStart.TimeOfDay,
				Duration = TimeSpan.FromSeconds (durationSeconds)
				};
			int[] originalMonths = baseline.SeasonalPercentages.ToArray ();
			int[] adjustedMonths = originalMonths.ToArray ();
			if (seasonalShared)
				adjustedMonths[localStart.Month - 1] = 50;
			string seasonOnly = seasonalShared ? TimerPlanSettings.EditSeason (baseline, adjustedMonths) : baseline.Parameter!;
			DateTime? rainEnd = rainResume ? localStart.AddMinutes (3) : baseline.RainDelayUntil;
			string rainOnly = rainResume ? TimerPlanSettings.EditRainDelay (baseline, rainEnd) : baseline.Parameter!;
			RainPointScheduleSnapshot planBaseline = rainResume ? Preview (baseline, rainOnly) : seasonalShared ? Preview (baseline, seasonOnly) : baseline;
			string disabled = ScheduleEditor.Edit (planBaseline, ScheduleEdit.Add, -1, ScheduleEditor.Encode (plan), false);
			plan.Enabled = true;
			string enabled = ScheduleEditor.Edit (planBaseline, ScheduleEdit.Add, -1, ScheduleEditor.Encode (plan), false);
			plan.Enabled = false;
			Journal journal = new ()
				{
				HomeId = hub.HomeId,
				HubId = hub.Id,
				DeviceId = timer.Id!.Value,
				Address = timer.Address,
				Original = baseline.Parameter!,
				SeasonOnly = seasonOnly,
				RainOnly = rainOnly,
				OriginalRain = baseline.RainDelayUntil,
				Allowed = new[] { baseline.Parameter!, seasonOnly, rainOnly, disabled, enabled },
				OriginalMonths = originalMonths,
				Disabled = disabled,
				Enabled = enabled,
				DueUtc = due
				};
			using (FileStream stream = new (path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
				JsonSerializer.Serialize (stream, journal);
				stream.Flush (true);
				}
			journalCreated = true;
			if (seasonalShared)
				{
				await client.SetTimerSeasonalAdjustmentAsync (hub, baseline, adjustedMonths, lifetime.Token);
				planBaseline = await WaitFor (client, hub, timer, seasonOnly, lifetime.Token);
				Report ("Current month changed to 50%; scheduled 120 seconds should run for one minute. Recovery journal saved first.");
				}
			if (rainResume)
				{
				await client.SetTimerRainDelayAsync (hub, baseline, rainEnd, lifetime.Token);
				planBaseline = await WaitFor (client, hub, timer, rainOnly, lifetime.Token);
				Report ($"Rain delay set until {rainEnd:O} home-local, covering the first planned occurrence.");
				}
			await client.AddTimerScheduleAsync (hub, planBaseline, plan, lifetime.Token);
			RainPointScheduleSnapshot saved = await WaitFor (client, hub, timer, disabled, lifetime.Token);
			Assert.That (saved.Schedules.Single ().Enabled, Is.False);
			Assert.That (due - DateTimeOffset.UtcNow, Is.GreaterThan (TimeSpan.FromSeconds (90)), "Creation ran too late; cleanup without enabling.");
			Assert.That (early.Task.IsCompleted, Is.False);
			await client.SetTimerScheduleEnabledAsync (hub, saved, 0, true, lifetime.Token);
			await WaitFor (client, hub, timer, enabled, lifetime.Token);
			Report ($"One bounded zone-1 occurrence (duration={durationSeconds}s; recurrence={plan.Repeat}; volume limit={plan.WaterLimitLitres} L) enabled for {due:O} UTC. No manual start command will be sent.");
			if (rainResume)
				{
				while (DateTimeOffset.UtcNow < due.AddSeconds (90))
					{
					Assert.That (early.Task.IsCompleted || opened.Task.IsCompleted, Is.False, "Rain-delayed occurrence reported active; cleanup and fail.");
					Assert.That (monitorRun.IsCompleted, Is.False);
					await Task.Delay (TimeSpan.FromSeconds (1), lifetime.Token);
					}
				Assert.That (monitor.Current?.LastSuccessfulPollAt, Is.GreaterThan (due.AddSeconds (60)), "Require a successful status read after the suppressed window.");
				Assert.That (monitor.Current!.Status.Timers.Single (item => item.Address == timer.Address).Zones.Single (item => item.Zone == 1).IsOpen, Is.False);
				Assert.That (monitor.State, Is.EqualTo (RainPointMonitorState.PushConnected));
				Report ("First interval-day occurrence remained idle through its full window while rain delay was active.");
				RainPointScheduleSnapshot delayed = await WaitFor (client, hub, timer, enabled, lifetime.Token);
				string cleared = TimerPlanSettings.EditRainDelay (delayed, baseline.RainDelayUntil);
				plan.StartTime = localStart.AddMinutes (7).TimeOfDay;
				plan.Enabled = true;
				Assert.That (localStart.AddMinutes (7).Date, Is.EqualTo (localStart.Date), "Do not cross midnight in the bounded interval test.");
				string rescheduled = ScheduleEditor.Edit (Preview (baseline, cleared), ScheduleEdit.Replace, 0, ScheduleEditor.Encode (plan), false);
				journal.Allowed = journal.Allowed!.Concat (new[] { cleared, rescheduled }).Distinct ().ToArray ();
				// Persist both possible recovery states before either write; retain the original journal on replacement failure.
				string nextJournal = path + ".next";
				using (FileStream stream = new (nextJournal, FileMode.CreateNew, FileAccess.Write, FileShare.None))
					{
					JsonSerializer.Serialize (stream, journal);
					stream.Flush (true);
					}
				File.Replace (nextJournal, path, null);
				due = due.AddMinutes (7);
				await client.SetTimerRainDelayAsync (hub, delayed, baseline.RainDelayUntil, lifetime.Token);
				RainPointScheduleSnapshot clearSnapshot = await WaitFor (client, hub, timer, cleared, lifetime.Token);
				await client.UpdateTimerScheduleAsync (hub, clearSnapshot, 0, plan, lifetime.Token);
				await WaitFor (client, hub, timer, rescheduled, lifetime.Token);
				Assert.That (due - DateTimeOffset.UtcNow, Is.GreaterThan (TimeSpan.FromMinutes (2)));
				Report ($"Rain delay cleared; same interval-day plan moved to {due:O} UTC. One-minute duration, no manual start.");
				}
			while (DateTimeOffset.UtcNow < due.AddSeconds (durationSeconds + 25) && !closed.Task.IsCompleted)
				{
				Assert.That (early.Task.IsCompleted, Is.False, "Activity occurred before the scheduled window; stop and reconcile.");
				Assert.That (monitorRun.IsCompleted, Is.False, "Monitor stopped during scheduled observation.");
				await Task.Delay (TimeSpan.FromSeconds (1), lifetime.Token);
				}
			Report ($"Observation ended: monitor={monitor.State}; accepted pushes={monitor.AcceptedPushCount}; last successful poll={monitor.Current?.LastSuccessfulPollAt:O}.");
			Assert.That (opened.Task.IsCompleted, Is.True, "No new normal-watering MQTT reading in the scheduled window.");
			Assert.That (closed.Task.IsCompleted, Is.True, "No natural idle report before cleanup stop; cannot claim automatic completion.");
			double reportedSeconds = (closed.Task.Result - opened.Task.Result).TotalSeconds;
			if (volumeLimited)
				{
				Assert.That (reportedSeconds, Is.InRange (1, durationSeconds - 20), "The run must end well before its duration cap to support volume-cutoff evidence.");
				Assert.That (completedUsage, Is.Not.Null, "Reported completion volume is required; do not infer it from a stop alone.");
				Assert.That (completedUsage!.Value, Is.InRange (1.0m, 2.0m), "Reported usage must be consistent with the one-litre cap, allowing reporting/valve delay; this is not a calibrated volume measurement.");
				}
			else
				Assert.That (reportedSeconds, Is.InRange (45, 80), "Reported duration was not consistent with the one-minute plan.");
			if (seasonalShared)
				{
				await Task.WhenAny (secondClosed.Task, Task.Delay (TimeSpan.FromSeconds (20), lifetime.Token));
				Assert.That (secondOpened.Task.IsCompleted && secondClosed.Task.IsCompleted, Is.True, "Both accounts must receive changing start/idle push events.");
				Assert.That (Math.Abs ((secondOpened.Task.Result - opened.Task.Result).TotalSeconds), Is.LessThan (5));
				Assert.That (Math.Abs ((secondClosed.Task.Result - closed.Task.Result).TotalSeconds), Is.LessThan (5));
				Assert.That (completedUsage, Is.GreaterThan (0m), "Fresh nonzero reported usage is required for this run.");
				Report ("Both accounts received matching start/idle events; odd-day execution and 50% seasonal duration verified by reports.");
				}
			Assert.That ((opened.Task.Result - due).TotalSeconds, Is.InRange (-2, 25));
			Report ($"Scheduled start and automatic idle reported {reportedSeconds:F1} seconds apart. No physical flow/volume claim.");
			}
		catch (Exception error) when (error is not AssertionException && error is not MultipleAssertException && error is not IgnoreException)
			{
			if (error is RainPointException protocol)
				Report ($"Sanitized cloud error: API {protocol.ApiCode}, HTTP {protocol.HttpStatus}.");
			Assert.Fail ("Scheduled zone-1 check failed (" + error.GetType ().Name + "). Private details omitted; cleanup still runs.");
			}
		finally
			{
			try
				{
				if (journalCreated && hub is not null && timer is not null)
					await RestoreAndStop (client, hub, timer, path);
				}
			finally
				{
				if (secondaryMonitor is not null)
					await secondaryMonitor.StopAsync ();
				if (secondary.HasValidSession)
					{
					using CancellationTokenSource secondLogout = new (TimeSpan.FromSeconds (15));
					try
						{
						await secondary.LogoutAsync (secondLogout.Token);
						}
					catch (Exception error) when (error is not OutOfMemoryException) { Report ("Secondary logout unavailable; local client disposed."); }
					}
				if (monitor is not null)
					await monitor.StopAsync ();
				if (client.HasValidSession)
					{
					using CancellationTokenSource logout = new (TimeSpan.FromSeconds (15));
					try
						{
						await client.LogoutAsync (logout.Token);
						}
					catch (Exception error) when (error is not OutOfMemoryException) { Report ("Remote logout unavailable; local client disposed."); }
					}
				}
			}
		}

	private static async Task RestoreAndStop (RainPointCloudClient client, RainPointHub hub, RainPointDevice timer, string path)
		{
		Assert.That (File.Exists (path), Is.True, "No journal to reconcile.");
		Journal journal = JsonSerializer.Deserialize<Journal> (File.ReadAllText (path))!;
		Assert.That (journal.HomeId == hub.HomeId && journal.HubId == hub.Id && journal.DeviceId == timer.Id && journal.Address == timer.Address, Is.True,
			"Recovery identity must match exactly before any command.");
		bool restored = false, stopped = false, closed = false;
		try
			{
			using CancellationTokenSource restore = new (TimeSpan.FromSeconds (60));
			RainPointScheduleSnapshot snapshot = await client.GetTimerSchedulesAsync (hub, timer.Address, 1, restore.Token);
			string seasonOnly = journal.SeasonOnly ?? journal.Original;
			string rainOnly = journal.RainOnly ?? journal.Original;
			if (snapshot.Parameter != journal.Original && snapshot.Parameter != seasonOnly && snapshot.Parameter != rainOnly)
				{
				Assert.That (snapshot.Parameter == journal.Disabled || snapshot.Parameter == journal.Enabled || journal.Allowed?.Contains (snapshot.Parameter!) == true, Is.True, "Unexpected settings; retain journal, do not overwrite.");
				Assert.That (snapshot.Schedules, Has.Count.EqualTo (1));
				await client.DeleteTimerScheduleAsync (hub, snapshot, 0, restore.Token);
				}
			if (rainOnly != journal.Original && snapshot.Parameter != journal.Original)
				{
				snapshot = await client.GetTimerSchedulesAsync (hub, timer.Address, 1, restore.Token);
				Assert.That (snapshot.Parameter == rainOnly || snapshot.Parameter == journal.Original, Is.True, "After exact plan deletion, only original or fixture rain settings may remain.");
				if (snapshot.Parameter == rainOnly)
					await client.SetTimerRainDelayAsync (hub, snapshot, journal.OriginalRain, restore.Token);
				}
			if (seasonOnly != journal.Original && snapshot.Parameter != journal.Original)
				{
				snapshot = await WaitFor (client, hub, timer, seasonOnly, restore.Token);
				Assert.That (journal.OriginalMonths, Has.Length.EqualTo (12));
				await client.SetTimerSeasonalAdjustmentAsync (hub, snapshot, journal.OriginalMonths!, restore.Token);
				}
			await WaitFor (client, hub, timer, journal.Original, restore.Token);
			restored = true;
			}
		finally
			{
			// Stop has its own budget even if settings recovery failed. Never retry start or enable.
			using CancellationTokenSource stop = new (TimeSpan.FromSeconds (30));
			try
				{
				await client.StopWateringAsync (hub, timer.Address, 1, stop.Token);
				stopped = true;
				Report ("Zone-1 cleanup stop acknowledged.");
				}
			catch (Exception error) when (error is not OutOfMemoryException) { Report ("Cleanup stop unavailable: " + error.GetType ().Name); }
			using CancellationTokenSource observation = new (TimeSpan.FromSeconds (60));
			try
				{
				for (int attempt = 0; attempt < 12; attempt++)
					{
					RainPointTimerStatus status = await client.GetTimerStatusAsync (hub, timer.Address, observation.Token);
					if (status.Zones.Single (item => item.Zone == 1).IsOpen == false)
						{
						closed = true;
						break;
						}
					await Task.Delay (TimeSpan.FromSeconds (3), observation.Token);
					}
				}
			catch (Exception error) when (error is not OutOfMemoryException) { Report ("Closed-state read unavailable: " + error.GetType ().Name); }
			if (restored && stopped && closed)
				{
				File.Delete (path);
				Report ("Temporary plan removed; complete original configuration restored; final zone-1 cloud state closed; journal removed.");
				}
			}
		Assert.That (restored && stopped && closed, Is.True, "Cleanup incomplete; retain journal and reconcile. Check the physical valve if stop/closure could not be confirmed.");
		}

	private static async Task<RainPointScheduleSnapshot> WaitFor (RainPointCloudClient client, RainPointHub hub, RainPointDevice timer, string expected, CancellationToken token)
		{
		for (int attempt = 0; attempt < 15; attempt++)
			{
			RainPointScheduleSnapshot snapshot = await client.GetTimerSchedulesAsync (hub, timer.Address, 1, token);
			if (snapshot.Availability == TimerReadingAvailability.Decoded && snapshot.Parameter == expected)
				return snapshot;
			await Task.Delay (TimeSpan.FromSeconds (2), token);
			}
		throw new InvalidOperationException ("Expected complete timer configuration did not appear.");
		}

	private static RainPointScheduleSnapshot Preview (RainPointScheduleSnapshot source, string parameter)
		{
		RainPointScheduleSnapshot result = ScheduleDecoder.Decode (new RainPointDevice { Address = source.Address, PortNumber = source.PortNumber, Parameter = parameter }, source.Zone);
		result.Parameter = parameter;
		result.PortNumber = source.PortNumber;
		result.FirmwareVersion = source.FirmwareVersion;
		TimerPlanSettings.Decode (result);
		return result;
		}

	private static void Report (string message)
		{
		string line = $"{DateTimeOffset.UtcNow:HH:mm:ss} UTC: {message}";
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
				TestContext.Progress.WriteLine ("Optional progress file unavailable; cleanup remains active.");
				}
			}
		}
	private sealed class Account
		{
		[JsonPropertyName ("email")] public string Email { get; set; } = string.Empty;
		[JsonPropertyName ("password")] public string Password { get; set; } = string.Empty;
		[JsonPropertyName ("areaCode")] public string AreaCode { get; set; } = "44";
		}
	private sealed class Journal
		{
		[JsonPropertyName ("homeId")]
		public long HomeId
			{
			get; set;
			}
		[JsonPropertyName ("hubId")]
		public long HubId
			{
			get; set;
			}
		[JsonPropertyName ("deviceId")]
		public long DeviceId
			{
			get; set;
			}
		[JsonPropertyName ("address")]
		public int Address
			{
			get; set;
			}
		[JsonPropertyName ("original")] public string Original { get; set; } = string.Empty;
		[JsonPropertyName ("seasonOnly")]
		public string? SeasonOnly
			{
			get; set;
			}
		[JsonPropertyName ("rainOnly")]
		public string? RainOnly
			{
			get; set;
			}
		[JsonPropertyName ("originalRain")]
		public DateTime? OriginalRain
			{
			get; set;
			}
		[JsonPropertyName ("allowed")]
		public string[]? Allowed
			{
			get; set;
			}
		[JsonPropertyName ("originalMonths")]
		public int[]? OriginalMonths
			{
			get; set;
			}
		[JsonPropertyName ("disabled")] public string Disabled { get; set; } = string.Empty;
		[JsonPropertyName ("enabled")] public string Enabled { get; set; } = string.Empty;
		[JsonPropertyName ("dueUtc")]
		public DateTimeOffset DueUtc
			{
			get; set;
			}
		}
	}
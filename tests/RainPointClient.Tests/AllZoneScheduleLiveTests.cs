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

[TestFixture, NonParallelizable, Category ("Live"), Category ("Configuration")]
public sealed class AllZoneScheduleLiveTests
	{
	[Test, Explicit ("Creates, replaces and deletes DISABLED plans in all zones. Requires RAINPOINT_LIVE_SCHEDULE=disabled-all-zones. Never enables a plan or opens a valve.")]
	public Task DisabledPlansRoundTripAllZonesModesAndRecurrences () => Run (false);

	[Test, Explicit ("Reconciles only this fixture's journaled disabled plan after exact identity/configuration checks.")]
	public Task ReconcileJournaledDisabledPlan () => Run (true);

	private static async Task Run (bool reconcileOnly)
		{
		if (Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SCHEDULE") != "disabled-all-zones")
			Assert.Ignore ("Explicit disabled-all-zones opt-in required.");
		string? path = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS");
		if (string.IsNullOrWhiteSpace (path))
			Assert.Ignore ("Private account settings required.");
		string journalPath = Path.GetFullPath (path!) + ".all-zone-schedules.json";
		if (!reconcileOnly)
			Assert.That (File.Exists (journalPath), Is.False, "Reconcile the prior fixture before starting another.");
		Account account = JsonSerializer.Deserialize<Account> (File.ReadAllText (path!))!;
		using RainPointCloudClient client = new ();
		using CancellationTokenSource timeout = new (TimeSpan.FromMinutes (15));
		try
			{
			await client.LoginAsync (account.Email, account.Password, account.AreaCode, timeout.Token);
			List<RainPointHub> matches = [];
			foreach (RainPointHome home in await client.GetHomesAsync (timeout.Token))
				matches.AddRange ((await client.GetHubsAsync (home.Id, timeout.Token)).Where (hub => hub.Model is "HWG023WBRF" or "HWG023WBRF-V2"
					&& hub.Devices.Any (device => device.Model == "HTV345FRF")));
			Assert.That (matches, Has.Count.EqualTo (1), "Exactly one matching hub required.");
			RainPointHub hub = matches.Single ();
			RainPointDevice[] timers = hub.Devices.Where (device => device.Model == "HTV345FRF").ToArray ();
			Assert.That (timers, Has.Length.EqualTo (1), "Exactly one matching timer required.");
			RainPointDevice timer = timers.Single ();
			if (reconcileOnly)
				{
				await Restore (client, hub, timer, journalPath, true);
				return;
				}
			// Require all zones to start empty. Every comparison includes the entire timer parameter.
			string? original = null;
			for (int zone = 1; zone <= 3; zone++)
				{
				RainPointScheduleSnapshot baseline = await client.GetTimerSchedulesAsync (hub, timer.Address, zone, timeout.Token);
				Assert.That (baseline.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
				Assert.That (baseline.Schedules, Is.Empty, "This fixture does not edit existing plans.");
				original ??= baseline.Parameter;
				Assert.That (baseline.Parameter, Is.EqualTo (original), "Configuration changed during baseline discovery.");
				}
			int verified = 0;
			for (int zone = 1; zone <= 3; zone++)
				foreach (RainPointScheduleMode mode in new[] { RainPointScheduleMode.Irrigation, RainPointScheduleMode.CycleAndSoak, RainPointScheduleMode.Misting })
					{
					RainPointScheduleSnapshot current = await client.GetTimerSchedulesAsync (hub, timer.Address, zone, timeout.Token);
					Assert.That (current.Parameter, Is.EqualTo (original));
					Journal journal = new ()
						{
						HomeId = hub.HomeId,
						HubId = hub.Id,
						DeviceId = timer.Id!.Value,
						Address = timer.Address,
						Zone = zone,
						Original = original!
						};
					bool readBackConfirmed = true;
					try
						{
						int variant = 0;
						foreach (RainPointScheduleRepeat repeat in new[] { RainPointScheduleRepeat.EveryDay,
							RainPointScheduleRepeat.OddDays, RainPointScheduleRepeat.EvenDays, RainPointScheduleRepeat.Weekdays, RainPointScheduleRepeat.IntervalDays })
							{
							Draft draft = new (mode, repeat, variant);
							string expected = ScheduleEditor.Edit (current, variant == 0 ? ScheduleEdit.Add : ScheduleEdit.Replace,
								variant == 0 ? -1 : 0, draft.Record, false);
							journal.Known.Add (expected);
							SaveJournal (journalPath, journal, variant == 0);
							readBackConfirmed = false;
							await draft.Write (client, hub, current, variant == 0, timeout.Token);
							current = await WaitFor (client, hub, timer, zone, expected, timeout.Token);
							readBackConfirmed = true;
							draft.Check (current.Schedules.Single ());
							verified++;
							TestContext.Progress.WriteLine ($"Zone {zone}: disabled {mode}, {repeat}, variant {variant + 1}/5 read back; full configuration matches.");
							variant++;
							await Task.Delay (TimeSpan.FromSeconds (1), timeout.Token);
							}
						}
					finally
						{
						if (File.Exists (journalPath))
							{
							// After an uncertain write, retain the journal rather than racing a delayed write with deletion.
							if (readBackConfirmed)
								await Restore (client, hub, timer, journalPath, false);
							else
								TestContext.Progress.WriteLine ("Write/read-back uncertain. Disabled-plan recovery journal retained; run reconciliation before any further writes.");
							}
						}
					}
			Assert.That (verified, Is.EqualTo (45));
			Assert.That (File.Exists (journalPath), Is.False);
			TestContext.Progress.WriteLine ("All 45 zone/mode/recurrence combinations verified. Nine temporary plans removed; full original configuration restored after every mode. No enabling or valve commands.");
			}
		catch (Exception error) when (error is not AssertionException && error is not MultipleAssertException && error is not IgnoreException)
			{
			if (error is IOException io)
				TestContext.Progress.WriteLine ("Local I/O failure code: " + io.HResult.ToString ("X8"));
			if (error is RainPointException protocol)
				TestContext.Progress.WriteLine ($"Sanitized cloud error: API {protocol.ApiCode}, HTTP {protocol.HttpStatus}.");
			Assert.Fail ("Disabled all-zone fixture failed (" + error.GetType ().Name + "). Private values omitted. Preserve any journal for explicit reconciliation.");
			}
		finally
			{
			if (client.HasValidSession)
				{
				using CancellationTokenSource cleanup = new (TimeSpan.FromSeconds (15));
				try
					{
					await client.LogoutAsync (cleanup.Token);
					}
				catch (Exception error) when (error is not OutOfMemoryException) { TestContext.Progress.WriteLine ("Remote logout unavailable; client disposed."); }
				}
			}
		}

	private static async Task Restore (RainPointCloudClient client, RainPointHub hub, RainPointDevice timer, string path, bool reconcile)
		{
		Assert.That (File.Exists (path), Is.True, "No fixture journal to reconcile.");
		Journal journal = JsonSerializer.Deserialize<Journal> (File.ReadAllText (path))!;
		Assert.That (journal.HomeId == hub.HomeId && journal.HubId == hub.Id && journal.DeviceId == timer.Id && journal.Address == timer.Address, Is.True,
			"Journal identity does not match discovered hardware.");
		Assert.That (journal.Zone, Is.InRange (1, 3));
		using CancellationTokenSource cleanup = new (TimeSpan.FromSeconds (90));
		RainPointScheduleSnapshot snapshot = await client.GetTimerSchedulesAsync (hub, timer.Address, journal.Zone, cleanup.Token);
		if (reconcile)
			{
			// Observe stability for 30 seconds before touching a prior uncertain write. No create/update replay.
			string? observed = snapshot.Parameter;
			for (int i = 0; i < 10; i++)
				{
				await Task.Delay (TimeSpan.FromSeconds (3), cleanup.Token);
				snapshot = await client.GetTimerSchedulesAsync (hub, timer.Address, journal.Zone, cleanup.Token);
				Assert.That (snapshot.Parameter, Is.EqualTo (observed), "Prior write has not settled; retain journal and reconcile later.");
				}
			}
		Assert.That (snapshot.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
		if (snapshot.Parameter != journal.Original)
			{
			Assert.That (journal.Known.Contains (snapshot.Parameter!), Is.True, "Unexpected configuration; refusing to overwrite it.");
			Assert.That (snapshot.Schedules, Has.Count.EqualTo (1));
			Assert.That (snapshot.Schedules.Single ().Enabled, Is.False);
			await client.DeleteTimerScheduleAsync (hub, snapshot, 0, cleanup.Token);
			await WaitFor (client, hub, timer, journal.Zone, journal.Original, cleanup.Token);
			}
		File.Delete (path);
		if (File.Exists (path + ".next"))
			File.Delete (path + ".next");
		TestContext.Progress.WriteLine ($"Zone {journal.Zone}: full original timer configuration restored; recovery journal removed.");
		}

	private static void SaveJournal (string path, Journal journal, bool first)
		{
		if (first)
			{
			using FileStream stream = new (path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
			JsonSerializer.Serialize (stream, journal);
			stream.Flush (true);
			}
		else
			{
			string temporary = path + ".next";
			using (FileStream stream = new (temporary, FileMode.Create, FileAccess.Write, FileShare.None))
				{
				JsonSerializer.Serialize (stream, journal);
				stream.Flush (true);
				}
			// File indexing/scanning can briefly deny delete sharing. Retry only the local atomic replace,
			// never any cloud request. Win32 1175 also leaves both original names intact.
			// https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew
			// Other errors still fail immediately.
			for (int attempt = 0; ; attempt++)
				{
				try
					{
					File.Replace (temporary, path, null);
					break;
					}
				catch (IOException error) when (attempt < 4 && (error.HResult & 0xffff) is 32 or 33 or 1175)
					{
					Thread.Sleep (100);
					}
				}
			}
		}

	private static async Task<RainPointScheduleSnapshot> WaitFor (RainPointCloudClient client, RainPointHub hub, RainPointDevice timer,
		int zone, string expected, CancellationToken token)
		{
		for (int attempt = 0; attempt < 15; attempt++)
			{
			RainPointScheduleSnapshot snapshot = await client.GetTimerSchedulesAsync (hub, timer.Address, zone, token);
			if (snapshot.Availability == TimerReadingAvailability.Decoded && snapshot.Parameter == expected)
				return snapshot;
			await Task.Delay (TimeSpan.FromSeconds (2), token);
			}
		throw new InvalidOperationException ("Expected full timer configuration did not appear.");
		}

	private sealed class Draft
		{
		private readonly RainPointScheduleMode _mode;
		private readonly RainPointIrrigationSchedule _normal;
		private readonly RainPointCycleAndSoakSchedule _cycle;
		private readonly RainPointMistingSchedule _mist;

		internal Draft (RainPointScheduleMode mode, RainPointScheduleRepeat repeat, int variant)
			{
			_mode = mode;
			_normal = new ()
				{
				Enabled = false,
				StartTime = new TimeSpan (23, 50 + variant, 0),
				Duration = TimeSpan.FromMinutes (1 + variant),
				Repeat = repeat,
				EffectiveDate = new DateTime (2083, 12, 31),
				Weekdays = repeat == RainPointScheduleRepeat.Weekdays ? new[] { DayOfWeek.Sunday, DayOfWeek.Wednesday, DayOfWeek.Saturday } : [],
				Interval = repeat == RainPointScheduleRepeat.IntervalDays ? 3 : null,
				WaterLimitLitres = variant % 2 == 0 ? 1.4m : null
				};
			_cycle = new ()
				{
				Enabled = false,
				StartTime = _normal.StartTime,
				Duration = TimeSpan.FromMinutes (5 + variant),
				CycleWateringTime = TimeSpan.FromMinutes (1),
				CyclePauseTime = TimeSpan.FromMinutes (2),
				Repeat = repeat,
				EffectiveDate = _normal.EffectiveDate,
				Weekdays = _normal.Weekdays,
				Interval = _normal.Interval,
				WaterLimitLitres = _normal.WaterLimitLitres
				};
			_mist = new ()
				{
				Enabled = false,
				StartTime = _normal.StartTime,
				Duration = _normal.Duration,
				CycleWateringTime = TimeSpan.FromSeconds (10 + variant),
				CyclePauseTime = TimeSpan.FromSeconds (20 + variant),
				Repeat = repeat,
				EffectiveDate = _normal.EffectiveDate,
				Weekdays = _normal.Weekdays,
				Interval = _normal.Interval,
				WaterLimitLitres = _normal.WaterLimitLitres
				};
			}

		internal string Record => _mode switch
			{
				RainPointScheduleMode.Irrigation => ScheduleEditor.Encode (_normal),
				RainPointScheduleMode.CycleAndSoak => ScheduleEditor.EncodeCycleAndSoak (_cycle),
				_ => ScheduleEditor.EncodeMisting (_mist)
				};

		internal Task Write (RainPointCloudClient client, RainPointHub hub, RainPointScheduleSnapshot snapshot, bool add, CancellationToken token) => _mode switch
			{
				RainPointScheduleMode.Irrigation => add ? client.AddTimerScheduleAsync (hub, snapshot, _normal, token) : client.UpdateTimerScheduleAsync (hub, snapshot, 0, _normal, token),
				RainPointScheduleMode.CycleAndSoak => add ? client.AddTimerScheduleAsync (hub, snapshot, _cycle, token) : client.UpdateTimerScheduleAsync (hub, snapshot, 0, _cycle, token),
				_ => add ? client.AddTimerScheduleAsync (hub, snapshot, _mist, token) : client.UpdateTimerScheduleAsync (hub, snapshot, 0, _mist, token)
				};

		internal void Check (RainPointSchedule saved)
			{
			using (Assert.EnterMultipleScope ())
				{
				Assert.That (saved.Enabled, Is.False);
				Assert.That (saved.Mode, Is.EqualTo (_mode));
				Assert.That (saved.StartTime, Is.EqualTo (_normal.StartTime));
				Assert.That (saved.Repeat, Is.EqualTo (_normal.Repeat));
				Assert.That (saved.Weekdays, Is.EqualTo (_normal.Weekdays));
				Assert.That (saved.Interval, Is.EqualTo (_normal.Interval));
				Assert.That (saved.EffectiveDate, Is.EqualTo (_normal.EffectiveDate));
				Assert.That (saved.WaterLimitLitres, Is.EqualTo (_normal.WaterLimitLitres));
				Assert.That (saved.Duration, Is.EqualTo (_mode == RainPointScheduleMode.CycleAndSoak ? _cycle.Duration : _normal.Duration));
				Assert.That (saved.CycleWateringTime, Is.EqualTo (_mode == RainPointScheduleMode.Irrigation ? (TimeSpan?)null : _mode == RainPointScheduleMode.CycleAndSoak ? _cycle.CycleWateringTime : _mist.CycleWateringTime));
				Assert.That (saved.CyclePauseTime, Is.EqualTo (_mode == RainPointScheduleMode.Irrigation ? (TimeSpan?)null : _mode == RainPointScheduleMode.CycleAndSoak ? _cycle.CyclePauseTime : _mist.CyclePauseTime));
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
		[JsonPropertyName ("zone")]
		public int Zone
			{
			get; set;
			}
		[JsonPropertyName ("original")] public string Original { get; set; } = string.Empty;
		[JsonPropertyName ("known")] public List<string> Known { get; set; } = [];
		}
	}
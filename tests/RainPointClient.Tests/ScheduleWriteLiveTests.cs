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
public sealed class ScheduleWriteLiveTests
	{
	[Test, Explicit ("Creates one DISABLED zone-1 plan for app comparison. Run cleanup afterward; requires RAINPOINT_LIVE_SCHEDULE=disabled-zone1.")]
	public Task PrepareDisabledZone1PlanForAppComparison () => Run (prepare: true);

	[Test, Explicit ("Creates one DISABLED cycle-and-soak zone-1 plan for app comparison. Run cleanup afterward; requires RAINPOINT_LIVE_SCHEDULE=disabled-zone1.")]
	public Task PrepareDisabledZone1CyclePlanForAppComparison () => Run (prepare: true, mode: RainPointScheduleMode.CycleAndSoak);

	[Test, Explicit ("Creates one DISABLED misting zone-1 plan for app comparison. Run cleanup afterward; requires RAINPOINT_LIVE_SCHEDULE=disabled-zone1.")]
	public Task PrepareDisabledZone1MistingPlanForAppComparison () => Run (prepare: true, mode: RainPointScheduleMode.Misting);

	[Test, Explicit ("Removes only this fixture's disabled plan after exact configuration comparison. Requires RAINPOINT_LIVE_SCHEDULE=disabled-zone1.")]
	public Task RemoveDisabledZone1ComparisonPlan () => Run (prepare: false);

	[Test]
	[Explicit ("Creates and removes one DISABLED zone-1 volume plan; requires RAINPOINT_LIVE_SCHEDULE=disabled-zone1.")]
	public Task DisabledVolumePlanRoundTripRestoresOriginalConfiguration ()
		=> Run (prepare: true, volume: 1.4m, restoreAfter: true, allModes: true);

	private static async Task Run (bool prepare, RainPointScheduleMode mode = RainPointScheduleMode.Irrigation, decimal? volume = null, bool restoreAfter = false, bool allModes = false)
		{
		if (Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SCHEDULE") != "disabled-zone1")
			{
			Assert.Ignore ("Explicit disabled-zone1 schedule opt-in is required.");
			}
		string? path = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS");
		if (string.IsNullOrWhiteSpace (path))
			{
			Assert.Ignore ("Private account settings are required.");
			}
		string journalPath = Path.GetFullPath (path!) + ".schedule-comparison.json";
		using RainPointCloudClient client = new ();
		using CancellationTokenSource timeout = new (TimeSpan.FromSeconds (120));
		try
			{
			Account account = JsonSerializer.Deserialize<Account> (File.ReadAllText (path!))!;
			await client.LoginAsync (account.Email, account.Password, account.AreaCode, timeout.Token);
			List<RainPointHub> hubs = [];
			foreach (RainPointHome home in await client.GetHomesAsync (timeout.Token))
				{
				hubs.AddRange ((await client.GetHubsAsync (home.Id, timeout.Token)).Where (hub => hub.Model is "HWG023WBRF" or "HWG023WBRF-V2"
					 && hub.Devices.Any (device => device.Model == "HTV345FRF")));
				}
			Assert.That (hubs, Has.Count.EqualTo (1), "Requires exactly one matching hub.");
			RainPointHub target = hubs[0];
			RainPointDevice[] devices = target.Devices.Where (device => device.Model == "HTV345FRF").ToArray ();
			Assert.That (devices, Has.Length.EqualTo (1), "Requires exactly one matching timer.");
			// One login covers preparation and restoration for every mode; rapid re-login is rejected by the service.
			foreach (RainPointScheduleMode selectedMode in allModes
			 ? new[] { RainPointScheduleMode.Irrigation, RainPointScheduleMode.CycleAndSoak, RainPointScheduleMode.Misting }
			 : new[] { mode })
				{
				RainPointScheduleSnapshot before = await client.GetTimerSchedulesAsync (target, devices[0].Address, 1, timeout.Token);
				Assert.That (before.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
				if (prepare)
					{
					Assert.That (File.Exists (journalPath), Is.False, "Clean up the earlier comparison fixture first.");
					Assert.That (before.Schedules, Is.Empty, "The comparison fixture only starts with an empty zone 1 plan list.");
					RainPointIrrigationSchedule plan = new ()
						{
						Enabled = false,
						WaterLimitLitres = volume,
						StartTime = new TimeSpan (23, 57, 0),
						Duration = TimeSpan.FromMinutes (1),
						Repeat = RainPointScheduleRepeat.EveryDay,
						EffectiveDate = new DateTime (2083, 12, 31)
						};
					RainPointCycleAndSoakSchedule cyclePlan = new ()
						{
						WaterLimitLitres = volume,
						StartTime = plan.StartTime,
						EffectiveDate = plan.EffectiveDate
						};
					RainPointMistingSchedule mistPlan = new ()
						{
						WaterLimitLitres = volume,
						StartTime = plan.StartTime,
						EffectiveDate = plan.EffectiveDate
						};
					bool cycle = selectedMode == RainPointScheduleMode.CycleAndSoak;
					bool mist = selectedMode == RainPointScheduleMode.Misting;
					string record = mist ? ScheduleEditor.EncodeMisting (mistPlan) : cycle ? ScheduleEditor.EncodeCycleAndSoak (cyclePlan) : ScheduleEditor.Encode (plan);
					ComparisonJournal journal = new ()
						{
						HomeId = target.HomeId,
						HubId = target.Id,
						DeviceId = devices[0].Id!.Value,
						Address = devices[0].Address,
						Original = before.Parameter!,
						Expected = ScheduleEditor.Edit (before, ScheduleEdit.Add, -1, record, false)
						};
					// Persist recovery information before any request; never print identifiers or encoded settings.
					using (FileStream stream = new (journalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
						{
						JsonSerializer.Serialize (stream, journal);
						}
					try
						{
						if (mist)
							await client.AddTimerScheduleAsync (target, before, mistPlan, timeout.Token);
						else if (cycle)
							await client.AddTimerScheduleAsync (target, before, cyclePlan, timeout.Token);
						else
							await client.AddTimerScheduleAsync (target, before, plan, timeout.Token);
						RainPointScheduleSnapshot after = await WaitForParameter (client, target, devices[0].Address, journal.Expected, timeout.Token);
						RainPointSchedule saved = after.Schedules.Single ();
						using (Assert.EnterMultipleScope ())
							{
							Assert.That (saved.Enabled, Is.False);
							Assert.That (saved.WaterLimitLitres, Is.EqualTo (volume));
							Assert.That (saved.StartTime, Is.EqualTo (plan.StartTime));
							Assert.That (saved.Duration, Is.EqualTo (mist ? mistPlan.Duration : cycle ? cyclePlan.Duration : plan.Duration));
							Assert.That (saved.Mode, Is.EqualTo (selectedMode));
							Assert.That (saved.CycleWateringTime, Is.EqualTo (mist ? mistPlan.CycleWateringTime : cycle ? cyclePlan.CycleWateringTime : (TimeSpan?)null));
							Assert.That (saved.CyclePauseTime, Is.EqualTo (mist ? mistPlan.CyclePauseTime : cycle ? cyclePlan.CyclePauseTime : (TimeSpan?)null));
							Assert.That (saved.EffectiveDate, Is.EqualTo (plan.EffectiveDate));
							}
						TestContext.Progress.WriteLine (volume.HasValue ? "Read back disabled volume limit: 1.4 L. Threshold enforcement was not exercised." : "Read back duration-only plan.");
						TestContext.Progress.WriteLine ("Saved one DISABLED zone-1 plan: " + (mist ? "misting, 10-minute configured duration, 10-second bursts, 20-second pauses" : cycle ? "cycle-and-soak, 10 minutes total watering, 5-minute cycles, 30-minute pauses" : "normal irrigation, 60 seconds") + ", 23:57, daily, effective 31 December 2083. No watering command sent. Recovery journal retained until exact restoration.");
						}
					finally
						{
						if (restoreAfter)
							await Restore (client, target, devices[0], journalPath);
						}
					}
				else
					{
					await Restore (client, target, devices[0], journalPath);
					}
				}
			}
		catch (Exception error) when (error is not AssertionException && error is not SuccessException && error is not IgnoreException)
			{
			if (error is RainPointException protocol)
				TestContext.Progress.WriteLine ($"Sanitized cloud error: API {protocol.ApiCode}, HTTP {protocol.HttpStatus}.");
			Assert.Fail ("Schedule fixture failed (" + error.GetType ().Name + "). Details omitted. If preparation reached the write, retain the private journal and run cleanup explicitly.");
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
				catch (Exception error) when (error is not OutOfMemoryException) { TestContext.Progress.WriteLine ("Remote logout did not complete; disposing client."); }
				}
			}
		}

	private static async Task Restore (RainPointCloudClient client, RainPointHub target, RainPointDevice device, string journalPath)
		{
		using CancellationTokenSource cleanup = new (TimeSpan.FromSeconds (60));
		RainPointScheduleSnapshot before = await client.GetTimerSchedulesAsync (target, device.Address, 1, cleanup.Token);
		Assert.That (File.Exists (journalPath), Is.True, "No comparison fixture journal exists.");
		ComparisonJournal journal = JsonSerializer.Deserialize<ComparisonJournal> (File.ReadAllText (journalPath))!;
		Assert.That (target.HomeId == journal.HomeId && target.Id == journal.HubId
			 && device.Id == journal.DeviceId && device.Address == journal.Address, Is.True, "Recovery device does not match discovery.");
		if (before.Parameter != journal.Original)
			{
			Assert.That (before.Parameter == journal.Expected, Is.True, "Configuration changed since preparation; refusing to overwrite it. Keep the private journal for reconciliation.");
			Assert.That (before.Schedules, Has.Count.EqualTo (1));
			Assert.That (before.Schedules[0].Enabled, Is.False);
			await client.DeleteTimerScheduleAsync (target, before, 0, cleanup.Token);
			await WaitForParameter (client, target, device.Address, journal.Original, cleanup.Token);
			}
		File.Delete (journalPath);
		TestContext.Progress.WriteLine ("Comparison plan removed; complete original timer configuration restored exactly. Other zones and settings unchanged. No watering command sent.");
		}

	private static async Task<RainPointScheduleSnapshot> WaitForParameter (RainPointCloudClient client, RainPointHub hub, int address,
		 string expected, CancellationToken cancellationToken)
		{
		for (int attempt = 0; attempt < 10; attempt++)
			{
			RainPointScheduleSnapshot result = await client.GetTimerSchedulesAsync (hub, address, 1, cancellationToken);
			if (result.Availability == TimerReadingAvailability.Decoded && result.Parameter == expected)
				{
				return result;
				}
			await Task.Delay (TimeSpan.FromSeconds (3), cancellationToken);
			}
		throw new InvalidOperationException ("Expected configuration did not appear within the bounded read-back window.");
		}

	private sealed class Account
		{
		[JsonPropertyName ("email")] public string Email { get; set; } = string.Empty;
		[JsonPropertyName ("password")] public string Password { get; set; } = string.Empty;
		[JsonPropertyName ("areaCode")] public string AreaCode { get; set; } = "44";
		}

	private sealed class ComparisonJournal
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
		[JsonPropertyName ("expected")] public string Expected { get; set; } = string.Empty;
		}
	}
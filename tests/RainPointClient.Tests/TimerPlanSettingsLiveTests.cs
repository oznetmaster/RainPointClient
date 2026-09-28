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
public sealed class TimerPlanSettingsLiveTests
	{
	[Test, Explicit ("Temporarily changes zone-1 seasonal adjustment and rain delay. Requires RAINPOINT_LIVE_SETTINGS_EDIT=zone1 and RAINPOINT_LIVE_RAIN_END. Run cleanup afterward.")]
	public Task PrepareZone1SettingsForAppComparison () => Run (true);

	[Test, Explicit ("Restores only this fixture's journaled settings after exact comparison. Requires RAINPOINT_LIVE_SETTINGS_EDIT=zone1.")]
	public Task RestoreZone1Settings () => Run (false);

	private static async Task Run (bool prepare)
		{
		if (Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS_EDIT") != "zone1")
			Assert.Ignore ("Explicit zone-1 settings opt-in is required.");
		string? path = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS");
		if (string.IsNullOrWhiteSpace (path))
			{
			Assert.Ignore ("Private account settings are required.");
			}
		string journalPath = Path.GetFullPath (path!) + ".settings-comparison.json";
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
			RainPointScheduleSnapshot before = await client.GetTimerSchedulesAsync (target, devices[0].Address, 1, timeout.Token);
			Assert.That (before.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
			if (prepare)
				{
				Assert.That (File.Exists (journalPath), Is.False, "Restore any earlier fixture before preparation.");
				for (int zone = 1; zone <= 3; zone++)
					{
					RainPointScheduleSnapshot check = await client.GetTimerSchedulesAsync (target, devices[0].Address, zone, timeout.Token);
					Assert.That (check.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
					Assert.That (check.Schedules, Is.Empty, "Settings comparison requires no existing plans on any zone.");
					}
				Assert.That (before.SeasonalAdjustmentAvailability, Is.EqualTo (TimerReadingAvailability.Decoded));
				Assert.That (before.RainDelayAvailability, Is.EqualTo (TimerReadingAvailability.Decoded));
				string? endText = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_RAIN_END");
				Assert.That (DateTime.TryParseExact (endText, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
					 System.Globalization.DateTimeStyles.None, out DateTime end), Is.True, "Supply an explicit home-local rain-delay end time.");
				Assert.That (end > DateTime.UtcNow.AddHours (-12) && end < DateTime.UtcNow.AddDays (4), Is.True, "Use a bounded comparison date near the current date.");
				int[] months = Enumerable.Repeat (90, 12).ToArray ();
				string seasonOnly = TimerPlanSettings.EditSeason (before, months);
				RainPointScheduleSnapshot preview = Preview (before, seasonOnly);
				ComparisonJournal journal = new ()
					{
					HomeId = target.HomeId,
					HubId = target.Id,
					DeviceId = devices[0].Id!.Value,
					Address = devices[0].Address,
					Original = before.Parameter!,
					SeasonOnly = seasonOnly,
					Expected = TimerPlanSettings.EditRainDelay (preview, end),
					OriginalMonths = before.SeasonalPercentages.ToArray (),
					OriginalRainEnd = before.RainDelayUntil
					};
				using (FileStream stream = new (journalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
					JsonSerializer.Serialize (stream, journal);
				await client.SetTimerSeasonalAdjustmentAsync (target, before, months, timeout.Token);
				RainPointScheduleSnapshot intermediate = await WaitForParameter (client, target, devices[0].Address, journal.SeasonOnly, timeout.Token);
				await client.SetTimerRainDelayAsync (target, intermediate, end, timeout.Token);
				RainPointScheduleSnapshot after = await WaitForParameter (client, target, devices[0].Address, journal.Expected, timeout.Token);
				Assert.That (after.SeasonalPercentages, Is.EqualTo (months));
				Assert.That (after.RainDelayUntil, Is.EqualTo (end));
				TestContext.Progress.WriteLine ($"Zone 1: all months 90%; rain delay until {end:yyyy-MM-dd HH:mm:ss} home-local. No saved plans or valve commands. Private recovery journal retained for cleanup.");
				}
			else
				{
				Assert.That (File.Exists (journalPath), Is.True, "No settings recovery journal exists.");
				ComparisonJournal journal = JsonSerializer.Deserialize<ComparisonJournal> (File.ReadAllText (journalPath))!;
				Assert.That (target.HomeId == journal.HomeId && target.Id == journal.HubId && devices[0].Id == journal.DeviceId && devices[0].Address == journal.Address, Is.True);
				Assert.That (before.Parameter == journal.Original || before.Parameter == journal.SeasonOnly || before.Parameter == journal.Expected, Is.True,
					"Configuration changed externally; retain journal and reconcile. Nothing will be overwritten.");
				if (before.Parameter == journal.Expected && before.Parameter != journal.SeasonOnly)
					{
					await client.SetTimerRainDelayAsync (target, before, journal.OriginalRainEnd, timeout.Token);
					before = await WaitForParameter (client, target, devices[0].Address, journal.SeasonOnly, timeout.Token);
					}
				if (before.Parameter != journal.Original)
					{
					await client.SetTimerSeasonalAdjustmentAsync (target, before, journal.OriginalMonths, timeout.Token);
					await WaitForParameter (client, target, devices[0].Address, journal.Original, timeout.Token);
					}
				File.Delete (journalPath);
				TestContext.Progress.WriteLine ("Seasonal adjustment and rain delay restored; complete original timer configuration matches exactly. Recovery journal removed.");
				}
			}
		catch (Exception error) when (error is not AssertionException && error is not SuccessException && error is not IgnoreException)
			{
			Assert.Fail ("Settings fixture failed (" + error.GetType ().Name + "). Details omitted. If preparation reached the write, retain the private journal and run cleanup explicitly.");
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

	private static RainPointScheduleSnapshot Preview (RainPointScheduleSnapshot source, string parameter)
		{
		RainPointScheduleSnapshot result = ScheduleDecoder.Decode (new RainPointDevice { Address = source.Address, PortNumber = source.PortNumber, Parameter = parameter }, source.Zone);
		result.Parameter = parameter;
		result.PortNumber = source.PortNumber;
		result.FirmwareVersion = source.FirmwareVersion;
		TimerPlanSettings.Decode (result);
		return result;
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
		[JsonPropertyName ("seasonOnly")] public string SeasonOnly { get; set; } = string.Empty;
		[JsonPropertyName ("originalMonths")] public int[] OriginalMonths { get; set; } = [];
		[JsonPropertyName ("originalRainEnd")]
		public DateTime? OriginalRainEnd
			{
			get; set;
			}
		[JsonPropertyName ("expected")] public string Expected { get; set; } = string.Empty;
		}
	}
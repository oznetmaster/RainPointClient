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

[TestFixture, NonParallelizable, Category ("Live")]
public sealed class CalendarLiveTests
	{
	[Test, Explicit ("Reads all zones and projects saved plans locally. No valve, plan or device-configuration commands.")]
	public async Task ProjectsAllZonesWithoutChangingPlans ()
		{
		string? path = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS");
		if (string.IsNullOrWhiteSpace (path))
			{
			Assert.Ignore ("Set RAINPOINT_LIVE_SETTINGS to an ignored private settings file.");
			}
		using RainPointCloudClient client = new ();
		using CancellationTokenSource timeout = new (TimeSpan.FromSeconds (120));
		try
			{
			Account account = JsonSerializer.Deserialize<Account> (File.ReadAllText (path!))!;
			await client.LoginAsync (account.Email, account.Password, account.AreaCode, timeout.Token);
			List<RainPointHub> hubs = [];
			foreach (RainPointHome home in await client.GetHomesAsync (timeout.Token))
				{
				hubs.AddRange ((await client.GetHubsAsync (home.Id, timeout.Token))
					 .Where (hub => hub.Model is "HWG023WBRF" or "HWG023WBRF-V2"
						 && hub.Devices.Any (device => device.Model == "HTV345FRF")));
				}
			Assert.That (hubs, Has.Count.EqualTo (1), "The fixture requires exactly one matching hub.");
			RainPointDevice[] timers = hubs[0].Devices.Where (device => device.Model == "HTV345FRF").ToArray ();
			Assert.That (timers, Has.Length.EqualTo (1), "The fixture requires exactly one matching timer.");

			for (int zone = 1; zone <= 3; zone++)
				{
				var snapshot = await client.GetTimerSchedulesAsync (hubs[0], timers[0].Address, zone, timeout.Token);
				Assert.That (snapshot.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
				DateTime first = DateTime.SpecifyKind (DateTime.UtcNow.Date, DateTimeKind.Unspecified);
				var preview = ScheduleCalendar.Create (snapshot, first, first.AddDays (30));
				Assert.That (preview.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
				Assert.That (preview.Occurrences.All (item => item.Zone == zone && item.Plan.Enabled && item.StartsAt.Kind == DateTimeKind.Unspecified), Is.True);
				if (!snapshot.Schedules.Any (plan => plan.Enabled))
					Assert.That (preview.Occurrences, Is.Empty);
				var next = preview.GetNextOccurrence (first);
				Assert.That (next is null || next.RainDelay != RainPointCalendarRainDelay.Delayed, Is.True);
				TestContext.Progress.WriteLine ($"Zone {zone}: {snapshot.Schedules.Count} saved plan(s), {preview.Occurrences.Count} projected starts across 31 dates. No execution or populated-app comparison claim.");
				}

			}
		catch (Exception error) when (error is not AssertionException && error is not SuccessException && error is not IgnoreException)
			{
			Assert.Fail ("Calendar read-only check failed (" + error.GetType ().Name + "). " + (error is RainPointException protocol ? protocol.Message : "Account details and response bodies are omitted."));
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
				catch (Exception error) when (error is not OutOfMemoryException)
					{
					TestContext.Progress.WriteLine ("Remote logout did not complete; client will be disposed.");
					}
				}
			}
		}

	[Test, Explicit ("Projects only the private scheduled-test recovery snapshot. No account login, network access or writes. Used for simultaneous official-app comparison.")]
	public void ProjectsJournaledPlanWithoutCloudAccess ()
		{
		string? path = Environment.GetEnvironmentVariable ("RAINPOINT_CALENDAR_CAPTURE");
		if (string.IsNullOrWhiteSpace (path))
			Assert.Ignore ("Supply the private scheduled-test journal snapshot.");
		CalendarCapture capture = JsonSerializer.Deserialize<CalendarCapture> (File.ReadAllText (path!))!;
		RainPointScheduleSnapshot snapshot = ScheduleDecoder.Decode (new RainPointDevice { Address = 1, PortNumber = 3, Parameter = capture.Enabled }, 1);
		snapshot.Parameter = capture.Enabled;
		snapshot.PortNumber = 3;
		TimerPlanSettings.Decode (snapshot);
		Assert.That (snapshot.SeasonalAdjustmentAvailability, Is.EqualTo (TimerReadingAvailability.Decoded));
		Assert.That (snapshot.RainDelayAvailability, Is.EqualTo (TimerReadingAvailability.Decoded));
		DateTime date = DateTime.SpecifyKind (TimeZoneInfo.ConvertTimeFromUtc (capture.DueUtc.UtcDateTime, TimeZoneInfo.FindSystemTimeZoneById ("GMT Standard Time")).Date, DateTimeKind.Unspecified);
		var calendar = ScheduleCalendar.Create (snapshot, date, date.AddDays (35));
		Assert.That (calendar.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
		Assert.That (calendar.Occurrences, Is.Not.Empty);
		foreach (var entry in calendar.Occurrences)
			TestContext.Progress.WriteLine ($"Projected {entry.StartsAt:yyyy-MM-dd HH:mm}; duration={entry.CalendarDuration}; seasonal={entry.SeasonalPercentage}%; rain={entry.RainDelay}.");
		TestContext.Progress.WriteLine ($"Next non-delayed occurrence: {calendar.GetNextOccurrence (date)?.StartsAt:yyyy-MM-dd HH:mm}.");
		}
	[Test, Explicit ("Reads only home timezone rules and checks the previously observed interval calendar. No plans or commands are sent.")]
	public async Task ReportedHomeRulesMatchObservedLondonCalendar ()
		{
		string? path = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS");
		if (string.IsNullOrWhiteSpace (path))
			Assert.Ignore ("Private account settings required.");
		Account account = JsonSerializer.Deserialize<Account> (File.ReadAllText (path!))!;
		using RainPointCloudClient client = new ();
		using CancellationTokenSource token = new (TimeSpan.FromMinutes (2));
		try
			{
			await client.LoginAsync (account.Email, account.Password, account.AreaCode, token.Token);
			long? selectedHome = null;
			foreach (RainPointHome candidate in await client.GetHomesAsync (token.Token))
				foreach (RainPointHub hub in await client.GetHubsAsync (candidate.Id, token.Token))
					if (hub.Model is "HWG023WBRF" or "HWG023WBRF-V2" && hub.Devices.Any (d => d.Model == "HTV345FRF"))
						{
						Assert.That (selectedHome, Is.Null, "Require exactly one matching paired kit.");
						selectedHome = candidate.Id;
						}
			Assert.That (selectedHome, Is.Not.Null);
			var home = await client.GetHomeAsync (selectedHome!.Value, token.Token);
			Assert.That (home.TimeZoneName, Is.EqualTo ("Europe/London"));
			Assert.That (home.CalendarTimeZone, Is.Not.Null, "The home must report decodable offset and daylight transitions.");
			var preview = ScheduleCalendar.Create (CalendarTimeZoneTests.IntervalSnapshot (1, new DateTime (2026, 9, 27)), new DateTime (2026, 10, 23), new DateTime (2026, 10, 31), home.CalendarTimeZone!);
			Assert.That (preview.Occurrences.Select (o => o.StartsAt.Day), Is.EqualTo (new[] { 23, 25, 26, 28, 30 }));
			TestContext.Progress.WriteLine ($"Reported home rules contain {home.CalendarTimeZone!.Transitions.Count} transitions; projected 23/25/26/28/30 October matches the observed official app. No network writes.");
			}
		finally
			{
			if (client.HasValidSession)
				{
				using CancellationTokenSource logout = new (TimeSpan.FromSeconds (15));
				await client.LogoutAsync (logout.Token);
				}
			}
		}

	private sealed class CalendarCapture
		{
		[JsonPropertyName ("enabled")] public string Enabled { get; set; } = string.Empty;
		[JsonPropertyName ("dueUtc")]
		public DateTimeOffset DueUtc
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
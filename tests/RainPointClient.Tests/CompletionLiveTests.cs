using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture, NonParallelizable, Category ("Live")]
public sealed class CompletionLiveTests
	{
	[Test, Explicit ("Reads catalog, firmware and all zones, and renews authentication once. No valve or device-configuration commands.")]
	public async Task ReadsAllZonesCatalogAndRenewsWithoutDeviceWrites ()
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

			var catalog = await client.GetProductCatalogAsync (timeout.Token);
			Assert.That (catalog.Models.Any (m => m.Model == "HTV345FRF"), Is.True, "Target timer is absent from the catalog.");
			TestContext.Progress.WriteLine ($"Catalog: {catalog.Models.Count} typed model variants; target timer present.");
			var hubFirmware = await client.GetHubFirmwareAsync (hubs[0], timeout.Token);
			var timerFirmware = await client.GetTimerFirmwareAsync (hubs[0], timers[0].Address, timeout.Token);
			TestContext.Progress.WriteLine ($"Firmware: hub {hubFirmware.InstalledVersion}; timer {timerFirmware.InstalledVersion}; reported RF channel {hubs[0].RfChannel?.ToString () ?? "unknown"}.");
			for (int zone = 1; zone <= 3; zone++)
				{
				var plans = await client.GetTimerSchedulesAsync (hubs[0], timers[0].Address, zone, timeout.Token);
				Assert.That (plans.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
				Assert.That (plans.SeasonalAdjustmentAvailability, Is.EqualTo (TimerReadingAvailability.Decoded));
				Assert.That (plans.RainDelayAvailability, Is.EqualTo (TimerReadingAvailability.Decoded));
				TestContext.Progress.WriteLine ($"Zone {zone}: {plans.Schedules.Count} plans; seasonal, rain delay and zone settings readable.");
				DateTime end = DateTime.SpecifyKind (DateTime.UtcNow.Date, DateTimeKind.Unspecified);
				var calendar = ScheduleCalendar.Create (plans, end, end.AddDays (30));
				Assert.That (calendar.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
				Assert.That (calendar.Occurrences.All (item => item.Zone == zone && item.Plan.Enabled), Is.True);
				TestContext.Progress.WriteLine ($"Zone {zone}: {calendar.Occurrences.Count} local-calendar projections across 31 dates. No execution claim.");
				var usage = await client.GetTimerWaterUsageAsync (hubs[0], timers[0].Address, zone, RainPointUsagePeriod.Day, end.AddDays (-29), end, timeout.Token);
				Assert.That (usage.All (row => row.Litres is null or >= 0), Is.True);
				var events = await client.GetEventsAsync (hubs[0].HomeId, new RainPointEventQuery { HubId = hubs[0].Id, Address = timers[0].Address, Zone = zone, Limit = 2 }, timeout.Token);
				Assert.That (events.Events.All (item => (item.Zone == zone || (item.Zone == 0 && item.Kind == RainPointEventKind.SubDevicePowerOn))), Is.True);
				TestContext.Progress.WriteLine ($"Zone {zone}: {usage.Count} usage buckets; {events.Events.Count} bounded events.");
				}
			Assert.That (client.CanRefreshSession, Is.True, "This account did not supply a refresh token.");
			// Trigger one proactive-renewal check with an injected near-expiry clock; do not wait for the real token to expire.
			var recovery = new RainPointSessionRecovery (client, null, () => client.SessionExpiresAt!.Value.AddSeconds (-30), Task.Delay);
			await recovery.CheckAsync (timeout.Token);
			Assert.That (recovery.State, Is.EqualTo (RainPointSessionState.Healthy));
			Assert.That ((await client.GetHomesAsync (timeout.Token)).Any (h => h.Id == hubs[0].HomeId), Is.True);
			TestContext.Progress.WriteLine ("One automatic-renewal path completed and authenticated discovery succeeded. This is not an endurance or password-relogin test.");


			}
		catch (Exception error) when (error is not AssertionException && error is not SuccessException && error is not IgnoreException)
			{
			Assert.Fail ("Completion read-only check failed (" + error.GetType ().Name + "). " + (error is RainPointException protocol ? protocol.Message : "Account details and response bodies are omitted."));
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

	private sealed class Account
		{
		[JsonPropertyName ("email")] public string Email { get; set; } = string.Empty;
		[JsonPropertyName ("password")] public string Password { get; set; } = string.Empty;
		[JsonPropertyName ("areaCode")] public string AreaCode { get; set; } = "44";
		}
	}
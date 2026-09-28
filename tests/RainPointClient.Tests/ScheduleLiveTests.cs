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
public sealed class ScheduleLiveTests
	{
	[Test, Explicit ("Reads saved cloud schedules; requires RAINPOINT_LIVE_SETTINGS. No valve or configuration commands.")]
	public async Task ReadsSavedSchedulesWithoutChangingConfiguration ()
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
				RainPointScheduleSnapshot snapshot = await client.GetTimerSchedulesAsync (hubs[0], timers[0].Address, zone, timeout.Token);
				TestContext.Progress.WriteLine ($"Zone {zone}: schedule availability={snapshot.Availability}; saved plans={snapshot.Schedules.Count}.");
				Assert.That (snapshot.Availability, Is.EqualTo (TimerReadingAvailability.Decoded), "Saved configuration was not decoded.");
				TestContext.Progress.WriteLine ($"Settings: seasonal={snapshot.SeasonalAdjustmentAvailability} [{string.Join (",", snapshot.SeasonalPercentages)}]; rain delay={snapshot.RainDelayAvailability}, end={snapshot.RainDelayUntil:yyyy-MM-dd HH:mm:ss}.");
				}
			}
		catch (Exception error) when (error is not AssertionException && error is not SuccessException && error is not IgnoreException)
			{
			Assert.Fail ("Read-only schedule check failed (" + error.GetType ().Name + "). Account details and response bodies are omitted.");
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
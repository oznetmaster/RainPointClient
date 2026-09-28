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
public sealed class HistoryLiveTests
	{
	[Test, Explicit ("Reads usage and event history; requires RAINPOINT_LIVE_SETTINGS. No valve or configuration commands.")]
	public async Task ReadsUsageAndEventHistoryWithoutChangingConfiguration ()
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

			DateTime end = DateTime.SpecifyKind (DateTime.UtcNow.Date, DateTimeKind.Unspecified);
			DateTime start = end.AddDays (-29);
			foreach (RainPointUsagePeriod period in new[] { RainPointUsagePeriod.Day, RainPointUsagePeriod.Month })
				{
				var usage = await client.GetTimerWaterUsageAsync (hubs[0], timers[0].Address, 1, period, start, end, timeout.Token);
				TestContext.Progress.WriteLine ($"Zone 1 {period}: reported buckets={usage.Count}; sum of available readings={usage.Sum (row => row.Litres ?? 0)} L.");
				Assert.That (usage.All (row => row.Litres is null or >= 0), Is.True);
				}
			RainPointEventQuery query = new ()
				{
				HubId = hubs[0].Id,
				Address = timers[0].Address,
				Zone = 1,
				Code = 1,
				Limit = 2
				};
			RainPointEventPage page = await client.GetEventsAsync (hubs[0].HomeId, query, timeout.Token);
			TestContext.Progress.WriteLine ($"Zone 1 watering events: {page.Events.Count}; limit reached={page.IsLimitReached}.");
			foreach (RainPointEvent item in page.Events)
				{
				TestContext.Progress.WriteLine ($"Reported start={item.ReportedLocalTime:yyyy-MM-dd HH:mm:ss}; duration={item.Duration?.TotalSeconds} s; usage={item.WaterUsedLitres} L; modes={item.WorkModeCode}/{item.ControlModeCode}.");
				}
			Assert.That (page.Events.All (item => item.Kind == RainPointEventKind.Watering && item.Zone == 1), Is.True);
			if (page.OldestTimestamp.HasValue)
				{
				query.End = page.OldestTimestamp;
				var older = await client.GetEventsAsync (hubs[0].HomeId, query, timeout.Token);
				Assert.That (older.Events.All (item => item.CloudTimestamp <= query.End.Value), Is.True);
				TestContext.Progress.WriteLine ($"Older bounded page: {older.Events.Count}; distinct new IDs={older.Events.Count (item => page.Events.All (prior => prior.Id != item.Id))}.");
				}

			}
		catch (Exception error) when (error is not AssertionException && error is not SuccessException && error is not IgnoreException)
			{
			Assert.Fail ("Read-only history check failed (" + error.GetType ().Name + "). Account details and response bodies are omitted.");
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
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

namespace RainPointClient.Tests;

[TestFixture, NonParallelizable, Category ("Live"), Category ("Configuration")]
public sealed class ZoneProfileLiveTests
	{
	[Test, Explicit ("Reads all zones, toggles only zone-1 recommendation preference, and restores it. Requires RAINPOINT_LIVE_PROFILE=zone1-restore.")]
	public Task ReadsAllZonesAndRestoresZone1Preference () => Run (false);
	[Test, Explicit ("Restores the private zone-profile journal. Requires RAINPOINT_LIVE_PROFILE=zone1-restore.")]
	public Task RestoreJournaledPreference () => Run (true);
	private static async Task Run (bool recovery)
		{
		string? path = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS");
		if (string.IsNullOrWhiteSpace (path) || Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_PROFILE") != "zone1-restore")
			Assert.Ignore ("Private settings and zone1-restore opt-in are required.");
		string journalPath = Path.GetFullPath (path!) + ".profile.json";
		using RainPointCloudClient client = new ();
		using CancellationTokenSource timeout = new (TimeSpan.FromSeconds (120));
		try
			{
			Account account = JsonSerializer.Deserialize<Account> (File.ReadAllText (path!))!;
			await client.LoginAsync (account.Email, account.Password, account.AreaCode, timeout.Token);
			List<RainPointHub> hubs = [];
			foreach (var home in await client.GetHomesAsync (timeout.Token))
				hubs.AddRange ((await client.GetHubsAsync (home.Id, timeout.Token)).Where (h => h.Model is "HWG023WBRF" or "HWG023WBRF-V2" && h.Devices.Any (d => d.Model == "HTV345FRF")));
			Assert.That (hubs, Has.Count.EqualTo (1));
			RainPointHub hub = hubs[0];
			var timers = hub.Devices.Where (d => d.Model == "HTV345FRF").ToArray ();
			Assert.That (timers, Has.Length.EqualTo (1));
			var timer = timers[0];
			if (recovery)
				{
				await Restore (client, hub, timer.Address, journalPath);
				return;
				}
			Assert.That (File.Exists (journalPath), Is.False, "Resolve the existing profile recovery journal first.");
			var catalog = await client.GetZoneProfileCatalogAsync (timeout.Token);
			Assert.That (catalog.Count, Is.GreaterThan (0));
			TestContext.Progress.WriteLine ($"Read {catalog.Count} profile categories.");
			List<RainPointZoneProfileSnapshot> before = [];
			var plans = await client.GetTimerSchedulesAsync (hub, timer.Address, 1, timeout.Token);
			for (int zone = 1; zone <= 3; zone++)
				{
				var profile = await client.GetZoneProfileAsync (hub, timer.Address, zone, timeout.Token);
				Assert.That (profile.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
				Assert.That (profile.IsConfigured, Is.True, "This restoration fixture requires an existing profile.");
				before.Add (profile);
				var recommendations = await client.GetZoneRecommendationsAsync (hub, timer.Address, zone, timeout.Token);
				Assert.That (recommendations, Is.Not.Empty);
				TestContext.Progress.WriteLine ($"Zone {zone}: readable profile and {recommendations.Count} typed recommendation(s).");
				}
			Journal journal = new ()
				{
				Home = hub.HomeId,
				Hub = hub.Id,
				Device = timer.Id!.Value,
				OriginalEnabled = before[0].RecommendationsEnabled!.Value,
				OriginalOptions = before[0].SelectedOptionIds.ToArray ()
				};
			using (var file = new FileStream (journalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				JsonSerializer.Serialize (file, journal);
			try
				{
				await client.SetZoneProfileAsync (hub, before[0], !journal.OriginalEnabled, journal.OriginalOptions, timeout.Token);
				await WaitForProfile (client, hub, timer.Address, !journal.OriginalEnabled, journal.OriginalOptions, timeout.Token);
				TestContext.Progress.WriteLine ("Zone-1 recommendation preference changed and read back. No plan or valve operation sent.");
				}
			finally { await Restore (client, hub, timer.Address, journalPath); }
			for (int zone = 1; zone <= 3; zone++)
				{
				var after = await client.GetZoneProfileAsync (hub, timer.Address, zone, timeout.Token);
				Assert.That (after.RecommendationsEnabled, Is.EqualTo (before[zone - 1].RecommendationsEnabled));
				Assert.That (after.SelectedOptionIds, Is.EqualTo (before[zone - 1].SelectedOptionIds));
				}
			var afterPlans = await client.GetTimerSchedulesAsync (hub, timer.Address, 1, timeout.Token);
			Assert.That (afterPlans.Parameter == plans.Parameter, Is.True, "Timer settings/plans changed during the profile check.");
			TestContext.Progress.WriteLine ("All three profiles restored semantically; timer settings and plans unchanged. No watering.");
			}
		catch (Exception e) when (e is not AssertionException && e is not IgnoreException)
			{
			Assert.Fail ("Profile live check failed (" + e.GetType ().Name + "). " + (e is RainPointException p ? "API " + p.ApiCode : "Details omitted.") + " Keep any private recovery journal.");
			}
		finally
			{
			if (client.HasValidSession)
				{
				using CancellationTokenSource logout = new (TimeSpan.FromSeconds (15));
				try
					{
					await client.LogoutAsync (logout.Token);
					}
				catch (Exception e) when (e is not OutOfMemoryException) { TestContext.Progress.WriteLine ("Logout incomplete; disposing client."); }
				}
			}
		}
	private static async Task WaitForProfile (RainPointCloudClient client, RainPointHub hub, int address, bool enabled, int[] options, CancellationToken token)
		{
		for (int n = 0; n < 10; n++)
			{
			var current = await client.GetZoneProfileAsync (hub, address, 1, token);
			if (current.Availability == TimerReadingAvailability.Decoded && current.RecommendationsEnabled == enabled && current.SelectedOptionIds.SequenceEqual (options))
				return;
			await Task.Delay (TimeSpan.FromSeconds (3), token);
			}
		Assert.Fail ("Profile did not match within the bounded read-back window.");
		}
	private static async Task Restore (RainPointCloudClient client, RainPointHub hub, int address, string path)
		{
		using CancellationTokenSource timeout = new (TimeSpan.FromSeconds (60));
		Journal journal = JsonSerializer.Deserialize<Journal> (File.ReadAllText (path))!;
		Assert.That (hub.HomeId == journal.Home && hub.Id == journal.Hub && hub.Devices.Single (d => d.Address == address).Id == journal.Device, Is.True, "Recovery identity mismatch.");
		var current = await client.GetZoneProfileAsync (hub, address, 1, timeout.Token);
		Assert.That (current.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
		Assert.That (current.SelectedOptionIds.SequenceEqual (journal.OriginalOptions), Is.True, "Options changed outside the fixture; refusing to overwrite them.");
		if (current.RecommendationsEnabled != journal.OriginalEnabled)
			{
			await client.SetZoneProfileAsync (hub, current, journal.OriginalEnabled, journal.OriginalOptions, timeout.Token);
			await WaitForProfile (client, hub, address, journal.OriginalEnabled, journal.OriginalOptions, timeout.Token);
			}
		File.Delete (path);
		TestContext.Progress.WriteLine ("Original zone-1 recommendation preference restored; recovery journal removed.");
		}
	private sealed class Account
		{
		[JsonPropertyName ("email")] public string Email { get; set; } = string.Empty;
		[JsonPropertyName ("password")] public string Password { get; set; } = string.Empty;
		[JsonPropertyName ("areaCode")] public string AreaCode { get; set; } = "44";
		}
	private sealed class Journal
		{
		[JsonPropertyName ("home")]
		public long Home
			{
			get; set;
			}
		[JsonPropertyName ("hub")]
		public long Hub
			{
			get; set;
			}
		[JsonPropertyName ("device")]
		public long Device
			{
			get; set;
			}
		[JsonPropertyName ("enabled")]
		public bool OriginalEnabled
			{
			get; set;
			}
		[JsonPropertyName ("options")] public int[] OriginalOptions { get; set; } = Array.Empty<int> ();
		}
	}
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
public sealed class RfChannelLiveTests
	{
	[Test, Explicit ("Briefly changes and restores the hub RF channel. Requires RAINPOINT_LIVE_RF=change-and-restore. No watering.")]
	public Task ChangesAndRestoresHubChannel () => Run (false);
	[Test, Explicit ("Restores only the RF channel recorded in the private recovery journal. Requires RAINPOINT_LIVE_RF=change-and-restore.")]
	public Task RestoreJournaledChannel () => Run (true);
	private static async Task Run (bool recovery)
		{
		string? path = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS");
		if (string.IsNullOrWhiteSpace (path) || Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_RF") != "change-and-restore")
			Assert.Ignore ("Private settings and RF change-and-restore opt-in are required.");
		string journalPath = Path.GetFullPath (path!) + ".rf-channel.json";
		using RainPointCloudClient client = new ();
		using CancellationTokenSource timeout = new (TimeSpan.FromSeconds (120));
		try
			{
			Account account = JsonSerializer.Deserialize<Account> (File.ReadAllText (path!))!;
			await client.LoginAsync (account.Email, account.Password, account.AreaCode, timeout.Token);
			List<RainPointHub> hubs = [];
			foreach (var home in await client.GetHomesAsync (timeout.Token))
				hubs.AddRange ((await client.GetHubsAsync (home.Id, timeout.Token)).Where (h => h.Model is "HWG023WBRF" or "HWG023WBRF-V2"));
			Assert.That (hubs, Has.Count.EqualTo (1));
			RainPointHub hub = hubs[0];
			if (recovery)
				{
				Assert.That (File.Exists (journalPath), Is.True);
				await Restore (client, hub, journalPath);
				return;
				}
			Assert.That (File.Exists (journalPath), Is.False, "Reconcile the existing RF journal first.");
			Assert.That (hub.RfChannel, Is.InRange (1, 3));
			Assert.That (hub.Devices, Has.Count.EqualTo (1), "Do not change channel with other paired devices present.");
			var timer = hub.Devices.Single ();
			Assert.That (timer.Model, Is.EqualTo ("HTV345FRF"));
			var status = await client.GetHubStatusAsync (hub, timeout.Token);
			Assert.That (status.IsConnected, Is.True);
			Assert.That (status.Timers.Single ().Zones, Has.Count.EqualTo (3));
			Assert.That (status.Timers.Single ().Zones.All (z => z.IsOpen == false), Is.True, "Every zone must report idle.");
			string? parameter = null;
			for (int zone = 1; zone <= 3; zone++)
				{
				var plans = await client.GetTimerSchedulesAsync (hub, timer.Address, zone, timeout.Token);
				Assert.That (plans.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
				Assert.That (plans.Schedules, Is.Empty, "No saved schedules may be present during this channel test.");
				parameter = plans.Parameter;
				}
			Journal journal = new ()
				{
				Home = hub.HomeId,
				Hub = hub.Id,
				Name = hub.DeviceName,
				Key = hub.ProductKey,
				Original = hub.RfChannel!.Value,
				Target = hub.RfChannel == 1 ? 2 : 1
				};
			using (var file = new FileStream (journalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				JsonSerializer.Serialize (file, journal);
			try
				{
				await client.SetRfChannelAsync (hub, journal.Target, timeout.Token);
				await WaitForChannel (client, hub, journal.Target, timeout.Token);
				TestContext.Progress.WriteLine ($"Cloud RF channel changed from {journal.Original} to {journal.Target}. No valve command sent.");
				}
			finally { await Restore (client, hub, journalPath); }
			var restored = await client.GetTimerSchedulesAsync (hub, timer.Address, 1, timeout.Token);
			Assert.That (restored.Parameter == parameter, Is.True, "Timer configuration changed during the RF check.");
			TestContext.Progress.WriteLine ("Timer configuration unchanged. Cloud read-back alone does not confirm RF reception.");
			}
		catch (Exception e) when (e is not AssertionException && e is not IgnoreException)
			{
			Assert.Fail ("RF fixture failed (" + e.GetType ().Name + "). " + (e is RainPointException p ? "API code " + p.ApiCode : "Details omitted.") + " Retain any private recovery journal.");
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
	private static async Task<RainPointHub> Fresh (RainPointCloudClient client, RainPointHub hub, CancellationToken token)
		{
		var matches = (await client.GetHubsAsync (hub.HomeId, token)).Where (h => h.Id == hub.Id && h.Model == hub.Model && h.DeviceName == hub.DeviceName && h.ProductKey == hub.ProductKey).ToArray ();
		Assert.That (matches, Has.Length.EqualTo (1), "Hub identity changed.");
		return matches[0];
		}
	private static async Task WaitForChannel (RainPointCloudClient client, RainPointHub hub, int channel, CancellationToken token)
		{
		for (int n = 0; n < 10; n++)
			{
			if ((await Fresh (client, hub, token)).RfChannel == channel)
				return;
			await Task.Delay (TimeSpan.FromSeconds (3), token);
			}
		Assert.Fail ("RF channel did not match within the bounded read-back window.");
		}
	private static async Task Restore (RainPointCloudClient client, RainPointHub hub, string path)
		{
		using CancellationTokenSource timeout = new (TimeSpan.FromSeconds (60));
		Journal journal = JsonSerializer.Deserialize<Journal> (File.ReadAllText (path))!;
		Assert.That (hub.HomeId == journal.Home && hub.Id == journal.Hub && hub.DeviceName == journal.Name && hub.ProductKey == journal.Key, Is.True, "Recovery identity mismatch.");
		var current = await Fresh (client, hub, timeout.Token);
		if (current.RfChannel != journal.Original)
			{
			Assert.That (current.RfChannel == journal.Target, Is.True, "Channel changed outside the fixture; refusing to overwrite it.");
			await client.SetRfChannelAsync (current, journal.Original, timeout.Token);
			await WaitForChannel (client, hub, journal.Original, timeout.Token);
			}
		File.Delete (path);
		TestContext.Progress.WriteLine ($"Original cloud RF channel {journal.Original} restored; recovery journal removed.");
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
		[JsonPropertyName ("name")] public string Name { get; set; } = string.Empty;
		[JsonPropertyName ("key")] public string Key { get; set; } = string.Empty;
		[JsonPropertyName ("original")]
		public int Original
			{
			get; set;
			}
		[JsonPropertyName ("target")]
		public int Target
			{
			get; set;
			}
		}
	}
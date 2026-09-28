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

[TestFixture, NonParallelizable, Category ("Live")]
public sealed class SoilSensorLiveTests
	{
	[Test, Explicit ("Reads all three zones' sensor configuration without writes. Requires RAINPOINT_LIVE_SETTINGS.")]
	public async Task ReadsAllZonesWithoutWrites ()
		{
		string? path = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS");
		if (string.IsNullOrWhiteSpace (path))
			Assert.Ignore ("Private settings required.");
		Account account = JsonSerializer.Deserialize<Account> (File.ReadAllText (path!))!;
		using RainPointCloudClient client = new ();
		using CancellationTokenSource timeout = new (TimeSpan.FromSeconds (90));
		await client.LoginAsync (account.Email, account.Password, account.AreaCode, timeout.Token);
		List<RainPointHub> hubs = new ();
		foreach (var home in await client.GetHomesAsync (timeout.Token))
			hubs.AddRange ((await client.GetHubsAsync (home.Id, timeout.Token)).Where (h => h.Model is "HWG023WBRF" or "HWG023WBRF-V2" && h.Devices.Any (d => d.Model == "HTV345FRF")));
		Assert.That (hubs.Count, Is.EqualTo (1));
		var hub = hubs[0];
		var timer = hub.Devices.Single (d => d.Model == "HTV345FRF");
		for (int zone = 1; zone <= 3; zone++)
			{
			var snapshot = await client.GetTimerSchedulesAsync (hub, timer.Address, zone, timeout.Token);
			Assert.That (snapshot.SoilSensorAvailability, Is.EqualTo (TimerReadingAvailability.Decoded));
			var available = await client.GetAvailableSoilSensorsAsync (hub, snapshot, timeout.Token);
			TestContext.Progress.WriteLine ($"Zone {zone}: decoded sensor settings; {available.Count} compatible available sensors.");
			}
		}
	private sealed class Account
		{
		[JsonPropertyName ("email")] public string Email { get; set; } = string.Empty;
		[JsonPropertyName ("password")] public string Password { get; set; } = string.Empty;
		[JsonPropertyName ("areaCode")] public string AreaCode { get; set; } = "44";
		}
	}
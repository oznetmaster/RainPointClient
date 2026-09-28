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
public sealed class AdministrationLiveTests
	{
	[Test, Explicit ("Reads home, room, member and invitation metadata without any write. Requires RAINPOINT_LIVE_SETTINGS.")]
	public async Task ReadsHomeMembersAndInvitationsWithoutWrites ()
		{
		string? path = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS");
		if (string.IsNullOrWhiteSpace (path))
			Assert.Ignore ("Private settings required.");
		Account account = JsonSerializer.Deserialize<Account> (File.ReadAllText (path!))!;
		using RainPointCloudClient client = new ();
		using CancellationTokenSource timeout = new (TimeSpan.FromSeconds (90));
		await client.LoginAsync (account.Email, account.Password, account.AreaCode, timeout.Token);
		Assert.That (client.AccountProfile, Is.Not.Null, "Sign-in should expose its nonsensitive profile observation.");
		TestContext.Progress.WriteLine ($"Profile decoded; email present: {!string.IsNullOrWhiteSpace (client.AccountProfile!.Email)}; nickname present: {!string.IsNullOrWhiteSpace (client.AccountProfile.Nickname)}.");
		var options = await client.GetHomeOptionsAsync (timeout.Token);
		Assert.That (options.Currencies, Is.Not.Empty);
		Assert.That (options.WeatherTypes, Is.Not.Empty);
		var timeZones = await client.GetTimeZonesAsync (timeout.Token);
		Assert.That (timeZones.Any (z => z.Name == "Europe/London"), Is.True);
		var homes = await client.GetHomesAsync (timeout.Token);
		Assert.That (homes, Is.Not.Empty);
		foreach (var entry in homes)
			{
			var home = await client.GetHomeAsync (entry.Id, timeout.Token);
			Assert.That (home.Id, Is.EqualTo (entry.Id));
			var until = DateTimeOffset.UtcNow;
			var history = await client.GetSceneHistoryAsync (entry.Id, until.AddDays (-7), until, cancellationToken: timeout.Token);
			TestContext.Progress.WriteLine ($"Scene history decoded: {history.Entries.Count} entries on first page; more available: {history.HasMore}.");
			var scenes = await client.GetScenesAsync (entry.Id, timeout.Token);
			foreach (var summary in scenes)
				{
				var scene = await client.GetSceneAsync (entry.Id, summary.Id, timeout.Token);
				Assert.That (scene.Id, Is.EqualTo (summary.Id));
				}
			var hubs = await client.GetSceneDevicesAsync (entry.Id, timeout.Token);
			TestContext.Progress.WriteLine ($"Scenes: {scenes.Count}; scene-capable hubs: {hubs.Count (h => h.SupportsSceneExecution == true)}; scene-capable timers: {hubs.SelectMany (h => h.Devices).Count (d => d.SupportsSceneActions == true)}.");
			foreach (var hub in hubs)
				foreach (var timer in hub.Devices.Where (d => d.Model == "HTV345FRF"))
					TestContext.Progress.WriteLine ($"Timer scene metadata: model {timer.ModelCode}; catalog flags {timer.AdvertisedSceneFlags?.ToString () ?? "missing"}; runtime function present: {!string.IsNullOrWhiteSpace (timer.SceneFunctionParameter)}; supported actions: {timer.SupportsSceneActions?.ToString () ?? "unknown"}.");
			var members = await client.GetMembersAsync (entry.Id, timeout.Token);
			TestContext.Progress.WriteLine ($"Home administration decoded: {home.Rooms.Count} rooms, {members.Count} members; units decoded: {home.DisplayUnits is not null}.");
			}
		var invitations = await client.GetInvitationsAsync (timeout.Token);
		TestContext.Progress.WriteLine ($"Pending invitations: {invitations.Count}; notification sign-in observation available: {client.NotificationPreferences is not null}.");

		}
	private sealed class Account
		{
		[JsonPropertyName ("email")] public string Email { get; set; } = string.Empty;
		[JsonPropertyName ("password")] public string Password { get; set; } = string.Empty;
		[JsonPropertyName ("areaCode")] public string AreaCode { get; set; } = "44";
		}
	}
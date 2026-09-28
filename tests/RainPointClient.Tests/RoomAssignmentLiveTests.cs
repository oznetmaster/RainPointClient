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
public sealed class RoomAssignmentLiveTests
	{
	[Test, Explicit ("Creates a temporary room and verifies cloud-only room assignments for all three existing timer zones, then removes it. Existing room assignments must not reference this timer. Requires RAINPOINT_LIVE_ROOMS=all-zones.")]
	public Task AllZoneAssignmentsRoundTrip () => Run (false);

	[Test, Explicit ("Removes only the exact temporary room recorded by this fixture. Never changes the timer or existing rooms.")]
	public Task ReconcileTemporaryRoom () => Run (true);

	private static async Task Run (bool reconcileOnly)
		{
		if (Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_ROOMS") != "all-zones")
			Assert.Ignore ("Explicit cloud room-assignment opt-in required.");
		string? settings = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS");
		if (string.IsNullOrWhiteSpace (settings))
			Assert.Ignore ("Private owner-account settings required.");
		string path = Path.GetFullPath (settings!) + ".zone-rooms.json";
		Assert.That (File.Exists (path), Is.EqualTo (reconcileOnly), "Reconcile the previous room fixture first.");
		Account account = JsonSerializer.Deserialize<Account> (File.ReadAllText (settings!))!;
		using RainPointCloudClient client = new ();
		using CancellationTokenSource timeout = new (TimeSpan.FromMinutes (4));
		Journal? journal = null;
		try
			{
			await client.LoginAsync (account.Email, account.Password, account.AreaCode, timeout.Token);
			if (reconcileOnly)
				{
				journal = JsonSerializer.Deserialize<Journal> (File.ReadAllText (path))!;
				return;
				}
			List<RainPointHub> matches = [];
			foreach (RainPointHome item in await client.GetHomesAsync (timeout.Token))
				matches.AddRange ((await client.GetHubsAsync (item.Id, timeout.Token)).Where (h => h.Model is "HWG023WBRF" or "HWG023WBRF-V2"
					&& h.Devices.Any (d => d.Model == "HTV345FRF")));
			Assert.That (matches, Has.Count.EqualTo (1));
			RainPointHub hub = matches.Single ();
			RainPointDevice timer = hub.Devices.Single (d => d.Model == "HTV345FRF");
			Assert.That (timer.Id.HasValue && timer.SupportedZoneCount == 3, Is.True);
			RainPointHomeDetails home = await client.GetHomeAsync (hub.HomeId, timeout.Token);
			Assert.That (home.IsOwner, Is.True, "Require the owner account for a reversible room test.");
			foreach (RainPointRoom room in home.Rooms)
				{
				var assignments = await client.GetRoomDevicesAsync (home, room.Id, timeout.Token);
				Assert.That (assignments.Any (a => a.HubId == hub.Id && (!a.DeviceId.HasValue || a.DeviceId == timer.Id)), Is.False,
					"Do not move an existing assignment; this fixture requires an unassigned timer and hub.");
				}
			Journal pending = new ()
				{
				HomeId = home.Id,
				OwnerId = client.AccountProfile?.Id ?? throw new AssertionException ("Account identity unavailable."),
				HubId = hub.Id,
				DeviceId = timer.Id!.Value,
				RoomName = "RPzones-" + Guid.NewGuid ().ToString ("N").Substring (0, 8),
				OriginalRooms = home.Rooms.Select (r => new OriginalRoom { Id = r.Id, Name = r.Name, Devices = r.Wire.Devices }).ToArray ()
				};
			using (FileStream file = new (path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
				JsonSerializer.Serialize (file, pending);
				file.Flush (true);
				}
			journal = pending;
			await client.CreateRoomAsync (home, journal.RoomName, timeout.Token);
			home = await ReadUntil (client, home.Id, h => h.Rooms.Count (r => r.Name == journal.RoomName) == 1, timeout.Token);
			long roomId = home.Rooms.Single (r => r.Name == journal.RoomName).Id;
			journal.CreatedRoomId = roomId;
			SaveJournal (path, journal);
			VerifyOriginalRooms (home, journal);
			foreach (int[] zones in new[] { new[] { 1 }, new[] { 2 }, new[] { 3 }, new[] { 1, 2, 3 }, Array.Empty<int> () })
				{
				RainPointRoomDevice[] assignments = zones.Select (z => new RainPointRoomDevice (hub.Id, timer.Id, z)).ToArray ();
				await client.SetRoomDevicesAsync (home, roomId, assignments, timeout.Token);
				string expected = string.Join (",", assignments.Select (a => a.Encode ()));
				home = await ReadUntil (client, home.Id, h => (h.Rooms.Single (r => r.Id == roomId).Wire.Devices ?? "") == expected, timeout.Token);
				var observed = await client.GetRoomDevicesAsync (home, roomId, timeout.Token);
				Assert.That (observed.Count == zones.Length && observed.All (a => a.HubId == hub.Id && a.DeviceId == timer.Id), Is.True);
				Assert.That (observed.Select (a => a.Zone), Is.EquivalentTo (zones.Select (z => (int?)z)));
				VerifyOriginalRooms (home, journal);
				TestContext.Progress.WriteLine ($"Room assignment read-back passed for {zones.Length} selected zone(s): {string.Join (",", zones)}. Cloud metadata only.");
				}
			}
		catch (Exception error) when (error is not AssertionException && error is not MultipleAssertException && error is not IgnoreException)
			{
			Assert.Fail ("Room assignment check failed (" + error.GetType ().Name + "). Private identifiers omitted; cleanup still runs.");
			}
		finally
			{
			try
				{
				if (journal is not null)
					await Cleanup (client, journal, path);
				}
			catch (Exception error) when (error is not AssertionException && error is not MultipleAssertException && error is not OutOfMemoryException)
				{
				Assert.Fail ("Room cleanup failed (" + error.GetType ().Name + "). Retain the recovery journal; private details omitted.");
				}
			finally
				{
				using CancellationTokenSource logout = new (TimeSpan.FromSeconds (15));
				try
					{
					if (client.HasValidSession)
						await client.LogoutAsync (logout.Token);
					}
				catch (Exception error) when (error is not OutOfMemoryException) { TestContext.Progress.WriteLine ("Remote logout unavailable; client disposed."); }
				}
			}
		}

	private static async Task Cleanup (RainPointCloudClient client, Journal journal, string path)
		{
		using CancellationTokenSource timeout = new (TimeSpan.FromSeconds (90));
		Assert.That (client.AccountProfile?.Id == journal.OwnerId, Is.True, "Recovery requires the same owner.");
		RainPointHomeDetails home = await client.GetHomeAsync (journal.HomeId, timeout.Token);
		if (!journal.CreatedRoomId.HasValue && home.Rooms.All (r => r.Name != journal.RoomName))
			home = await ReadUntil (client, journal.HomeId, h => h.Rooms.Any (r => r.Name == journal.RoomName), timeout.Token);
		Assert.That (home.IsOwner, Is.True);
		VerifyOriginalRooms (home, journal);
		RainPointRoom[] matches = home.Rooms.Where (r => r.Name == journal.RoomName).ToArray ();
		if (matches.Length == 0 && journal.CreatedRoomId.HasValue)
			{
			Assert.That (home.Rooms.All (r => r.Id != journal.CreatedRoomId.Value) && home.Rooms.Count == journal.OriginalRooms.Length, Is.True);
			File.Delete (path);
			TestContext.Progress.WriteLine ("Previously created temporary room confirmed absent; original room metadata unchanged; recovery journal removed.");
			return;
			}
		Assert.That (matches, Has.Length.EqualTo (1));
		RainPointRoom room = matches.Single ();
		Assert.That (!journal.CreatedRoomId.HasValue || journal.CreatedRoomId.Value == room.Id, Is.True);
		Assert.That (journal.OriginalRooms.All (r => r.Id != room.Id), Is.True, "Never delete a pre-existing room.");
		var assignments = await client.GetRoomDevicesAsync (home, room.Id, timeout.Token);
		Assert.That (assignments.All (a => a.HubId == journal.HubId && a.DeviceId == journal.DeviceId && a.Zone is >= 1 and <= 3), Is.True,
			"Unexpected room assignments; retain journal rather than delete unrelated metadata.");
		if (assignments.Count > 0)
			{
			await client.SetRoomDevicesAsync (home, room.Id, Array.Empty<RainPointRoomDevice> (), timeout.Token);
			home = await ReadUntil (client, home.Id, h => string.IsNullOrEmpty (h.Rooms.Single (r => r.Id == room.Id).Wire.Devices), timeout.Token);
			}
		await client.DeleteRoomAsync (home, room.Id, timeout.Token);
		home = await ReadUntil (client, home.Id, h => h.Rooms.All (r => r.Id != room.Id), timeout.Token);
		VerifyOriginalRooms (home, journal);
		Assert.That (home.Rooms.Count == journal.OriginalRooms.Length, Is.True);
		File.Delete (path);
		TestContext.Progress.WriteLine ("Temporary room removed; all original rooms and their assignments unchanged; recovery journal removed. No device configuration or valve commands.");
		}

	private static void SaveJournal (string path, Journal journal)
		{
		string temporary = path + "." + Guid.NewGuid ().ToString ("N") + ".tmp";
		using (FileStream file = new (temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
			JsonSerializer.Serialize (file, journal);
			file.Flush (true);
			}
		File.Replace (temporary, path, null);
		}

	private static void VerifyOriginalRooms (RainPointHomeDetails home, Journal journal)
		{
		Assert.That (journal.OriginalRooms.All (old => home.Rooms.Any (r => r.Id == old.Id && r.Name == old.Name && r.Wire.Devices == old.Devices)), Is.True,
			"Original room metadata changed; do not overwrite it.");
		Assert.That (home.Rooms.All (r => journal.OriginalRooms.Any (old => old.Id == r.Id) || r.Name == journal.RoomName), Is.True,
			"Unexpected new room; inspect before further changes.");
		}

	private static async Task<RainPointHomeDetails> ReadUntil (RainPointCloudClient client, long id, Func<RainPointHomeDetails, bool> predicate, CancellationToken token)
		{
		for (int attempt = 0; attempt < 12; attempt++)
			{
			RainPointHomeDetails home = await client.GetHomeAsync (id, token);
			if (predicate (home))
				return home;
			await Task.Delay (TimeSpan.FromSeconds (2), token);
			}
		throw new AssertionException ("Expected room metadata was not observed; retain any recovery journal.");
		}

	private sealed class Account
		{
		[JsonPropertyName ("email")] public string Email { get; set; } = string.Empty;
		[JsonPropertyName ("password")] public string Password { get; set; } = string.Empty;
		[JsonPropertyName ("areaCode")] public string AreaCode { get; set; } = "44";
		}
	private sealed class Journal
		{
		[JsonPropertyName ("homeId")]
		public long HomeId
			{
			get; set;
			}
		[JsonPropertyName ("ownerId")]
		public long OwnerId
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
		[JsonPropertyName ("createdRoomId")]
		public long? CreatedRoomId
			{
			get; set;
			}
		[JsonPropertyName ("roomName")] public string RoomName { get; set; } = string.Empty;
		[JsonPropertyName ("originalRooms")] public OriginalRoom[] OriginalRooms { get; set; } = [];
		}
	private sealed class OriginalRoom
		{
		[JsonPropertyName ("id")]
		public long Id
			{
			get; set;
			}
		[JsonPropertyName ("name")] public string Name { get; set; } = string.Empty;
		[JsonPropertyName ("devices")]
		public string? Devices
			{
			get; set;
			}
		}
	}
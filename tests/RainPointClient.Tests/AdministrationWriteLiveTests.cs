// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture, NonParallelizable, Category ("Live")]
public sealed class AdministrationWriteLiveTests
	{
	[Test, Explicit ("Creates, edits and deletes only an isolated empty test home. Requires RAINPOINT_LIVE_ADMIN=temporary-empty-home.")]
	public Task TemporaryEmptyHomeAndRoomRoundTrip () => Run (false);

	[Test, Explicit ("Reconciles only the uniquely named empty home recorded by this fixture; never retries creation or edits.")]
	public Task ReconcileTemporaryEmptyHome () => Run (true);

	[Test, Explicit ("Invites the authorized support test account to an isolated empty home, accepts, changes roles, removes it and deletes the home. No existing home is changed.")]
	public Task TemporaryHomeMembershipRoundTrip () => Run (false, true);

	private static async Task Run (bool cleanupOnly, bool membership = false)
		{
		if (Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_ADMIN") != "temporary-empty-home")
			Assert.Ignore ("Explicit temporary-empty-home opt-in required.");
		string? path = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS");
		if (string.IsNullOrWhiteSpace (path))
			Assert.Ignore ("Ignored private account settings required.");
		string journalPath = Path.GetFullPath (path!) + ".temporary-home.json";
		Account account = JsonSerializer.Deserialize<Account> (File.ReadAllText (path!))!;
		using SHA256 hash = SHA256.Create ();
		string accountFingerprint = Convert.ToBase64String (hash.ComputeHash (Encoding.UTF8.GetBytes (account.Email.Trim ().ToUpperInvariant () + "|" + account.AreaCode)));
		using RainPointCloudClient client = new ();
		using RainPointCloudClient guest = new ();
		using CancellationTokenSource timeout = new (TimeSpan.FromMinutes (6));
		Journal? journal = null;
		try
			{
			await client.LoginAsync (account.Email, account.Password, account.AreaCode, timeout.Token);
			if (cleanupOnly)
				{
				Assert.That (File.Exists (journalPath), Is.True, "No recovery journal exists.");
				journal = JsonSerializer.Deserialize<Journal> (File.ReadAllText (journalPath))!;
				return;
				}
			Assert.That (File.Exists (journalPath), Is.False, "Reconcile the previous fixture before creating another home.");
			var existing = await client.GetHomesAsync (timeout.Token);
			string suffix = Guid.NewGuid ().ToString ("N").Substring (0, 12);
			Journal pending = new ()
				{
				AccountFingerprint = accountFingerprint,
				OriginalName = "RPtest-" + suffix,
				UpdatedName = "RPedit-" + suffix,
				ExistingHomeIds = existing.Select (h => h.Id).ToArray ()
				};
			using (FileStream file = new (journalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				JsonSerializer.Serialize (file, pending);
			journal = pending;
			RainPointHomeDetails home = await client.CreateHomeAsync (journal.OriginalName, "Europe/London", ["Initial room"], timeout.Token);
			Assert.That (journal.ExistingHomeIds, Does.Not.Contain (home.Id));
			journal.CreatedHomeId = home.Id;
			SaveJournal (journalPath, journal);
			Assert.That (home.Name, Is.EqualTo (journal.OriginalName));
			Assert.That (home.Rooms.Select (r => r.Name), Is.EquivalentTo (new[] { "Initial room" }));
			await EnsureIsolated (client, home, timeout.Token);
			if (membership)
				{
				string guestPath = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SECONDARY_SETTINGS") ?? throw new AssertionException ("Secondary settings required.");
				Account secondary = JsonSerializer.Deserialize<Account> (File.ReadAllText (guestPath))!;
				Assert.That (secondary.Email.Equals ("support@marvelous.com", StringComparison.OrdinalIgnoreCase), Is.True, "Only the authorized test mailbox may be invited.");
				await Task.Delay (TimeSpan.FromMinutes (2), timeout.Token);
				await guest.LoginAsync (secondary.Email, secondary.Password, secondary.AreaCode, timeout.Token);
				journal.GuestId = guest.AccountProfile?.Id ?? throw new AssertionException ("Guest identity unavailable.");
				journal.GuestOriginalHomes = (await guest.GetHomesAsync (timeout.Token)).Select (h => h.Id).ToArray ();
				Assert.That (journal.GuestId != client.AccountProfile?.Id, Is.True);
				SaveJournal (journalPath, journal);
				await client.InviteMemberAsync (home, secondary.Email, timeout.Token);
				RainPointInvitation? invitation = null;
				for (int i = 0; i < 30 && invitation is null; i++)
					{
					invitation = (await guest.GetInvitationsAsync (timeout.Token)).SingleOrDefault (x => x.HomeId == home.Id && x.HomeName == journal.OriginalName);
					if (invitation is null)
						await Task.Delay (2000, timeout.Token);
					}
				Assert.That (invitation, Is.Not.Null, "The isolated-home invitation was not delivered to the test account.");
				await guest.RespondToInvitationAsync (invitation!, true, timeout.Token);
				RainPointMember member = await WaitForGuest (client, home.Id, journal.GuestId.Value, null, timeout.Token);
				Assert.That (member.IsOwner, Is.False);
				foreach (RainPointMemberRole role in new[] { RainPointMemberRole.Administrator, RainPointMemberRole.Member })
					{
					home = await client.GetHomeAsync (home.Id, timeout.Token);
					await client.SetMemberRoleAsync (home, member, role, timeout.Token);
					member = await WaitForGuest (client, home.Id, journal.GuestId.Value, role, timeout.Token);
					}
				await RemoveFixtureGuest (client, home.Id, journal, journalPath, timeout.Token);
				var guestHomes = await guest.GetHomesAsync (timeout.Token);
				Assert.That (guestHomes.Select (h => h.Id), Is.EquivalentTo (journal.GuestOriginalHomes));
				TestContext.Progress.WriteLine ("Temporary-home invitation accepted; administrator/member roles verified; test member removed; original guest homes unchanged.");
				return;
				}

			await client.RenameHomeAsync (home, journal.UpdatedName, timeout.Token);
			home = await ReadUntil (client, home.Id, h => h.Name == journal.UpdatedName, timeout.Token);
			await client.CreateRoomAsync (home, "Additional room", timeout.Token);
			home = await ReadUntil (client, home.Id, h => h.Rooms.Count == 2 && h.Rooms.Any (r => r.Name == "Additional room"), timeout.Token);
			long roomId = home.Rooms.Single (r => r.Name == "Additional room").Id;
			await client.RenameRoomAsync (home, roomId, "Updated room", timeout.Token);
			home = await ReadUntil (client, home.Id, h => h.Rooms.Any (r => r.Id == roomId && r.Name == "Updated room"), timeout.Token);
			await client.DeleteRoomAsync (home, roomId, timeout.Token);
			home = await ReadUntil (client, home.Id, h => h.Rooms.Count == 1 && h.Rooms[0].Name == "Initial room", timeout.Token);
			RainPointDisplayUnits original = home.DisplayUnits ?? throw new AssertionException ("Test-home display units were unavailable.");
			var changed = new RainPointDisplayUnits
				{
				TwelveHourClock = !original.TwelveHourClock,
				Fahrenheit = !original.Fahrenheit,
				ImperialLength = !original.ImperialLength,
				ImperialVolume = !original.ImperialVolume,
				Pressure = RainPointPressureUnit.InchesOfMercury,
				DateFormat = original.DateFormat.HasValue ? original.DateFormat == RainPointDateFormat.DayDashMonthYear ? RainPointDateFormat.YearDashMonthDay : RainPointDateFormat.DayDashMonthYear : null
				};
			await client.SetHomeDisplayUnitsAsync (home, changed, timeout.Token);
			home = await ReadUntil (client, home.Id, h => SameUnits (h.DisplayUnits, changed), timeout.Token);
			await client.SetHomeDisplayUnitsAsync (home, original, timeout.Token);
			home = await ReadUntil (client, home.Id, h => SameUnits (h.DisplayUnits, original), timeout.Token);
			Assert.That (home.TimeZoneName, Is.EqualTo ("Europe/London"));
			await client.SetHomeTimeZoneAsync (home, "Europe/Paris", timeout.Token);
			home = await ReadUntil (client, home.Id, h => h.TimeZoneName == "Europe/Paris", timeout.Token);
			await client.SetHomeTimeZoneAsync (home, "Europe/London", timeout.Token);
			home = await ReadUntil (client, home.Id, h => h.TimeZoneName == "Europe/London", timeout.Token);
			RainPointHomeOptions options = await client.GetHomeOptionsAsync (timeout.Token);
			RainPointCurrency? originalCurrency = options.Currencies.FirstOrDefault (c => c.Code == home.CurrencyCode);
			RainPointCurrency replacement = options.Currencies.First (c => c.Code != home.CurrencyCode);
			await client.SetHomeCurrencyAsync (home, replacement, timeout.Token);
			home = await ReadUntil (client, home.Id, h => h.CurrencyCode == replacement.Code, timeout.Token);
			if (originalCurrency is not null)
				{
				await client.SetHomeCurrencyAsync (home, originalCurrency, timeout.Token);
				home = await ReadUntil (client, home.Id, h => h.CurrencyCode == originalCurrency.Code, timeout.Token);
				}
			// Public fixture coordinates belong only to the empty temporary home, never to the owner's real home.
			await client.SetHomeLocationAsync (home, 51.5m, -0.1m, timeout.Token);
			home = await ReadUntil (client, home.Id, h => h.Latitude == 51.5m && h.Longitude == -0.1m, timeout.Token);
			Assert.That (home.Rooms.Select (r => r.Name), Is.EquivalentTo (new[] { "Initial room" }));
			TestContext.Progress.WriteLine ($"Temporary home/room changes, units and time-zone restoration, currency and fixture-location read-back passed. Date-format change checked: {original.DateFormat.HasValue}. Existing homes and hardware were not changed.");
			}
		catch (Exception error) when (error is not AssertionException && error is not SuccessException && error is not IgnoreException)
			{
			Assert.Fail ("Temporary-home check failed (" + error.GetType ().Name + "). "
				 + (error is RainPointException protocol ? protocol.Message : "Private details omitted."));
			}
		finally
			{
			try
				{
				if (journal is not null)
					{
					await Cleanup (client, journal, journalPath, accountFingerprint);
					if (guest.HasValidSession)
						{
						using CancellationTokenSource verify = new (TimeSpan.FromSeconds (30));
						Assert.That ((await guest.GetInvitationsAsync (verify.Token)).Any (i => i.HomeId == journal.CreatedHomeId), Is.False, "Temporary-home invitation must be absent after cleanup.");
						Assert.That ((await guest.GetHomesAsync (verify.Token)).Select (h => h.Id), Is.EquivalentTo (journal.GuestOriginalHomes));
						}
					}
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
					catch (Exception error) when (error is not OutOfMemoryException) { TestContext.Progress.WriteLine ("Remote logout did not complete; client disposed."); }
					}
				}
			}
		}


	private static async Task<RainPointMember> WaitForGuest (RainPointCloudClient client, long homeId, long guestId, RainPointMemberRole? role, CancellationToken token)
		{
		for (int i = 0; i < 20; i++)
			{
			var members = await client.GetMembersAsync (homeId, token);
			Assert.That (members.All (m => m.IsOwner == true || m.Id == guestId), Is.True, "Unexpected member; manual reconciliation required.");
			RainPointMember? match = members.SingleOrDefault (m => m.Id == guestId && (!role.HasValue || m.Role == role));
			if (match is not null)
				return match;
			await Task.Delay (2000, token);
			}
		throw new AssertionException ("Expected fixture-member state was not observed.");
		}
	private static async Task RemoveFixtureGuest (RainPointCloudClient client, long homeId, Journal journal, string journalPath, CancellationToken token)
		{
		var members = await client.GetMembersAsync (homeId, token);
		Assert.That (members.All (m => m.IsOwner == true || m.Id == journal.GuestId), Is.True, "Unexpected member; do not change this home.");
		RainPointMember? guest = members.SingleOrDefault (m => m.Id == journal.GuestId);
		if (guest is not null)
			{
			Assert.That (guest.IsOwner, Is.False);
			Assert.That (journal.RemovalAttempted, Is.False, "Do not replay an uncertain membership removal; retain the journal.");
			journal.RemovalAttempted = true;
			SaveJournal (journalPath, journal);
			await client.RemoveMemberAsync (await client.GetHomeAsync (homeId, token), guest, token);
			}
		for (int i = 0; i < 20; i++)
			{
			members = await client.GetMembersAsync (homeId, token);
			if (members.Count == 1 && members[0].IsOwner == true)
				return;
			await Task.Delay (2000, token);
			}
		throw new AssertionException ("Fixture member removal was not confirmed.");
		}

	private static bool SameUnits (RainPointDisplayUnits? actual, RainPointDisplayUnits expected) => actual is not null
		 && actual.TwelveHourClock == expected.TwelveHourClock && actual.Fahrenheit == expected.Fahrenheit
		 && actual.ImperialLength == expected.ImperialLength && actual.ImperialVolume == expected.ImperialVolume
		 && actual.Pressure == expected.Pressure && actual.DateFormat == expected.DateFormat;

	private static async Task<RainPointHomeDetails> ReadUntil (RainPointCloudClient client, long id, Func<RainPointHomeDetails, bool> predicate, CancellationToken token)
		{
		for (int attempt = 0; attempt < 12; attempt++)
			{
			RainPointHomeDetails home = await client.GetHomeAsync (id, token);
			if (predicate (home))
				return home;
			await Task.Delay (TimeSpan.FromSeconds (2), token);
			}
		throw new AssertionException ("Temporary-home change was not observed within the bounded read-back period.");
		}

	private static async Task EnsureIsolated (RainPointCloudClient client, RainPointHomeDetails home, CancellationToken token)
		{
		Assert.That (home.IsOwner, Is.True, "The test account must own the temporary home.");
		Assert.That (await client.GetHubsAsync (home.Id, token), Is.Empty, "Do not delete a home containing hardware.");
		var members = await client.GetMembersAsync (home.Id, token);
		Assert.That (members.Count, Is.EqualTo (1), "Do not delete a shared home.");
		Assert.That (members[0].IsOwner, Is.True);
		Assert.That (await client.GetScenesAsync (home.Id, token), Is.Empty, "Do not delete a home containing automations.");
		Assert.That (home.Rooms.All (r => r.Name is "Initial room" or "Additional room" or "Updated room"), Is.True, "Unexpected room data requires manual reconciliation.");
		}

	private static void SaveJournal (string path, Journal journal)
		{
		string temporary = path + "." + Guid.NewGuid ().ToString ("N") + ".tmp";
		using (FileStream file = new (temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			JsonSerializer.Serialize (file, journal);
		File.Replace (temporary, path, null);
		}

	private static async Task Cleanup (RainPointCloudClient client, Journal journal, string journalPath, string accountFingerprint)
		{
		using CancellationTokenSource cleanup = new (TimeSpan.FromSeconds (90));
		try
			{
			Assert.That (journal.AccountFingerprint == accountFingerprint, Is.True, "Recovery requires the same account.");
			Assert.That (journal.OriginalName.StartsWith ("RPtest-", StringComparison.Ordinal) && journal.OriginalName.Length == 19, Is.True);
			Assert.That (journal.UpdatedName, Is.EqualTo ("RPedit-" + journal.OriginalName.Substring (7)));
			var homes = await client.GetHomesAsync (cleanup.Token);
			var matches = homes.Where (h => h.Name == journal.OriginalName || h.Name == journal.UpdatedName).ToArray ();
			if (!journal.CreatedHomeId.HasValue && matches.Length == 0)
				{
				// An uncertain creation can become visible after the request fails. Reads are retried, creation never is.
				for (int attempt = 0; attempt < 12 && matches.Length == 0; attempt++)
					{
					await Task.Delay (TimeSpan.FromSeconds (3), cleanup.Token);
					homes = await client.GetHomesAsync (cleanup.Token);
					matches = homes.Where (h => h.Name == journal.OriginalName || h.Name == journal.UpdatedName).ToArray ();
					}
				Assert.That (matches.Length, Is.EqualTo (1), "Creation remains uncertain; retain the journal for later reconciliation.");
				}
			Assert.That (matches.Length, Is.LessThanOrEqualTo (1), "Ambiguous temporary-home identity; journal retained.");
			if (journal.CreatedHomeId.HasValue)
				Assert.That (homes.All (h => h.Id != journal.CreatedHomeId.Value || h.Name == journal.OriginalName || h.Name == journal.UpdatedName), Is.True, "Temporary home was renamed outside this fixture; manual reconciliation required.");
			if (matches.Length == 1)
				{
				long id = matches[0].Id;
				Assert.That (!journal.CreatedHomeId.HasValue || journal.CreatedHomeId.Value == id, Is.True, "Temporary-home identity changed.");
				Assert.That (journal.ExistingHomeIds, Does.Not.Contain (id), "Never delete a pre-existing home.");
				RainPointHomeDetails home = await client.GetHomeAsync (id, cleanup.Token);
				if (journal.GuestId.HasValue)
					{
					Assert.That (home.IsOwner, Is.True);
					Assert.That (await client.GetHubsAsync (id, cleanup.Token), Is.Empty);
					Assert.That (await client.GetScenesAsync (id, cleanup.Token), Is.Empty);
					await RemoveFixtureGuest (client, id, journal, journalPath, cleanup.Token);
					home = await client.GetHomeAsync (id, cleanup.Token);
					}
				await EnsureIsolated (client, home, cleanup.Token);
				await client.DeleteHomeAsync (home, cleanup.Token);
				for (int attempt = 0; attempt < 12; attempt++)
					{
					homes = await client.GetHomesAsync (cleanup.Token);
					if (homes.All (h => h.Id != id))
						break;
					await Task.Delay (TimeSpan.FromSeconds (2), cleanup.Token);
					}
				Assert.That (homes.Any (h => h.Id == id), Is.False, "Temporary home still present; journal retained.");
				}
			Assert.That (journal.ExistingHomeIds.All (id => homes.Any (h => h.Id == id)), Is.True, "A pre-existing home is missing; journal retained for reconciliation.");
			File.Delete (journalPath);
			TestContext.Progress.WriteLine ("Temporary home absent, original home identities retained, recovery journal removed.");
			}
		catch (Exception error) when (error is not OutOfMemoryException)
			{
			throw new AssertionException ("Temporary-home cleanup was not confirmed (" + error.GetType ().Name + "). Private recovery journal retained; use the explicit reconciliation fixture.");
			}
		}

	private sealed class Journal
		{
		[JsonPropertyName ("removalAttempted")]
		public bool RemovalAttempted
			{
			get; set;
			}
		[JsonPropertyName ("guestId")]
		public long? GuestId
			{
			get; set;
			}
		[JsonPropertyName ("guestOriginalHomes")] public long[] GuestOriginalHomes { get; set; } = [];
		[JsonPropertyName ("accountFingerprint")] public string AccountFingerprint { get; set; } = string.Empty;
		[JsonPropertyName ("createdHomeId")]
		public long? CreatedHomeId
			{
			get; set;
			}
		[JsonPropertyName ("originalName")] public string OriginalName { get; set; } = string.Empty;
		[JsonPropertyName ("updatedName")] public string UpdatedName { get; set; } = string.Empty;
		[JsonPropertyName ("existingHomeIds")] public long[] ExistingHomeIds { get; set; } = [];
		}
	private sealed class Account
		{
		[JsonPropertyName ("email")] public string Email { get; set; } = string.Empty;
		[JsonPropertyName ("password")] public string Password { get; set; } = string.Empty;
		[JsonPropertyName ("areaCode")] public string AreaCode { get; set; } = "44";
		}
	}
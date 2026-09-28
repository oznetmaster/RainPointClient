// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture, NonParallelizable, Category ("Live"), Category ("Configuration")]
public sealed class AccountProfileLiveTests
	{
	[Test, Explicit ("Temporarily changes only the account nickname, verifies it after sign-in, restores it and verifies restoration. Requires RAINPOINT_LIVE_PROFILE=nickname-roundtrip.")]
	public Task NicknameRoundTripPreservesOtherProfileFields () => Run (false);

	[Test, Explicit ("Restores only this fixture's exact journaled nickname. Requires RAINPOINT_LIVE_PROFILE=nickname-roundtrip.")]
	public Task ReconcileNickname () => Run (true);

	private static async Task Run (bool reconcileOnly)
		{
		if (Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_PROFILE") != "nickname-roundtrip")
			Assert.Ignore ("Explicit nickname-only opt-in required.");
		string? settings = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS");
		if (string.IsNullOrWhiteSpace (settings))
			Assert.Ignore ("Private account settings required.");
		string path = Path.GetFullPath (settings!) + ".nickname.json";
		Assert.That (File.Exists (path), Is.EqualTo (reconcileOnly), "Reconcile an existing journal before starting another test.");
		Account account = JsonSerializer.Deserialize<Account> (File.ReadAllText (settings!))!;
		using RainPointCloudClient client = new ();
		using CancellationTokenSource timeout = new (TimeSpan.FromMinutes (9));
		DateTimeOffset lastLogin = DateTimeOffset.MinValue;
		Journal? journal = null;
		async Task SignIn (CancellationToken token)
			{
			await Logout (client);
			TimeSpan wait = lastLogin.AddMinutes (2) - DateTimeOffset.UtcNow;
			if (wait > TimeSpan.Zero)
				{
				TestContext.Progress.WriteLine ("Spacing profile verification sign-ins by two minutes.");
				await Task.Delay (wait, token);
				}
			lastLogin = DateTimeOffset.UtcNow;
			await client.LoginAsync (account.Email, account.Password, account.AreaCode, token);
			}
		try
			{
			await SignIn (timeout.Token);
			RainPointAccountProfile original = client.AccountProfile ?? throw new AssertionException ("Sign-in profile was not reported.");
			if (reconcileOnly)
				{
				journal = JsonSerializer.Deserialize<Journal> (File.ReadAllText (path))!;
				return;
				}
			Assert.That (original.Id.HasValue && !string.IsNullOrWhiteSpace (original.Nickname), Is.True, "Require an identifiable profile and a restorable nickname.");
			Journal pending = new ()
				{
				Id = original.Id!.Value,
				Email = original.Email,
				Original = original.Nickname!,
				Photo = original.Photo,
				Language = original.Language,
				Temporary = "RPtest-" + Guid.NewGuid ().ToString ("N").Substring (0, 8)
				};
			using (FileStream file = new (path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
				JsonSerializer.Serialize (file, pending);
				file.Flush (true);
				}
			journal = pending;
			await client.SetAccountNicknameAsync (original, journal.Temporary, timeout.Token);
			Assert.That (client.AccountProfile, Is.Null, "Attempted profile observations must be invalidated.");
			await SignIn (timeout.Token);
			RainPointAccountProfile changed = RequireMatchingProfile (client, journal);
			Assert.That (changed.Nickname == journal.Temporary, Is.True, "Temporary nickname did not survive a fresh sign-in; private values omitted.");
			TestContext.Progress.WriteLine ("Temporary nickname verified after fresh sign-in; identity, email, photo and language unchanged.");
			}
		catch (Exception error) when (error is not AssertionException && error is not MultipleAssertException && error is not IgnoreException)
			{
			Assert.Fail ("Nickname check failed (" + error.GetType ().Name + "). Private details omitted; restoration still runs.");
			}
		finally
			{
			try
				{
				if (journal is not null)
					{
					using CancellationTokenSource cleanup = new (TimeSpan.FromMinutes (6));
					if (client.AccountProfile is null)
						await SignIn (cleanup.Token);
					RainPointAccountProfile current = RequireMatchingProfile (client, journal);
					Assert.That (current.Nickname == journal.Original || current.Nickname == journal.Temporary, Is.True,
						"Nickname changed externally; retain journal and do not overwrite.");
					if (current.Nickname == journal.Temporary)
						{
						await client.SetAccountNicknameAsync (current, journal.Original, cleanup.Token);
						await SignIn (cleanup.Token);
						current = RequireMatchingProfile (client, journal);
						}
					Assert.That (current.Nickname == journal.Original, Is.True, "Original nickname not confirmed; retain recovery journal.");
					File.Delete (path);
					TestContext.Progress.WriteLine ("Original nickname and all observed profile fields verified; private recovery journal removed. No password, email, device or watering changes.");
					}
				}
			catch (Exception error) when (error is not AssertionException && error is not MultipleAssertException && error is not OutOfMemoryException)
				{
				Assert.Fail ("Nickname restoration failed (" + error.GetType ().Name + "). Retain the private journal and reconcile; no write replay.");
				}
			finally { await Logout (client); }
			}
		}

	private static RainPointAccountProfile RequireMatchingProfile (RainPointCloudClient client, Journal journal)
		{
		RainPointAccountProfile profile = client.AccountProfile ?? throw new AssertionException ("Fresh sign-in profile unavailable.");
		Assert.That (profile.Id == journal.Id && profile.Email == journal.Email, Is.True, "Account identity must match the journal; private values omitted.");
		Assert.That (profile.Photo == journal.Photo && profile.Language == journal.Language, Is.True, "Unrelated profile fields changed; private values omitted.");
		return profile;
		}

	private static async Task Logout (RainPointCloudClient client)
		{
		if (!client.HasValidSession)
			return;
		using CancellationTokenSource timeout = new (TimeSpan.FromSeconds (15));
		try
			{
			await client.LogoutAsync (timeout.Token);
			}
		catch (Exception error) when (error is not OutOfMemoryException) { TestContext.Progress.WriteLine ("Remote logout unavailable; local client will be disposed."); }
		}

	private sealed class Account
		{
		[JsonPropertyName ("email")] public string Email { get; set; } = string.Empty;
		[JsonPropertyName ("password")] public string Password { get; set; } = string.Empty;
		[JsonPropertyName ("areaCode")] public string AreaCode { get; set; } = "44";
		}

	private sealed class Journal
		{
		[JsonPropertyName ("id")]
		public long Id
			{
			get; set;
			}
		[JsonPropertyName ("email")]
		public string? Email
			{
			get; set;
			}
		[JsonPropertyName ("original")] public string Original { get; set; } = string.Empty;
		[JsonPropertyName ("temporary")] public string Temporary { get; set; } = string.Empty;
		[JsonPropertyName ("photo")]
		public string? Photo
			{
			get; set;
			}
		[JsonPropertyName ("language")]
		public string? Language
			{
			get; set;
			}
		}
	}
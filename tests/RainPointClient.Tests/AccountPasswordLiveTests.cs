using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture, NonParallelizable, Category ("Live"), Category ("Configuration")]
public sealed class AccountPasswordLiveTests
	{
	[Test, Explicit ("Temporarily changes only the dedicated support test account password, verifies new sign-in, restores the original and verifies it. Private journal required; no device writes.")]
	public Task TestAccountPasswordRoundTrip () => Run (false);
	[Test, Explicit ("Reconciles the exact dedicated test-account password journal; never repeats a restoration attempt with an uncertain outcome.")]
	public Task ReconcileTestAccountPassword () => Run (true);
	private static async Task Run (bool reconcile)
		{
		if (Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_PASSWORD") != "support-account-roundtrip")
			Assert.Ignore ("Dedicated test-account opt-in required.");
		string settings = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS") ?? throw new AssertionException ("Private settings required.");
		Account account = JsonSerializer.Deserialize<Account> (File.ReadAllText (settings))!;
		Assert.That (account.Email.Equals ("support@marvelous.com", StringComparison.OrdinalIgnoreCase), Is.True, "Only the dedicated development account may be changed.");
		string path = Path.GetFullPath (settings) + ".password-test.json";
		Assert.That (File.Exists (path), Is.EqualTo (reconcile), "Reconcile an existing journal first.");
		using RainPointCloudClient client = new ();
		DateTimeOffset lastLogin = DateTimeOffset.MinValue;
		bool temporaryVerified = false;
		Journal? journal = reconcile ? JsonSerializer.Deserialize<Journal> (File.ReadAllText (path)) : null;
		async Task SignIn (string password, CancellationToken token)
			{
			TimeSpan wait = lastLogin.AddMinutes (2) - DateTimeOffset.UtcNow;
			if (wait > TimeSpan.Zero)
				await Task.Delay (wait, token);
			lastLogin = DateTimeOffset.UtcNow;
			await client.LoginAsync (account.Email, password, account.AreaCode, token);
			}
		void Save ()
			{
			string temporary = path + ".tmp";
			using (FileStream file = new (temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
				JsonSerializer.Serialize (file, journal);
				file.Flush (true);
				}
			if (File.Exists (path))
				File.Replace (temporary, path, null);
			else
				File.Move (temporary, path);
			}
		try
			{
			if (reconcile)
				return;
			using CancellationTokenSource start = new (TimeSpan.FromMinutes (3));
			await SignIn (account.Password, start.Token);
			long id = client.AccountProfile?.Id ?? throw new AssertionException ("Account identity unavailable.");
			byte[] random = new byte[12];
			using (RandomNumberGenerator rng = RandomNumberGenerator.Create ())
				rng.GetBytes (random);
			journal = new ()
				{
				Id = id,
				Original = account.Password,
				Temporary = "Rp7!" + Convert.ToBase64String (random)
				};
			Save ();
			await client.ChangePasswordAsync (journal.Original, journal.Temporary, start.Token);
			Assert.That (client.HasValidSession, Is.False, "The submitted password change must invalidate the local session.");
			await SignIn (journal.Temporary, start.Token);
			Assert.That (client.AccountProfile?.Id == journal.Id, Is.True);
			temporaryVerified = true;
			TestContext.Progress.WriteLine ("Temporary test-account password verified by fresh sign-in; restoring the original next.");
			}
		catch (Exception error) when (error is not IgnoreException)
			{
			Assert.Fail ("Password round trip failed (" + error.GetType ().Name + "). Private details omitted; reconciliation still runs.");
			}
		finally
			{
			try
				{
				if (journal is not null)
					{
					using CancellationTokenSource restore = new (TimeSpan.FromMinutes (7));
					Assert.That (journal.Original == account.Password && journal.Id > 0, Is.True, "Settings must match the private journal.");
					bool originalVerified = false;
					if (!temporaryVerified && client.HasValidSession)
						await client.LogoutAsync (restore.Token);
					if (!client.HasValidSession)
						{
						if (!journal.RestorationAttempted)
							{
							try
								{
								await SignIn (journal.Temporary, restore.Token);
								}
							catch (Exception error) when (error is not OutOfMemoryException) { /* Reconcile by one original-password sign-in, never repeat the change. */ }
							}
						if (!client.HasValidSession)
							{
							await SignIn (journal.Original, restore.Token);
							originalVerified = true;
							}
						}
					Assert.That (client.AccountProfile?.Id == journal.Id, Is.True, "Journal account identity must match before restoration.");
					if (!originalVerified)
						{
						Assert.That (journal.RestorationAttempted, Is.False, "Do not replay uncertain restoration.");
						journal.RestorationAttempted = true;
						Save ();
						await client.ChangePasswordAsync (journal.Temporary, journal.Original, restore.Token);
						await SignIn (journal.Original, restore.Token);
						Assert.That (client.AccountProfile?.Id == journal.Id, Is.True);
						}
					File.Delete (path);
					TestContext.Progress.WriteLine ("Original test-account password verified by fresh sign-in; private recovery journal removed. No device or owner-account changes.");
					}
				}
			catch (Exception error) when (error is not OutOfMemoryException)
				{
				Assert.Fail ("Password restoration is not confirmed (" + error.GetType ().Name + "). Private journal retained for explicit reconciliation; no secret values printed.");
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
					catch (Exception error) when (error is not OutOfMemoryException) { TestContext.Progress.WriteLine ("Remote logout unavailable; client disposed."); }
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
	private sealed class Journal
		{
		[JsonPropertyName ("id")]
		public long Id
			{
			get; set;
			}
		[JsonPropertyName ("original")] public string Original { get; set; } = string.Empty;
		[JsonPropertyName ("temporary")] public string Temporary { get; set; } = string.Empty;
		[JsonPropertyName ("restorationAttempted")]
		public bool RestorationAttempted
			{
			get; set;
			}
		}
	}
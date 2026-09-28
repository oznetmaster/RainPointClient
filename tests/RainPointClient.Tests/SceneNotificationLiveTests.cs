using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture, NonParallelizable, Category ("Live"), Category ("Notification")]
public sealed class SceneNotificationLiveTests
	{
	private const string Message = "RainPoint client notification test. No watering command was sent.";
	[Test, Explicit ("Creates/replaces/switches a temporary scene, sends one approved test email, reads execution history, then deletes the scene. Requires RAINPOINT_LIVE_SCENE=one-approved-email and RAINPOINT_LIVE_NOTIFICATION_TO. Obtain explicit recipient approval before running.")]
	public Task OneApprovedNotificationSceneRoundTrip () => Run (false);

	[Test, Explicit ("Deletes only the exact journaled notification scene; never creates/enables or sends a message.")]
	public Task ReconcileNotificationScene () => Run (true);

	[Test, Explicit ("Sends exactly two separately approved test emails two minutes apart to establish scene-history pagination, then deletes the temporary scene. Requires RAINPOINT_LIVE_SCENE=two-approved-emails and recipient approval.")]
	public Task TwoApprovedNotificationsVerifyPagination () => Run (false, true);

	private static async Task Run (bool reconcileOnly, bool pagination = false)
		{
		string? optIn = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SCENE");
		if (reconcileOnly ? optIn is not ("one-approved-email" or "two-approved-emails") : optIn != (pagination ? "two-approved-emails" : "one-approved-email"))
			Assert.Ignore ("Explicit approved-email opt-in required.");
		string? settings = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS");
		if (string.IsNullOrWhiteSpace (settings))
			Assert.Ignore ("Private owner settings required.");
		string path = Path.GetFullPath (settings!) + ".notification-scene.json";
		Assert.That (File.Exists (path), Is.EqualTo (reconcileOnly), "Reconcile any existing scene journal first.");
		Account account = JsonSerializer.Deserialize<Account> (File.ReadAllText (settings!))!;
		using HttpClient http = new (new HistoryCapture (path + ".history-evidence") { InnerHandler = new HttpClientHandler { AllowAutoRedirect = false } })
			{
			Timeout = TimeSpan.FromSeconds (30),
			MaxResponseContentBufferSize = 4 * 1024 * 1024
			};
		using RainPointCloudClient client = new (http);
		using CancellationTokenSource timeout = new (TimeSpan.FromMinutes (15));
		Journal? journal = null;
		try
			{
			await client.LoginAsync (account.Email, account.Password, account.AreaCode, timeout.Token);
			if (reconcileOnly)
				{
				journal = JsonSerializer.Deserialize<Journal> (File.ReadAllText (path))!;
				return;
				}
			string recipient = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_NOTIFICATION_TO") ?? "";
			RainPointSceneAction notification = RainPointSceneAction.Notify (Message, Array.Empty<long> (), new[] { recipient });
			List<RainPointHub> matches = [];
			foreach (RainPointHome item in await client.GetHomesAsync (timeout.Token))
				matches.AddRange ((await client.GetSceneDevicesAsync (item.Id, timeout.Token)).Where (h => h.Model is "HWG023WBRF" or "HWG023WBRF-V2"
					&& h.Devices.Any (d => d.Model == "HTV345FRF")));
			Assert.That (matches, Has.Count.EqualTo (1));
			RainPointHub hub = matches.Single ();
			Assert.That (hub.SupportsSceneExecution, Is.True);
			RainPointHomeDetails home = await client.GetHomeAsync (hub.HomeId, timeout.Token);
			Assert.That (home.IsOwner == true && home.TimeZoneName == "Europe/London", Is.True);
			TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById ("GMT Standard Time");
			DateTime now = DateTime.SpecifyKind (TimeZoneInfo.ConvertTimeFromUtc (DateTime.UtcNow, zone), DateTimeKind.Unspecified);
			DateTime due = now.Date.AddHours (now.Hour).AddMinutes (now.Minute + 7);
			Assert.That (zone.IsAmbiguousTime (due) || zone.IsInvalidTime (due), Is.False);
			DateTime future = now.Date.AddDays (7).AddHours (12);
			Journal pending = new ()
				{
				HomeId = home.Id,
				HubId = hub.Id,
				OwnerId = client.AccountProfile?.Id ?? throw new AssertionException ("Owner identity unavailable."),
				Name = "RPnotify-" + Guid.NewGuid ().ToString ("N").Substring (0, 8),
				Recipient = recipient,
				NotificationCount = pagination ? 2 : 1,
				Future = future,
				Due = due,
				ExistingIds = (await client.GetScenesAsync (home.Id, timeout.Token)).Select (s => s.Id).ToArray ()
				};
			using (FileStream file = new (path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
				JsonSerializer.Serialize (file, pending);
				file.Flush (true);
				}
			journal = pending;
			RainPointSceneDraft draft = new ()
				{
				Name = journal.Name,
				MaximumRunsPerDay = journal.NotificationCount,
				MinimumIntervalMinutes = pagination ? 1 : 120,
				Conditions = new[] { RainPointSceneCondition.Once (future) },
				Actions = new[] { notification }
				};
			await client.CreateSceneAsync (hub, draft, timeout.Token);
			RainPointScene scene = await Find (client, journal, timeout.Token);
			Verify (scene, journal);
			journal.CreatedSceneId = scene.Id;
			SaveJournal (path, journal);
			Assert.That (scene.Conditions.Single ().AtLocal == future, Is.True);
			await client.SetSceneEnabledAsync (scene, false, timeout.Token);
			scene = await WaitFor (client, journal, s => s.Enabled == false, timeout.Token);
			RainPointSceneDraft replacement = scene.CreateDraft ();
			replacement.Name = journal.Name + "-edit";
			replacement.Conditions = new[] { RainPointSceneCondition.Once (future.AddDays (1)) };
			await client.ReplaceSceneAsync (scene, hub, replacement, timeout.Token);
			scene = await WaitFor (client, journal, s => s.Name == replacement.Name && s.Conditions.Single ().AtLocal == future.AddDays (1), timeout.Token);
			Verify (scene, journal);
			await client.SetSceneEnabledAsync (scene, true, timeout.Token);
			scene = await WaitFor (client, journal, s => s.Enabled == true, timeout.Token);
			await client.SetSceneEnabledAsync (scene, false, timeout.Token);
			scene = await WaitFor (client, journal, s => s.Enabled == false, timeout.Token);
			TestContext.Progress.WriteLine ("Populated scene create/read/replace and both switch states verified with future-only conditions.");
			DateTimeOffset dueUtc = new (TimeZoneInfo.ConvertTimeToUtc (due, zone));
			Assert.That (dueUtc - DateTimeOffset.UtcNow > TimeSpan.FromMinutes (2), Is.True, "Too late to arm the approved notification; cleanup without execution.");
			replacement = scene.CreateDraft ();
			replacement.Conditions = pagination ? new[] { RainPointSceneCondition.Once (due), RainPointSceneCondition.Once (due.AddMinutes (2)) } : new[] { RainPointSceneCondition.Once (due) };
			replacement.MatchAll = !pagination;
			await client.ReplaceSceneAsync (scene, hub, replacement, timeout.Token);
			scene = await WaitFor (client, journal, s => s.Conditions.Count == journal.NotificationCount && s.Conditions.Any (c => c.AtLocal == due), timeout.Token);
			Verify (scene, journal);
			if (scene.Enabled != true)
				{
				await client.SetSceneEnabledAsync (scene, true, timeout.Token);
				scene = await WaitFor (client, journal, s => s.Enabled == true, timeout.Token);
				}
			TestContext.Progress.WriteLine ($"{journal.NotificationCount} approved notification(s), first at {dueUtc:O}, second two minutes later if requested. No device action exists in this scene.");
			RainPointSceneLogEntry? result = null;
			RainPointSceneLogPage? observedHistory = null;
			while (DateTimeOffset.UtcNow < dueUtc.AddMinutes (pagination ? 4 : 2) && (result is null || pagination && observedHistory?.Entries.Count < 2))
				{
				await Task.Delay (TimeSpan.FromSeconds (10), timeout.Token);
				if (DateTimeOffset.UtcNow < dueUtc)
					continue;
				var page = await client.GetSceneHistoryAsync (home.Id, dueUtc.AddMinutes (-1), DateTimeOffset.UtcNow, sceneId: scene.Id, cancellationToken: timeout.Token);
				observedHistory = page;
				result = page.Entries.FirstOrDefault (entry => entry.SceneId == scene.Id && entry.TriggeredAt >= dueUtc.AddSeconds (-5));
				}
			Assert.That (result, Is.Not.Null, "No execution history was reported; delivery is not established.");
			Assert.That (result!.Actions is { Count: 1 } && result.Actions[0].Outcome == RainPointSceneActionOutcome.Succeeded, Is.True,
				"The cloud did not report successful execution of the single notification action.");
			if (pagination)
				{
				Assert.That (observedHistory!.Entries, Has.Count.EqualTo (2), "Require both approved executions before pagination checks.");
				Assert.That (observedHistory.Entries.All (e => e.Actions is { Count: 1 } && e.Actions[0].Outcome == RainPointSceneActionOutcome.Succeeded), Is.True);
				DateTimeOffset end = DateTimeOffset.UtcNow;
				List<RainPointSceneLogPage> pages = [];
				for (int page = 0; page <= 3; page++)
					pages.Add (await client.GetSceneHistoryAsync (home.Id, dueUtc.AddMinutes (-1), end, page, 1, scene.Id, timeout.Token));
				ReportPageMapping (pages);
				Assert.That (pages[0].Entries.Count == 1 && pages[1].Entries.Count == 1 && pages[0].Entries[0].Id != pages[1].Entries[0].Id, Is.True, "Zero-based public pages must return distinct records. Private captures retain the server mapping if this fails.");
				Assert.That (pages[2].Entries, Is.Empty);
				Assert.That (pages[2].HasMore, Is.False);
				TestContext.Progress.WriteLine ("Two successful notification executions traversed as distinct single-record pages, followed by an empty page.");
				}
			else
				TestContext.Progress.WriteLine ("Populated execution history reports one successful notification action. Actual inbox delivery requires independent confirmation.");
			}
		catch (Exception error) when (error is not AssertionException && error is not MultipleAssertException && error is not IgnoreException)
			{
			Assert.Fail ("Notification scene check failed (" + error.GetType ().Name + "). " + (error is RainPointException protocol ? protocol.Message : "Private details omitted.") + " Cleanup still runs.");
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
				Assert.Fail ("Scene cleanup failed (" + error.GetType ().Name + "). Retain the private journal and reconcile.");
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

	private static void ReportPageMapping (IReadOnlyList<RainPointSceneLogPage> pages)
		{
		for (int i = 0; i < pages.Count; i++)
			TestContext.Progress.WriteLine ($"Public page {i}: count={pages[i].Entries.Count}; total={pages[i].Total}; hasMore={pages[i].HasMore}.");
		if (pages[0].Entries.Count == 1)
			for (int i = 1; i < pages.Count; i++)
				TestContext.Progress.WriteLine ($"Page {i} repeats first record={pages[i].Entries.Any (entry => entry.Id == pages[0].Entries[0].Id)}.");
		}

	private static async Task<RainPointScene> Find (RainPointCloudClient client, Journal journal, CancellationToken token)
		{
		for (int i = 0; i < 12; i++)
			{
			var list = await client.GetScenesAsync (journal.HomeId, token);
			var matches = list.Where (s => s.Name == journal.Name || s.Name == journal.Name + "-edit").ToArray ();
			Assert.That (matches.Length <= 1, Is.True, "Ambiguous scene identity; no write.");
			if (matches.Length == 1)
				{
				Assert.That (!journal.ExistingIds.Contains (matches[0].Id), Is.True, "Never modify a pre-existing scene.");
				return await client.GetSceneAsync (journal.HomeId, matches[0].Id, token);
				}
			await Task.Delay (TimeSpan.FromSeconds (2), token);
			}
		throw new AssertionException ("Temporary scene not found; creation remains uncertain. Retain journal.");
		}
	private static async Task<RainPointScene> WaitFor (RainPointCloudClient client, Journal journal, Func<RainPointScene, bool> predicate, CancellationToken token)
		{
		for (int i = 0; i < 12; i++)
			{
			RainPointScene scene = await Find (client, journal, token);
			Verify (scene, journal);
			if (predicate (scene))
				return scene;
			await Task.Delay (TimeSpan.FromSeconds (2), token);
			}
		throw new AssertionException ("Expected scene state not reported; no replay.");
		}
	private static void Verify (RainPointScene scene, Journal journal)
		{
		Assert.That (scene.HomeId == journal.HomeId && scene.ExecutingHubId == journal.HubId && !journal.ExistingIds.Contains (scene.Id), Is.True);
		Assert.That (!journal.CreatedSceneId.HasValue || journal.CreatedSceneId.Value == scene.Id, Is.True);
		Assert.That (scene.Actions.Count == 1 && scene.Actions[0].Kind == RainPointSceneActionKind.Notification
			&& scene.Actions[0].Message == Message && scene.Actions[0].Recipients.SequenceEqual (new[] { journal.Recipient }), Is.True,
			"Only the exact approved notification is permitted; no device actions.");
		Assert.That (scene.Conditions.Count >= 1 && scene.Conditions.Count <= journal.NotificationCount
			&& scene.Conditions.All (c => c.Kind == RainPointSceneConditionKind.Once
			&& new[] { journal.Future, journal.Future.AddDays (1), journal.Due, journal.NotificationCount == 2 ? journal.Due.AddMinutes (2) : journal.Due }.Contains (c.AtLocal ?? DateTime.MinValue)), Is.True);
		}
	private static async Task Cleanup (RainPointCloudClient client, Journal journal, string path)
		{
		using CancellationTokenSource token = new (TimeSpan.FromSeconds (90));
		Assert.That (client.AccountProfile?.Id == journal.OwnerId, Is.True);
		var existing = await client.GetScenesAsync (journal.HomeId, token.Token);
		if (journal.CreatedSceneId.HasValue && existing.All (s => s.Id != journal.CreatedSceneId.Value))
			{
			Assert.That (existing.All (s => s.Name != journal.Name && s.Name != journal.Name + "-edit")
				&& journal.ExistingIds.All (id => existing.Any (s => s.Id == id)), Is.True);
			File.Delete (path);
			TestContext.Progress.WriteLine ("Previously created test scene confirmed absent; original scene identities retained; recovery journal removed.");
			return;
			}
		RainPointScene scene = await Find (client, journal, token.Token);
		Verify (scene, journal);
		if (scene.Enabled != false)
			{
			await client.SetSceneEnabledAsync (scene, false, token.Token);
			scene = await WaitFor (client, journal, s => s.Enabled == false, token.Token);
			}
		await client.DeleteSceneAsync (scene, token.Token);
		for (int i = 0; i < 12; i++)
			{
			var list = await client.GetScenesAsync (journal.HomeId, token.Token);
			if (list.All (s => s.Id != scene.Id))
				{
				Assert.That (journal.ExistingIds.All (id => list.Any (s => s.Id == id)), Is.True);
				File.Delete (path);
				TestContext.Progress.WriteLine ("Temporary notification scene disabled/deleted; original scene identities retained; recovery journal removed.");
				return;
				}
			await Task.Delay (TimeSpan.FromSeconds (2), token.Token);
			}
		throw new AssertionException ("Temporary scene still present; retain journal.");
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
	// Explicit local diagnostics contain private identifiers and must never be published.
	// Only history responses are captured: never login responses, credentials or headers.
	private sealed class HistoryCapture (string directory) : DelegatingHandler
		{
		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken token)
			{
			HttpResponseMessage response = await base.SendAsync (request, token);
			if (request.RequestUri?.AbsolutePath == "/app/scene/2.0.5/logPage")
				{
				string body = await response.Content.ReadAsStringAsync ();
				Directory.CreateDirectory (directory);
				string file = Path.Combine (directory, DateTime.UtcNow.ToString ("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid ().ToString ("N") + ".json");
				using FileStream stream = new (file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
				JsonSerializer.Serialize (stream, new Capture { CapturedAt = DateTimeOffset.UtcNow, Status = (int)response.StatusCode, Body = body, Query = request.RequestUri.Query });
				stream.Flush (true);
				}
			return response;
			}
		}
	private sealed class Capture
		{
		[JsonPropertyName ("capturedAt")]
		public DateTimeOffset CapturedAt
			{
			get; set;
			}
		[JsonPropertyName ("status")]
		public int Status
			{
			get; set;
			}
		[JsonPropertyName ("body")] public string Body { get; set; } = string.Empty;
		[JsonPropertyName ("query")] public string Query { get; set; } = string.Empty;
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
		[JsonPropertyName ("hubId")]
		public long HubId
			{
			get; set;
			}
		[JsonPropertyName ("ownerId")]
		public long OwnerId
			{
			get; set;
			}
		[JsonPropertyName ("createdSceneId")]
		public long? CreatedSceneId
			{
			get; set;
			}
		[JsonPropertyName ("name")] public string Name { get; set; } = string.Empty;
		[JsonPropertyName ("recipient")] public string Recipient { get; set; } = string.Empty;
		[JsonPropertyName ("notificationCount")] public int NotificationCount { get; set; } = 1;
		[JsonPropertyName ("future")]
		public DateTime Future
			{
			get; set;
			}
		[JsonPropertyName ("due")]
		public DateTime Due
			{
			get; set;
			}
		[JsonPropertyName ("existingIds")] public long[] ExistingIds { get; set; } = [];
		}
	}
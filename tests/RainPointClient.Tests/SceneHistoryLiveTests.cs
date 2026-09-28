// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture, NonParallelizable, Category ("Live")]
public sealed class SceneHistoryLiveTests
	{
	[Test, Explicit ("Reads history of the exact recorded notification test scene. Empty history is inconclusive. Requires RAINPOINT_LIVE_HISTORY_BOOKMARK and private RAINPOINT_LIVE_SETTINGS. No writes or messages.")]
	public async Task ReadsRecordedNotificationSceneHistory ()
		{
		string? settings = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS");
		string? bookmark = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_HISTORY_BOOKMARK");
		if (string.IsNullOrWhiteSpace (settings) || string.IsNullOrWhiteSpace (bookmark))
			Assert.Ignore ("Private account settings and a recorded notification-scene bookmark required.");
		Account account = JsonSerializer.Deserialize<Account> (File.ReadAllText (settings!))!;
		Bookmark expected = JsonSerializer.Deserialize<Bookmark> (File.ReadAllText (bookmark!))!;
		Assert.That (expected.HomeId > 0 && expected.SceneId > 0 && expected.DueUtc.Offset == TimeSpan.Zero, Is.True);
		Assert.That (expected.DueUtc < DateTimeOffset.UtcNow && expected.DueUtc > DateTimeOffset.UtcNow.AddDays (-7), Is.True);
		using RainPointCloudClient client = new ();
		using CancellationTokenSource timeout = new (TimeSpan.FromSeconds (120));
		try
			{
			await client.LoginAsync (account.Email, account.Password, account.AreaCode, timeout.Token);
			Assert.That ((await client.GetHomesAsync (timeout.Token)).Any (h => h.Id == expected.HomeId), Is.True, "Account must still belong to the recorded home.");
			bool scenePresent = (await client.GetScenesAsync (expected.HomeId, timeout.Token)).Any (s => s.Id == expected.SceneId);
			TestContext.Progress.WriteLine ($"Recorded notification scene still present: {scenePresent}.");
			var history = await client.GetSceneHistoryAsync (expected.HomeId, expected.DueUtc.AddMinutes (-1), expected.DueUtc.AddMinutes (3),
				pageSize: 10, sceneId: expected.SceneId, cancellationToken: timeout.Token);
			if (history.Entries.Count == 0)
				{
				var wide = await client.GetSceneHistoryAsync (expected.HomeId, expected.DueUtc.AddDays (-1), DateTimeOffset.UtcNow,
					pageSize: 100, sceneId: expected.SceneId, cancellationToken: timeout.Token);
				var unfiltered = await client.GetSceneHistoryAsync (expected.HomeId, expected.DueUtc.AddDays (-1), DateTimeOffset.UtcNow,
					pageSize: 100, cancellationToken: timeout.Token);
				TestContext.Progress.WriteLine ($"Read-only history diagnostics: narrow total={history.Total}, records={history.Entries.Count}; 24-hour scene total={wide.Total}, records={wide.Entries.Count}; home total={unfiltered.Total}, records={unfiltered.Entries.Count}, matching deleted scene={unfiltered.Entries.Count (e => e.SceneId == expected.SceneId)}.");
				}
			if (history.Entries.Count == 0)
				Assert.Inconclusive ("No history remains available for this scene; populated history and pagination cannot be validated from an empty response.");
			Assert.That (history.Entries.Count, Is.EqualTo (1), "Expected exactly one execution for the approved one-email test.");
			RainPointSceneLogEntry entry = history.Entries.Single ();
			Assert.That (entry.HomeId == expected.HomeId && entry.SceneId == expected.SceneId, Is.True);
			Assert.That ((entry.TriggeredAt - expected.DueUtc).TotalSeconds, Is.InRange (-5, 120));
			Assert.That (entry.Actions is { Count: 1 } && entry.Actions[0].Outcome == RainPointSceneActionOutcome.Succeeded, Is.True,
				"The recorded execution must report one successful notification action.");
			var beyond = await client.GetSceneHistoryAsync (expected.HomeId, expected.DueUtc.AddMinutes (-1), expected.DueUtc.AddMinutes (3),
				page: 1, pageSize: 10, sceneId: expected.SceneId, cancellationToken: timeout.Token);
			Assert.That (beyond.Entries, Is.Empty);
			Assert.That (beyond.HasMore, Is.False);
			TestContext.Progress.WriteLine ("Recorded scene has one typed successful notification execution; the next history page is empty. Read-only; no second email was sent.");
			}
		catch (Exception error) when (error is not AssertionException && error is not MultipleAssertException && error is not IgnoreException && error is not InconclusiveException)
			{
			Assert.Fail ("Populated history check failed (" + error.GetType ().Name + "). " + (error is RainPointException protocol ? protocol.Message : "Private details omitted."));
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
	[Test, Explicit ("Replays one private captured notification-history response through the real client using ScriptedHandler. Requires RAINPOINT_LIVE_HISTORY_CAPTURE. No network, credentials, scene or email.")]
	public async Task ReplaysCapturedNotificationHistoryWithoutNetwork ()
		{
		string? path = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_HISTORY_CAPTURE");
		if (string.IsNullOrWhiteSpace (path))
			Assert.Ignore ("Private history capture required.");
		HistoryCapture capture = JsonSerializer.Deserialize<HistoryCapture> (File.ReadAllText (path!))!;
		Assert.That (capture.Status, Is.EqualTo (200));
		var envelope = JsonSerializer.Deserialize<Protocol.ApiResult<SceneLogPageWire>> (capture.Body,
			new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowReadingFromString })!;
		Assert.That (envelope.Code, Is.EqualTo (0));
		SceneLogWire record = envelope.Data!.Records!.Single ();
		using ScriptedHandler handler = new ();
		using HttpClient http = new (handler, false);
		using RainPointCloudClient client = new (http);
		handler.Reply ("""{"code":0,"data":{"token":"offline-fixture","tokenExpired":3600}}""");
		await client.LoginAsync ("fixture@example.invalid", "fixture", "44");
		handler.Reply (capture.Body);
		DateTimeOffset trigger = DateTimeOffset.FromUnixTimeMilliseconds (record.TriggeredAt);
		RainPointSceneLogPage page = await client.GetSceneHistoryAsync (record.HomeId, trigger.AddMinutes (-1), trigger.AddMinutes (1), sceneId: record.SceneId);
		Assert.That (page.Entries.Count == 1 && page.Entries[0].SceneId == record.SceneId && page.Entries[0].TriggeredAt == trigger, Is.True);
		RainPointSceneActionResult result = page.Entries.Single ().Actions!.Single ();
		Assert.That (result.DeviceAddress, Is.Null);
		Assert.That (result.Outcome, Is.EqualTo (RainPointSceneActionOutcome.Succeeded));
		Assert.That (handler.Requests.Count, Is.EqualTo (2), "Only the scripted login and captured-history request are allowed.");
		TestContext.Progress.WriteLine ("The captured successful notification history decodes through the client with an unknown device address. No network or email; private identifiers omitted.");
		}
	private sealed class HistoryCapture
		{
		[JsonPropertyName ("status")]
		public int Status
			{
			get; set;
			}
		[JsonPropertyName ("body")] public string Body { get; set; } = string.Empty;
		}

	private sealed class Account
		{
		[JsonPropertyName ("email")] public string Email { get; set; } = string.Empty;
		[JsonPropertyName ("password")] public string Password { get; set; } = string.Empty;
		[JsonPropertyName ("areaCode")] public string AreaCode { get; set; } = "44";
		}
	private sealed class Bookmark
		{
		[JsonPropertyName ("homeId")]
		public long HomeId
			{
			get; set;
			}
		[JsonPropertyName ("sceneId")]
		public long SceneId
			{
			get; set;
			}
		[JsonPropertyName ("dueUtc")]
		public DateTimeOffset DueUtc
			{
			get; set;
			}
		}
	}
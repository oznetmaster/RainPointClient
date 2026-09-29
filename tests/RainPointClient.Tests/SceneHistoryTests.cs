// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using NUnit.Framework;
namespace RainPointClient.Tests;

[TestFixture]
public sealed class SceneHistoryTests
	{
	private ScriptedHandler _handler = null!; private HttpClient _http = null!; private RainPointCloudClient _client = null!;
	private static DateTimeOffset Start => DateTimeOffset.FromUnixTimeMilliseconds (1700000000000);
	[SetUp]
	public async Task Setup ()
		{
		_handler = new ();
		_http = new (_handler, false);
		_client = new (_http);
		_handler.Reply ("""{"code":0,"data":{"token":"fixture","tokenExpired":3600}}""");
		await _client.LoginAsync ("fixture@example.invalid", "fixture", "44");
		}
	[TearDown]
	public void Cleanup ()
		{
		_client.Dispose ();
		_http.Dispose ();
		_handler.Dispose ();
		}
	internal static string Response (string? result, long hid = 5, long scene = 12, long total = 2) => JsonSerializer.Serialize (new { code = 0, data = new { total, records = new[] { new { id = "fixture-log", hid, sceneMainId = scene, triggerTime = 1700000000000L, result } } } });
	[Test]
	public async Task TypedResultsPreserveUnknownCodesAndUseUtcMilliseconds ()
		{
		_handler.Reply (Response ("""[{"aid":91,"mid":101,"addr":2,"rt":0},{"aid":92,"mid":0,"addr":0,"rt":9876}]"""));
		var page = await _client.GetSceneHistoryAsync (5, Start, Start.AddDays (1), pageSize: 1, sceneId: 12);
		Assert.That (page.Entries.Single ().TriggeredAt, Is.EqualTo (Start));
		Assert.That (page.Entries.Single ().Actions!.Select (a => a.ResultCode), Is.EqualTo (new[] { 0, 9876 }));
		Assert.That (page.HasMore, Is.True);
		Assert.That (_handler.Requests.Last ().Path, Is.EqualTo ("/app/scene/2.0.5/logPage?appCode=2&hid=5&begin=1700000000000&end=1700086400000&pageNum=1&pageSize=1&sceneMainId=12"));
		Assert.That (_handler.Requests.Last ().Method, Is.EqualTo (HttpMethod.Get));
		}
	// Shape observed in a real successful notification result: aid/mid/rt, without addr.
	[TestCase ("{\"aid\":91,\"mid\":101,\"rt\":0}")]
	[TestCase ("{\"aid\":91,\"mid\":101,\"addr\":null,\"rt\":0}")]
	public async Task NotificationResultsPreserveAnUnreportedDeviceAddress (string action)
		{
		_handler.Reply (Response ("[" + action + "]", total: 1));
		var page = await _client.GetSceneHistoryAsync (5, Start, Start, sceneId: 12);
		RainPointSceneActionResult result = page.Entries.Single ().Actions!.Single ();
		Assert.That (result.ActionId, Is.EqualTo (91));
		Assert.That (result.HubId, Is.EqualTo (101));
		Assert.That (result.DeviceAddress, Is.Null, "Do not invent a zero address for a field omitted by the cloud.");
		Assert.That (result.Outcome, Is.EqualTo (RainPointSceneActionOutcome.Succeeded));
		Assert.That (page.HasMore, Is.False);
		}
	[Test]
	public async Task ExplicitZeroDeviceAddressRemainsReported ()
		{
		_handler.Reply (Response ("""[{"aid":91,"mid":101,"addr":0,"rt":0}]"""));
		var page = await _client.GetSceneHistoryAsync (5, Start, Start);
		Assert.That (page.Entries.Single ().Actions!.Single ().DeviceAddress, Is.EqualTo (0));
		}
	[TestCase (null)]
	[TestCase ("")]
	[TestCase (" ")]
	public async Task MissingResultsStayUnavailable (string? result)
		{
		_handler.Reply (Response (result));
		var page = await _client.GetSceneHistoryAsync (5, Start, Start);
		Assert.That (page.Entries.Single ().Actions, Is.Null);
		}
	[Test]
	public async Task SuppliedEmptyResultsAreKnownEmpty ()
		{
		_handler.Reply (Response ("[]"));
		var page = await _client.GetSceneHistoryAsync (5, Start, Start);
		Assert.That (page.Entries.Single ().Actions, Is.Empty);
		}
	[TestCase ("{private}")]
	[TestCase ("null")]
	[TestCase ("[null]")]
	[TestCase ("[{}]")]
	[TestCase ("[{\"aid\":1,\"mid\":0}]")]
	[TestCase ("[{\"aid\":1,\"mid\":-1,\"addr\":0,\"rt\":0}]")]
	public async Task MalformedResultsFailWithoutExposingTheirContents (string result)
		{
		_handler.Reply (Response (result));
		var error = await Assert.ThrowsAsync<RainPointException> (async () => await _client.GetSceneHistoryAsync (5, Start, Start));
		Assert.That (error!.Message, Does.Not.Contain (result));
		}
	[TestCase (6, 12)]
	[TestCase (5, 13)]
	public async Task WrongHomeOrFilteredSceneIsRejected (long home, long scene)
		{
		_handler.Reply (Response ("[]", home, scene));
		await Assert.ThrowsAsync<RainPointException> (async () => await _client.GetSceneHistoryAsync (5, Start, Start, sceneId: 12));
		}
	[TestCase (-1, 50)]
	[TestCase (int.MaxValue, 50)]
	[TestCase (0, 0)]
	[TestCase (0, 101)]
	public async Task InvalidPagingMakesNoRequest (int page, int size)
		{
		await Assert.ThrowsAsync<ArgumentOutOfRangeException> (async () => await _client.GetSceneHistoryAsync (5, Start, Start, page, size));
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		}
	// Live server evidence: pageNum 0 aliases 1; pageNum 2 contains the second record.
	[Test]
	public async Task ZeroBasedTraversalMapsToDistinctOneBasedServerPages ()
		{
		_handler.Reply (Response ("[]").Replace ("fixture-log", "newer-log"));
		_handler.Reply (Response ("[]").Replace ("fixture-log", "older-log"));
		_handler.Reply ("""{"code":0,"data":{"total":2,"records":[]}}""");
		var first = await _client.GetSceneHistoryAsync (5, Start, Start, 0, 1, 12);
		var second = await _client.GetSceneHistoryAsync (5, Start, Start, 1, 1, 12);
		var end = await _client.GetSceneHistoryAsync (5, Start, Start, 2, 1, 12);
		Assert.That (_handler.Requests.Skip (1).Select (request => request.Path), Is.EqualTo (new[] { 1, 2, 3 }.Select (number =>
			"/app/scene/2.0.5/logPage?appCode=2&hid=5&begin=1700000000000&end=1700000000000&pageNum=" + number + "&pageSize=1&sceneMainId=12")));
		Assert.That (new[] { first.Page, second.Page, end.Page }, Is.EqualTo (new[] { 0, 1, 2 }));
		Assert.That (first.Entries.Single ().Id, Is.Not.EqualTo (second.Entries.Single ().Id));
		Assert.That (first.HasMore, Is.True);
		Assert.That (second.HasMore || end.HasMore, Is.False);
		Assert.That (end.Entries, Is.Empty);
		}
	[Test]
	public async Task LargestPublicPageDoesNotOverflowServerIndex ()
		{
		_handler.Reply ("""{"code":0,"data":{"total":0,"records":[]}}""");
		await _client.GetSceneHistoryAsync (5, Start, Start, int.MaxValue - 1);
		Assert.That (_handler.Requests.Last ().Path, Does.Contain ("&pageNum=2147483647&"));
		}

	[Test]
	public async Task EmptyLastPageDoesNotLoop ()
		{
		_handler.Reply ("""{"code":0,"data":{"total":100,"records":[]}}""");
		var page = await _client.GetSceneHistoryAsync (5, Start, Start, page: 5);
		Assert.That (page.HasMore, Is.False);
		}

	[Test]
	public void ResultMeaningsFollowVerifiedBytecodeBranchesAndPreserveUnknownCodes ()
		{
		var meanings = new (int Code, RainPointSceneActionOutcome Outcome)[]
			 {
				(0, RainPointSceneActionOutcome.Succeeded), (1, RainPointSceneActionOutcome.Failed),
				(100, RainPointSceneActionOutcome.Invalid), (101, RainPointSceneActionOutcome.MissingDevice),
				(102, RainPointSceneActionOutcome.Unknown), (103, RainPointSceneActionOutcome.DataPointError),
				(104, RainPointSceneActionOutcome.PlanConflict), (105, RainPointSceneActionOutcome.PlanConflict), (106, RainPointSceneActionOutcome.PlanConflict),
				(107, RainPointSceneActionOutcome.LowPower), (108, RainPointSceneActionOutcome.TooFrequent),
				(200, RainPointSceneActionOutcome.Timeout), (1000, RainPointSceneActionOutcome.VendorError),
				(9999, RainPointSceneActionOutcome.VendorError), (999, RainPointSceneActionOutcome.Unknown),
				(10000, RainPointSceneActionOutcome.Unknown), (-1, RainPointSceneActionOutcome.Unknown)
			 };
		using (Assert.EnterMultipleScope ())
			foreach (var item in meanings)
				{
				var result = new RainPointSceneActionResult (new SceneActionResultWire { Result = item.Code });
				Assert.That (result.Outcome, Is.EqualTo (item.Outcome), "Code " + item.Code);
				Assert.That (result.ResultCode, Is.EqualTo (item.Code));
				}
		}
	}
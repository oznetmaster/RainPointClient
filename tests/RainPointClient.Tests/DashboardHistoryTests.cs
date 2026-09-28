using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using RainPointClient.Desktop.Core;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class DashboardHistoryTests
	{
	private ScriptedHandler _handler = null!;
	private HttpClient _http = null!;
	private Dashboard _dashboard = null!;
	private const long Stamp = 1790180653212;
	[SetUp]
	public void SetUp ()
		{
		_handler = new ();
		_http = new (_handler, false);
		_dashboard = new (new RainPointCloudClient (_http));
		}
	[TearDown]
	public async Task TearDown ()
		{
		await _dashboard.CloseAsync ();
		_http.Dispose ();
		_handler.Dispose ();
		}
	private async Task SelectAsync ()
		{
		_handler.Reply ("""{"code":0,"data":{"token":"fixture","tokenExpired":3600}}""");
		_handler.Reply ("""{"code":0,"data":[{"hid":42,"homeName":"Test home"}]}""");
		await _dashboard.ConnectAsync ("fixture@example.invalid", "fixture", "44");
		_handler.Reply ("""{"code":0,"data":[{"mid":101,"name":"Hub","model":"HWG023WBRF","deviceName":"fixture","productKey":"fixture","subDevices":[{"addr":2,"model":"HTV345FRF"}]}]}""");
		await _dashboard.SelectHomeAsync (_dashboard.Homes.Single ());
		_dashboard.SelectHub (_dashboard.Hubs.Single ());
		_dashboard.SelectTimer (_dashboard.Timers.Single ());
		_dashboard.UsageStart = "2026-09-01";
		_dashboard.UsageEnd = "2026-09-24";
		}
	private void Events (int count = 50, int start = 0, int zone = 1, int code = 1, long? sameStamp = null) => _handler.Reply (JsonSerializer.Serialize (new
		{
		code = 0,
		data = Enumerable.Range (start, count).Select (i => new
			{
			eid = "event-" + i,
			mid = 101,
			addr = 2,
			port = zone,
			code,
			timestamp = sameStamp ?? (Stamp - i * 1000L),
			time = "2026-09-23T17:23:34",
			timezone = "GMT+01:00",
			rule = new[] { new { type = "3", value = "14" }, new { type = "4", value = "61" }, new { type = "1", value = "1" }, new { type = "2", value = "2" }, new { type = "11", value = "0" }, new { type = "9", value = "fixture operator" } }
			}).ToArray ()
		}));

	[Test]
	public async Task ConstructionAndSelectionDoNotAutomaticallyReadHistory ()
		{
		await _dashboard.LoadUsageAsync ();
		await _dashboard.LoadEventsAsync ();
		await _dashboard.LoadOlderEventsAsync ();
		Assert.That (_handler.Requests, Is.Empty);
		await SelectAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (3));
		Assert.That (_dashboard.CanLoadOlderEvents, Is.False);
		}

	[TestCase ("Daily", "day", "ymd", 20260923, "2026-09-23")]
	[TestCase ("Monthly", "month", "ym", 202609, "2026-09")]
	public async Task UsageGroupingUsesCorrectUnitsDatesAndReadOnlyEndpoint (string grouping, string endpoint, string key, int value, string period)
		{
		await SelectAsync ();
		_dashboard.UsagePeriod = grouping;
		_dashboard.HistoryZone = 3;
		_handler.Reply ("{\"code\":0,\"data\":[{\"" + key + "\":" + value + ",\"val\":151}]}");
		await _dashboard.LoadUsageAsync ();
		Assert.That (_handler.Requests.Last ().Path, Is.EqualTo ("/app/iot/log/waterAmount/" + endpoint + "/list?code=0&mid=101&addr=2&port=3&startDate=20260901&endDate=20260924"));
		Assert.That (_dashboard.UsageHistory.Single ().Period, Is.EqualTo (period));
		Assert.That (_dashboard.UsageHistory.Single ().Litres, Is.EqualTo (15.1m.ToString ("0.0")));
		Assert.That (_dashboard.UsageSummary, Does.Contain ("not a guaranteed complete total"));
		Assert.That (_handler.Requests.Last ().Method, Is.EqualTo (HttpMethod.Get));
		}

	[Test]
	public async Task SparseUsageKeepsUnknownAndZeroDistinctWithoutInventingMissingDays ()
		{
		await SelectAsync ();
		_handler.Reply ("""{"code":0,"data":[{"ymd":20260924,"val":0},{"ymd":20260901}]}""");
		await _dashboard.LoadUsageAsync ();
		Assert.That (_dashboard.UsageHistory, Has.Count.EqualTo (2));
		Assert.That (_dashboard.UsageHistory[0].Litres, Is.EqualTo ("Unknown"));
		Assert.That (_dashboard.UsageHistory[1].Litres, Is.EqualTo (0m.ToString ("0.0")));
		Assert.That (_dashboard.UsageSummary, Does.Contain ("1 unknown"));
		}
	[TestCase ("[]", "No usage buckets returned")]
	[TestCase ("[{\"ymd\":20260901}]", "all amounts unknown")]
	public async Task EmptyAndUnknownUsageAreNotPresentedAsZero (string rows, string message)
		{
		await SelectAsync ();
		_handler.Reply ("{\"code\":0,\"data\":" + rows + "}");
		await _dashboard.LoadUsageAsync ();
		Assert.That (_dashboard.UsageSummary, Does.Contain (message));
		}
	[TestCase ("Daily", "invalid", "2026-09-24")]
	[TestCase ("Daily", "2026-09-25", "2026-09-24")]
	[TestCase ("Daily", "2026-08-25", "2026-09-24")]
	[TestCase ("Monthly", "2025-09-23", "2026-09-24")]
	[TestCase ("Daily", "1969-12-31", "1970-01-01")]
	public async Task InvalidUsageRangesNeverSendRequests (string grouping, string start, string end)
		{
		await SelectAsync ();
		_dashboard.UsagePeriod = grouping;
		_dashboard.UsageStart = start;
		_dashboard.UsageEnd = end;
		Assert.That (_dashboard.CanLoadUsage, Is.False);
		await _dashboard.LoadUsageAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (3));
		}
	[Test]
	public async Task UsageReadFailureClearsOldDataAndSanitizesErrors ()
		{
		await SelectAsync ();
		_handler.Reply ("""{"code":0,"data":[{"ymd":20260924,"val":14}]}""");
		await _dashboard.LoadUsageAsync ();
		_handler.Steps.Enqueue ((_, _) => throw new HttpRequestException ("secret transport detail"));
		await _dashboard.LoadUsageAsync ();
		Assert.That (_dashboard.UsageHistory, Is.Empty);
		Assert.That (_dashboard.UsageMessage, Does.Contain ("could not be read").And.Not.Contain ("secret"));
		}

	[TestCase ("All event types", 0)]
	[TestCase ("Watering", 1)]
	[TestCase ("Water usage", 2)]
	[TestCase ("Water control", 3)]
	[TestCase ("Hub status", 4)]
	[TestCase ("Power on", 5)]
	public async Task EventTypeAndUtcFiltersAreAddressedToSelectedZone (string filter, int code)
		{
		await SelectAsync ();
		_dashboard.HistoryZone = 2;
		_dashboard.EventFilter = filter;
		_dashboard.EventBegin = "2026-09-01 00:00:00";
		_dashboard.EventEnd = "2026-09-24 00:00:00";
		Events (1, zone: 2, code: code == 0 ? 999 : code);
		await _dashboard.LoadEventsAsync ();
		string path = _handler.Requests.Last ().Path;
		Assert.That (path, Does.StartWith ("/app/device/event/list?hid=42&size=50&mid=101&addr=2&port=2"));
		if (code == 0)
			Assert.That (path, Does.Not.Contain ("&code="));
		else
			Assert.That (path, Does.Contain ("&code=" + code));
		Assert.That (path, Does.Contain ("&begin=" + new DateTimeOffset (2026, 9, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds ()));
		Assert.That (_dashboard.EventHistory, Has.Count.EqualTo (1));
		Assert.That (_dashboard.EventDetails, Does.Contain ("2026-09-23 17:23:34").And.Contain ("GMT+01:00").And.Contain ("61 seconds").And.Contain ("fixture operator"));
		Assert.That (_dashboard.EventHistory[0].CloudTime, Is.EqualTo (DateTimeOffset.FromUnixTimeMilliseconds (Stamp).UtcDateTime.ToString ("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)));
		if (code == 0)
			Assert.That (_dashboard.EventHistory[0].Kind, Is.EqualTo ("Unknown (999)"));
		}
	[TestCase ("2026-09-01", "")]
	[TestCase ("", "bad")]
	[TestCase ("1969-12-31 23:59:59", "")]
	[TestCase ("2026-09-24 00:00:00", "2026-09-24 00:00:00")]
	[TestCase ("2026-09-24 00:00:01", "2026-09-24 00:00:00")]
	public async Task InvalidEventBoundsNeverSendRequests (string begin, string end)
		{
		await SelectAsync ();
		_dashboard.EventBegin = begin;
		_dashboard.EventEnd = end;
		Assert.That (_dashboard.CanLoadEvents, Is.False);
		await _dashboard.LoadEventsAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (3));
		}
	[Test]
	public async Task PagingIsExplicitDeduplicatesAndUsesOldestCloudTime ()
		{
		await SelectAsync ();
		Events ();
		await _dashboard.LoadEventsAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (4));
		Assert.That (_dashboard.CanLoadOlderEvents, Is.True);
		Events (50, 49);
		await _dashboard.LoadOlderEventsAsync ();
		Assert.That (_handler.Requests.Last ().Path, Does.EndWith ("&end=" + (Stamp - 49000)));
		Assert.That (_dashboard.EventHistory, Has.Count.EqualTo (99));
		Assert.That (_dashboard.CanLoadOlderEvents, Is.True);
		_handler.Reply ("{\"code\":0,\"data\":[]}");
		await _dashboard.LoadOlderEventsAsync ();
		Assert.That (_dashboard.EventHistory, Has.Count.EqualTo (99));
		Assert.That (_dashboard.CanLoadOlderEvents, Is.False);
		await _dashboard.LoadOlderEventsAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (6));
		}
	[TestCase (false)]
	[TestCase (true)]
	public async Task DuplicateOrTimestampTiedPagesStopWithoutLooping (bool newIds)
		{
		await SelectAsync ();
		Events (sameStamp: Stamp);
		await _dashboard.LoadEventsAsync ();
		Events (start: newIds ? 50 : 0, sameStamp: Stamp);
		await _dashboard.LoadOlderEventsAsync ();
		Assert.That (_dashboard.CanLoadOlderEvents, Is.False);
		Assert.That (_dashboard.EventsMessage, Does.Contain ("Paging made no progress"));
		Assert.That (_dashboard.EventHistory, Has.Count.EqualTo (newIds ? 100 : 50));
		await _dashboard.LoadOlderEventsAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (5));
		}
	[Test]
	public async Task DisplayIsBoundedAndDoesNotPretendToContainEveryEvent ()
		{
		await SelectAsync ();
		Events ();
		await _dashboard.LoadEventsAsync ();
		for (int page = 1; page < 10; page++)
			{
			Events (start: page * 50);
			await _dashboard.LoadOlderEventsAsync ();
			}
		Assert.That (_dashboard.EventHistory, Has.Count.EqualTo (500));
		Assert.That (_dashboard.CanLoadOlderEvents, Is.False);
		Assert.That (_dashboard.EventsMessage, Does.Contain ("500-event display limit"));
		}
	[Test]
	public async Task OlderReadFailureRetainsRowsAndAllowsOnlyExplicitRetry ()
		{
		await SelectAsync ();
		Events ();
		await _dashboard.LoadEventsAsync ();
		_handler.Steps.Enqueue ((_, _) => throw new HttpRequestException ("secret"));
		await _dashboard.LoadOlderEventsAsync ();
		Assert.That (_dashboard.EventHistory, Has.Count.EqualTo (50));
		Assert.That (_dashboard.CanLoadOlderEvents, Is.True);
		Assert.That (_dashboard.EventsMessage, Does.Contain ("Existing rows remain").And.Not.Contain ("secret"));
		Assert.That (_handler.Requests, Has.Count.EqualTo (5));
		}
	[Test]
	public async Task FilterChangesClearRowsAndPaginationWithoutNetworkTraffic ()
		{
		await SelectAsync ();
		Events ();
		await _dashboard.LoadEventsAsync ();
		_dashboard.EventFilter = "Watering";
		Assert.That (_dashboard.EventHistory, Is.Empty);
		Assert.That (_dashboard.CanLoadOlderEvents, Is.False);
		Events ();
		await _dashboard.LoadEventsAsync ();
		_dashboard.EventBegin = "2026-09-01 00:00:00";
		Assert.That (_dashboard.EventHistory, Is.Empty);
		Assert.That (_dashboard.EventDetails, Does.StartWith ("Select an event"));
		Events ();
		await _dashboard.LoadEventsAsync ();
		_dashboard.HistoryZone = 3;
		Assert.That (_dashboard.EventHistory, Is.Empty);
		Assert.That (_handler.Requests, Has.Count.EqualTo (6));
		}
	[Test]
	public async Task ChangingTimerOrSigningOutClearsBothHistoryPanels ()
		{
		await SelectAsync ();
		Events (1);
		await _dashboard.LoadEventsAsync ();
		_handler.Reply ("""{"code":0,"data":[{"ymd":20260924,"val":14}]}""");
		await _dashboard.LoadUsageAsync ();
		var timer = _dashboard.SelectedTimer;
		_dashboard.SelectTimer (null);
		Assert.That (_dashboard.EventHistory, Is.Empty);
		Assert.That (_dashboard.UsageHistory, Is.Empty);
		_dashboard.SelectTimer (timer);
		Events (1);
		await _dashboard.LoadEventsAsync ();
		_handler.Reply ("{\"code\":0}");
		await _dashboard.DisconnectAsync ();
		Assert.That (_dashboard.EventHistory, Is.Empty);
		Assert.That (_dashboard.CanLoadEvents, Is.False);
		}
	[Test]
	public async Task BusyHistoryReadBlocksFilterChangesAndCloseCancelsWithoutPublishingResults ()
		{
		await SelectAsync ();
		_handler.Steps.Enqueue (async (_, token) => { await Task.Delay (Timeout.Infinite, token); throw new InvalidOperationException ("Unreachable"); });
		Task read = _dashboard.LoadUsageAsync ();
		_dashboard.HistoryZone = 3;
		_dashboard.UsagePeriod = "Monthly";
		_dashboard.EventFilter = "Watering";
		await _dashboard.LoadEventsAsync ();
		await _dashboard.CloseAsync ();
		await read;
		Assert.That (_dashboard.HistoryZone, Is.EqualTo (1));
		Assert.That (_dashboard.UsagePeriod, Is.EqualTo ("Daily"));
		Assert.That (_dashboard.EventFilter, Is.EqualTo ("All event types"));
		Assert.That (_dashboard.UsageHistory, Is.Empty);
		Assert.That (_handler.Requests, Has.Count.EqualTo (4));
		}
	}
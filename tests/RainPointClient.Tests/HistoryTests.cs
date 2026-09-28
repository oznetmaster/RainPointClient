// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class HistoryTests
	{
	private ScriptedHandler _handler = null!;
	private HttpClient _http = null!;
	private RainPointCloudClient _client = null!;
	private static DateTime Date (int day) => new (2026, 9, day);
	private static RainPointHub Hub () => new ()
		{
		Id = 101,
		HomeId = 42,
		Model = "HWG023WBRF",
		DeviceName = "fixture",
		ProductKey = "fixture",
		Devices = new[] { new RainPointDevice { Address = 2, Model = "HTV345FRF" } }
		};
	private const string Event = """{"eid":"fixture-event","mid":101,"addr":2,"port":1,"code":1,"timestamp":1790180653212,"time":"2026-09-23T17:23:34","timezone":"GMT+01:00","rule":[]}""";
	private void Events (string value) => _handler.Reply ("{\"code\":0,\"data\":[" + value + "]}");

	[SetUp]
	public async Task SetUp ()
		{
		_handler = new ScriptedHandler ();
		_http = new HttpClient (_handler, false);
		_client = new RainPointCloudClient (_http);
		_handler.Reply ("""{"code":0,"data":{"token":"fixture-session","tokenExpired":3600}}""");
		await _client.LoginAsync ("test@example.invalid", "password", "44");
		}
	[TearDown]
	public void TearDown ()
		{
		_client.Dispose ();
		_http.Dispose ();
		_handler.Dispose ();
		}

	[TestCase (RainPointUsagePeriod.Day, "day", "ymd", "20260923")]
	[TestCase (RainPointUsagePeriod.Month, "month", "ym", "202609")]
	public async Task UsageUsesAppEndpointAndTenthsOfLitres (RainPointUsagePeriod period, string endpoint, string key, string date)
		{
		_handler.Reply ("{\"code\":0,\"data\":[{\"" + key + "\":\"" + date + "\",\"val\":\"104\"}]}");
		IReadOnlyList<RainPointWaterUsage> result = await _client.GetTimerWaterUsageAsync (Hub (), 2, 1, period, Date (1), Date (24));
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (_handler.Requests[1].Path, Is.EqualTo ("/app/iot/log/waterAmount/" + endpoint + "/list?code=0&mid=101&addr=2&port=1&startDate=20260901&endDate=20260924"));
			Assert.That (result, Has.Count.EqualTo (1));
			Assert.That (result[0].Litres, Is.EqualTo (10.4m));
			Assert.That (result[0].PeriodStart, Is.EqualTo (period == RainPointUsagePeriod.Day ? Date (23) : Date (1)));
			Assert.That (result[0].PeriodStart.Kind, Is.EqualTo (DateTimeKind.Unspecified));
			Assert.That (((IList<RainPointWaterUsage>)result).IsReadOnly, Is.True);
			}
		}

	[Test]
	public async Task MissingNegativeAndZeroAreDistinctAndBucketsAreSorted ()
		{
		_handler.Reply ("""{"code":0,"data":[{"ymd":20260924,"val":0},{"ymd":20260921},{"ymd":20260923,"val":-1}]}""");
		var result = await _client.GetTimerWaterUsageAsync (Hub (), 2, 1, RainPointUsagePeriod.Day, Date (1), Date (24));
		Assert.That (result.Select (row => row.Litres), Is.EqualTo (new decimal?[] { null, null, 0m }));
		Assert.That (result.Select (row => row.PeriodStart.Day), Is.EqualTo (new[] { 21, 23, 24 }));
		}

	[TestCase ("[]")]
	[TestCase ("[{\"ymd\":20260924,\"val\":null}]")]
	public async Task EmptyAndNullReadingsDoNotInventUsage (string rows)
		{
		_handler.Reply ("{\"code\":0,\"data\":" + rows + "}");
		var result = await _client.GetTimerWaterUsageAsync (Hub (), 2, 1, RainPointUsagePeriod.Day, Date (1), Date (24));
		Assert.That (result.All (row => row.Litres is null), Is.True);
		}

	[TestCase ("[null]")]
	[TestCase ("[{}]")]
	[TestCase ("[{\"ymd\":20260230}]")]
	[TestCase ("[{\"ymd\":20260925}]")]
	[TestCase ("[{\"ymd\":20260924},{\"ymd\":20260924}]")]
	[TestCase ("null")]
	public void InvalidBucketsAreRejected (string rows)
		{
		_handler.Reply ("{\"code\":0,\"data\":" + rows + "}");
		Assert.That (async () => await _client.GetTimerWaterUsageAsync (Hub (), 2, 1, RainPointUsagePeriod.Day, Date (1), Date (24)), Throws.TypeOf<RainPointException> ());
		}

	[TestCase (0, 0, 0)]
	[TestCase (4, 0, 0)]
	[TestCase (1, 2, 0)]
	[TestCase (1, 0, 30)]
	public void InvalidUsageOptionsDoNotSendRequests (int zone, int period, int range)
		{
		Assert.That (async () => await _client.GetTimerWaterUsageAsync (Hub (), 2, zone, (RainPointUsagePeriod)period, Date (1), Date (1).AddDays (range)), Throws.InstanceOf<ArgumentException> ());
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		}

	[Test]
	public void AmbiguousDatesAndLongMonthlyRangesAreRejected ()
		{
		Assert.That (async () => await _client.GetTimerWaterUsageAsync (Hub (), 2, 1, RainPointUsagePeriod.Day, DateTime.SpecifyKind (Date (1), DateTimeKind.Utc), Date (24)), Throws.ArgumentException);
		Assert.That (async () => await _client.GetTimerWaterUsageAsync (Hub (), 2, 1, RainPointUsagePeriod.Day, Date (1).AddHours (1), Date (24)), Throws.ArgumentException);
		Assert.That (async () => await _client.GetTimerWaterUsageAsync (Hub (), 2, 1, RainPointUsagePeriod.Month, Date (1).AddYears (-2), Date (24)), Throws.ArgumentException);
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		}

	[Test]
	public async Task EventFiltersUseCloudMillisecondsAndPreserveIndependentLocalTime ()
		{
		Events (Event);
		var result = await _client.GetEventsAsync (42, new RainPointEventQuery { HubId = 101, Address = 2, Zone = 1, Code = 1, Limit = 1, Begin = DateTimeOffset.FromUnixTimeMilliseconds (1790180600000), End = DateTimeOffset.FromUnixTimeMilliseconds (1790180700000) });
		RainPointEvent item = result.Events.Single ();
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (_handler.Requests[1].Path, Is.EqualTo ("/app/device/event/list?hid=42&size=1&mid=101&addr=2&port=1&code=1&begin=1790180600000&end=1790180700000"));
			Assert.That (item.CloudTimestamp.ToUnixTimeMilliseconds (), Is.EqualTo (1790180653212));
			Assert.That (item.ReportedLocalTime, Is.EqualTo (new DateTime (2026, 9, 23, 17, 23, 34)));
			Assert.That (item.ReportedLocalTime!.Value.Kind, Is.EqualTo (DateTimeKind.Unspecified));
			Assert.That (item.ReportedTimeZone, Is.EqualTo ("GMT+01:00"));
			Assert.That (item.Kind, Is.EqualTo (RainPointEventKind.Watering));
			Assert.That (result.IsLimitReached, Is.True);
			Assert.That (result.OldestTimestamp, Is.EqualTo (item.CloudTimestamp));
			Assert.That (((IList<RainPointEvent>)result.Events).IsReadOnly, Is.True);
			}
		}

	[Test]
	public async Task EventDetailsDecodeUnitsModesAndOperator ()
		{
		Events (Event.Replace ("[]", """[{"type":"1","value":"1"},{"type":"2","value":"2"},{"type":"3","value":"14"},{"type":"4","value":"26"},{"type":"9","value":"fixture operator"},{"type":"11","value":"0"}]"""));
		var item = (await _client.GetEventsAsync (42)).Events.Single ();
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (item.WaterUsedLitres, Is.EqualTo (1.4m));
			Assert.That (item.Duration, Is.EqualTo (TimeSpan.FromSeconds (26)));
			Assert.That (item.WorkModeCode, Is.EqualTo (1));
			Assert.That (item.ControlModeCode, Is.EqualTo (2));
			Assert.That (item.ExceptionCode, Is.Zero);
			Assert.That (item.Operator, Is.EqualTo ("fixture operator"));
			Assert.That (item.IsOnline, Is.Null);
			Assert.That (item.HasUninterpretedDetails, Is.False);
			Assert.That (_handler.Requests[1].Path, Is.EqualTo ("/app/device/event/list?hid=42&size=50"));
			}
		}

	[TestCase ("[{\"type\":\"3\",\"value\":\"-1\"}]")]
	[TestCase ("[{\"type\":\"3\",\"value\":\"invalid\"}]")]
	[TestCase ("[{\"type\":\"3\",\"value\":\"14\"},{\"type\":\"3\",\"value\":\"14\"}]")]
	[TestCase ("[{\"type\":\"999\",\"value\":\"2\"}]")]
	[TestCase ("[{\"type\":\"4\",\"value\":\"9223372036854775807\"}]")]
	[TestCase ("[null]")]
	[TestCase ("null")]
	public async Task UnknownOrMalformedDetailsDoNotFabricateReadings (string rules)
		{
		Events (Event.Replace ("[]", rules).Replace ("\"code\":1", "\"code\":999"));
		var item = (await _client.GetEventsAsync (42)).Events.Single ();
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (item.HasUninterpretedDetails, Is.True);
			Assert.That (item.WaterUsedLitres, Is.Null);
			Assert.That (item.Duration, Is.Null);
			Assert.That (item.Code, Is.EqualTo (999));
			Assert.That (item.Kind, Is.EqualTo (RainPointEventKind.Unknown));
			}
		}

	[TestCase ("0", false)]
	[TestCase ("1", true)]
	[TestCase ("2", null)]
	public async Task OnlineStatusRecognizesOnlyKnownValues (string value, bool? online)
		{
		Events (Event.Replace ("[]", "[{\"type\":\"8\",\"value\":\"" + value + "\"}]"));
		Assert.That ((await _client.GetEventsAsync (42)).Events[0].IsOnline, Is.EqualTo (online));
		}

	[TestCase ("timestamp", "-1")]
	[TestCase ("timestamp", "9223372036854775807")]
	[TestCase ("mid", "102")]
	[TestCase ("addr", "3")]
	[TestCase ("port", "2")]
	[TestCase ("code", "2")]
	public void InvalidEventMetadataAndFilterMismatchesAreRejected (string key, string value)
		{
		string row = System.Text.RegularExpressions.Regex.Replace (Event, "\"" + key + "\":-?[0-9]+", "\"" + key + "\":" + value);
		Events (row);
		Assert.That (async () => await _client.GetEventsAsync (42, new RainPointEventQuery { HubId = 101, Address = 2, Zone = 1, Code = 1 }), Throws.TypeOf<RainPointException> ());
		}

	[TestCase ("[null]")]
	[TestCase ("[{}]")]
	[TestCase ("null")]
	public void InvalidEventRowsAreRejected (string rows)
		{
		_handler.Reply ("{\"code\":0,\"data\":" + rows + "}");
		Assert.That (async () => await _client.GetEventsAsync (42), Throws.TypeOf<RainPointException> ());
		}

	[Test]
	public async Task DuplicateEventsArePreservedAndAnEmptyPageHasNoCursor ()
		{
		Events (Event + "," + Event);
		var page = await _client.GetEventsAsync (42);
		Assert.That (page.Events, Has.Count.EqualTo (2));
		Assert.That (page.IsLimitReached, Is.False);
		Events (string.Empty);
		var empty = await _client.GetEventsAsync (42);
		Assert.That (empty.Events, Is.Empty);
		Assert.That (empty.OldestTimestamp, Is.Null);
		}

	[Test]
	public void InvalidFiltersDoNotSendRequests ()
		{
		RainPointEventQuery[] queries = { new () { HubId = 0 }, new () { Address = 2 }, new () { Zone = 1 }, new () { Limit = 0 }, new () { Limit = 51 }, new () { Code = -1 }, new () { Begin = DateTimeOffset.UtcNow, End = DateTimeOffset.FromUnixTimeMilliseconds (0) } };
		foreach (var query in queries)
			{
			Assert.That (async () => await _client.GetEventsAsync (42, query), Throws.ArgumentException);
			}
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		}

	[Test]
	public async Task CloudErrorsAndCancellationAreNotRetried ()
		{
		_handler.Reply ("{\"code\":9993}");
		Assert.That (async () => await _client.GetEventsAsync (42), Throws.TypeOf<RainPointException> ());
		_handler.Steps.Enqueue (async (_, token) => { await Task.Delay (Timeout.Infinite, token); throw new InvalidOperationException (); });
		using CancellationTokenSource cancellation = new ();
		var request = _client.GetEventsAsync (42, cancellationToken: cancellation.Token);
		cancellation.Cancel ();
		Assert.That (async () => await request, Throws.InstanceOf<OperationCanceledException> ());
		Assert.That (_handler.Requests, Has.Count.EqualTo (3));
		}
	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public async Task TimerWidePowerEventsKeepZoneZeroWhenQueryingAnyZone (int zone)
		{
		Events (Event.Replace ("\"port\":1", "\"port\":0").Replace ("\"code\":1", "\"code\":5"));
		var page = await _client.GetEventsAsync (42, new RainPointEventQuery { HubId = 101, Address = 2, Zone = zone });
		Assert.That (page.Events[0].Zone, Is.Zero);
		Assert.That (page.Events[0].Kind, Is.EqualTo (RainPointEventKind.SubDevicePowerOn));
		}
	[TestCase (1)]
	[TestCase (3)]
	[TestCase (99)]
	public void ZoneZeroDoesNotAdmitArbitraryEventKinds (int code)
		{
		Events (Event.Replace ("\"port\":1", "\"port\":0").Replace ("\"code\":1", "\"code\":" + code));
		Assert.ThrowsAsync<RainPointException> (async () => await _client.GetEventsAsync (42, new RainPointEventQuery { HubId = 101, Address = 2, Zone = 2 }));
		}

	}
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
namespace RainPointClient.Tests;

[TestFixture]
public sealed class SceneTests
	{
	internal const string LIST = """{"code":0,"data":[{"id":12,"sceneName":"Fixture","sceneFlags":"9","open":0,"enable":1}]}""";
	internal const string DETAIL = """{"code":0,"data":{"id":12,"sceneName":"Fixture","sceneExecutant":101,"sceneFlags":"9","sceneFreq":1,"sceneInterval":120,"startDate":65535,"endDate":65535,"startTime":65535,"endTime":65535,"dateRepeat":0,"open":0,"enable":1,"conditions":[{"type":1,"code":4,"enable":1,"contrast":2,"value1":"32000000","dataType":0}],"actions":[{"type":1,"code":1,"enable":1,"value":"Rain expected","param":"10"}]}}""";
	internal const string HUBS = """{"code":0,"data":[{"mid":101,"model":"HWG023WBRF","modelCode":10,"deviceName":"fixture","productKey":"fixture","function":"{\"SM\":7}","supportSmart":7,"subDevices":[{"sid":42,"addr":2,"model":"HTV345FRF","modelCode":38,"function":"{\"SM\":7}","supportSmart":7}]}]}""";
	internal const string CATALOG = """{"code":0,"data":{"models":[{"model":"HWG023WBRF","modelCode":10,"supportSmart":7},{"model":"HTV345FRF","modelCode":38,"supportSmart":7}]}}""";
	private ScriptedHandler _handler = null!; private HttpClient _http = null!; private RainPointCloudClient _client = null!;
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
	private async Task<RainPointScene> Read ()
		{
		_handler.Reply (LIST);
		_handler.Reply (DETAIL);
		return await _client.GetSceneAsync (5, 12);
		}
	[Test]
	public async Task ReadsTypedDetailsWithoutExposingPayloads ()
		{
		var scene = await Read ();
		Assert.That (scene.Enabled, Is.False);
		Assert.That (scene.Available, Is.True);
		Assert.That (scene.Conditions.Single ().Threshold, Is.EqualTo (50));
		Assert.That (scene.Actions.Single ().Message, Is.EqualTo ("Rain expected"));
		Assert.That (_handler.Requests.Skip (1).All (r => r.Method == HttpMethod.Get), Is.True);
		}
	[TestCase (true)]
	[TestCase (false)]
	public async Task SwitchIsExactScopedAndOneAttempt (bool enabled)
		{
		var scene = await Read ();
		_handler.Reply (LIST);
		_handler.Reply (DETAIL);
		_handler.Steps.Enqueue ((r, _) => { Assert.That (r.Headers.GetValues ("hid").Single (), Is.EqualTo ("5")); return Task.FromResult (new HttpResponseMessage (HttpStatusCode.OK) { Content = new StringContent ("{\"code\":0}") }); });
		await _client.SetSceneEnabledAsync (scene, enabled);
		Assert.That (_handler.Requests.Last ().Body, Is.EqualTo ("{\"id\":12,\"open\":" + (enabled ? 1 : 0) + "}"));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.SetSceneEnabledAsync (scene, enabled));
		}
	[Test]
	public async Task DeleteOmitsSwitchAndDoesNotReplayFailure ()
		{
		var scene = await Read ();
		_handler.Reply (LIST);
		_handler.Reply (DETAIL);
		_handler.Reply ("{\"code\":42}");
		Assert.ThrowsAsync<RainPointException> (async () => await _client.DeleteSceneAsync (scene));
		Assert.That (_handler.Requests.Last ().Body, Is.EqualTo ("{\"id\":12}"));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.DeleteSceneAsync (scene));
		}
	[Test]
	public async Task ConcurrentChangePreventsWrite ()
		{
		var scene = await Read ();
		_handler.Reply (LIST);
		_handler.Reply (DETAIL.Replace ("120", "60"));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.SetSceneEnabledAsync (scene, true));
		Assert.That (_handler.Requests.Skip (1).All (r => r.Method == HttpMethod.Get), Is.True);
		}
	[Test]
	public async Task MissingMembershipPreventsDetailRead ()
		{
		_handler.Reply ("{\"code\":0,\"data\":[]}");
		Assert.ThrowsAsync<ArgumentException> (async () => await _client.GetSceneAsync (5, 12));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}
	[TestCase ("{\"id\":0}")]
	[TestCase ("{\"id\":12},{\"id\":12}")]
	public void InvalidSceneListsAreRejected (string items)
		{
		_handler.Reply ("{\"code\":0,\"data\":[" + items + "]}");
		Assert.ThrowsAsync<RainPointException> (async () => await _client.GetScenesAsync (5));
		}
	[Test]
	public async Task AccountSwitchInvalidatesObservation ()
		{
		var scene = await Read ();
		_handler.Reply ("""{"code":0,"data":{"token":"next","tokenExpired":3600}}""");
		await _client.LoginAsync ("other@example.invalid", "fixture", "44");
		int count = _handler.Requests.Count;
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.DeleteSceneAsync (scene));
		Assert.That (_handler.Requests, Has.Count.EqualTo (count));
		}
	[TestCase (RainPointSceneWeatherMetric.TemperatureCelsius, -40, "70FEFFFF")]
	[TestCase (RainPointSceneWeatherMetric.TemperatureCelsius, 20, "A8020000")]
	[TestCase (RainPointSceneWeatherMetric.HumidityPercent, 100, "64000000")]
	[TestCase (RainPointSceneWeatherMetric.RainProbabilityPercent, 50, "32000000")]
	[TestCase (RainPointSceneWeatherMetric.WindSpeedKilometresPerHour, 150, "DC050000")]
	public void WeatherEncodingUsesIndependentWireVectors (RainPointSceneWeatherMetric metric, int value, string hex)
		{
		var condition = RainPointSceneCondition.Weather (metric, RainPointSceneComparison.GreaterThan, value);
		Assert.That (condition.Wire.Value1, Is.EqualTo (hex));
		Assert.That (condition.Threshold, Is.EqualTo (value));
		}
	[TestCase (RainPointSceneWeatherMetric.RainProbabilityPercent, -1)]
	[TestCase (RainPointSceneWeatherMetric.HumidityPercent, 101)]
	[TestCase (RainPointSceneWeatherMetric.TemperatureCelsius, 61)]
	[TestCase (RainPointSceneWeatherMetric.WindSpeedKilometresPerHour, 151)]
	public void WeatherBoundsRejectInvalidDrafts (RainPointSceneWeatherMetric metric, int value) => Assert.Throws<ArgumentOutOfRangeException> (() => RainPointSceneCondition.Weather (metric, RainPointSceneComparison.Equal, value));
	[TestCase (RainPointSceneRepeat.Daily, RainPointSceneTime.Clock, "001E0200")]
	[TestCase (RainPointSceneRepeat.OddDates, RainPointSceneTime.Sunrise, "01004000")]
	[TestCase (RainPointSceneRepeat.EvenDates, RainPointSceneTime.Sunset, "02014000")]
	public void RepeatVectors (RainPointSceneRepeat repeat, RainPointSceneTime time, string expected)
		{
		var c = RainPointSceneCondition.Repeating (repeat, time, time == RainPointSceneTime.Clock ? new TimeSpan (8, 30, 0) : null);
		Assert.That (c.Wire.Value1, Is.EqualTo (expected));
		Assert.That (c.Kind, Is.EqualTo (RainPointSceneConditionKind.RepeatingTime));
		}
	[TestCase (DayOfWeek.Sunday)]
	[TestCase (DayOfWeek.Monday)]
	[TestCase (DayOfWeek.Tuesday)]
	[TestCase (DayOfWeek.Wednesday)]
	[TestCase (DayOfWeek.Thursday)]
	[TestCase (DayOfWeek.Friday)]
	[TestCase (DayOfWeek.Saturday)]
	public void WeekdayBitOrderMatchesSundayFirst (DayOfWeek day)
		{
		var c = RainPointSceneCondition.Repeating (RainPointSceneRepeat.Weekdays, RainPointSceneTime.Sunrise, weekdays: (RainPointSceneWeekdays)(1 << (int)day));
		Assert.That (c.Wire.Value1!.Substring (0, 2), Is.EqualTo ((128 | 1 << (int)day).ToString ("X2")));
		Assert.That (c.Weekdays, Is.EqualTo ((RainPointSceneWeekdays)(1 << (int)day)));
		}
	[Test]
	public void OncePreservesHomeWallTimeAndRejectsUtc ()
		{
		DateTime date = new (2026, 9, 24, 8, 30, 0, DateTimeKind.Unspecified);
		var c = RainPointSceneCondition.Once (date);
		Assert.That (c.Wire.Value1, Is.EqualTo ("8087701A"));
		Assert.That (c.AtLocal, Is.EqualTo (date));
		Assert.Throws<ArgumentException> (() => RainPointSceneCondition.Once (DateTime.SpecifyKind (date, DateTimeKind.Utc)));
		}
	[Test]
	public void MalformedAndUnknownConditionsRemainUnsupported ()
		{
		Assert.That (new RainPointSceneCondition (new ()
			{
			Type = 2,
			Code = 1,
			Value1 = "FFFFFFFF"
			}).Kind, Is.EqualTo (RainPointSceneConditionKind.Unsupported));
		Assert.That (new RainPointSceneCondition (new ()
			{
			Type = 0,
			Code = 999,
			Value1 = "01000000"
			}).Kind, Is.EqualTo (RainPointSceneConditionKind.Unsupported));
		}
	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public async Task RainDelayAllTimerZonesAreEncodedSeparately (int zone)
		{
		_handler.Reply (HUBS);
		var hub = (await _client.GetHubsAsync (5)).Single ();
		var action = RainPointSceneAction.RainDelay (hub, 2, zone, 3);
		Assert.That (action.Wire.Value, Is.EqualTo (zone.ToString ()));
		Assert.That (action.Wire.Parameter, Is.EqualTo ("72"));
		Assert.That (action.Zones.Single (), Is.EqualTo (zone));
		}
	[Test]
	public void NotificationsValidateRecipientsAndLength ()
		{
		var action = RainPointSceneAction.Notify ("Fixture", [10], ["fixture@example.invalid"]);
		Assert.That (action.Wire.Parameter, Is.EqualTo ("10|fixture@example.invalid"));
		Assert.Throws<ArgumentException> (() => RainPointSceneAction.Notify ("Fixture", [], []));
		Assert.Throws<ArgumentException> (() => RainPointSceneAction.Notify (new string ('x', 101), [10], []));
		Assert.Throws<ArgumentException> (() => RainPointSceneAction.Notify ("Fixture", [10, 10], []));
		}
	internal static RainPointSceneDraft Draft () => new () { Name = "Fixture", Conditions = [RainPointSceneCondition.Repeating (RainPointSceneRepeat.Daily, RainPointSceneTime.Clock, new TimeSpan (8, 30, 0))], Actions = [RainPointSceneAction.Notify ("Fixture", [10], [])] };
	[Test]
	public async Task CreateUsesHomeHeaderAndNeverClaimsDisabledDraft ()
		{
		_handler.Reply (HUBS);
		var hub = (await _client.GetHubsAsync (5)).Single ();
		_handler.Reply (HUBS);
		_handler.Reply (CATALOG);
		_handler.Reply ("""{"code":0,"data":[{"uid":10}]}""");
		_handler.Reply ("{\"code\":0}");
		await _client.CreateSceneAsync (hub, Draft ());
		using var json = JsonDocument.Parse (_handler.Requests.Last ().Body!);
		var body = json.RootElement;
		Assert.That (body.GetProperty ("sceneFlags").GetInt32 (), Is.EqualTo (9));
		Assert.That (body.GetProperty ("startTime").GetInt32 (), Is.EqualTo (65535));
		Assert.That (body.TryGetProperty ("open", out _), Is.False);
		Assert.That (body.TryGetProperty ("enable", out _), Is.False);
		Assert.That (body.TryGetProperty ("id", out _), Is.False);
		}
	[Test]
	public async Task UnsupportedHubNeverSaves ()
		{
		_handler.Reply (HUBS);
		var hub = (await _client.GetHubsAsync (5)).Single ();
		_handler.Reply (HUBS);
		_handler.Reply (CATALOG.Replace ("\"supportSmart\":7", "\"supportSmart\":0"));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.CreateSceneAsync (hub, Draft ()));
		Assert.That (_handler.Requests.Skip (1).All (r => r.Method == HttpMethod.Get), Is.True);
		}
	[TestCase (-1, 120)]
	[TestCase (31, 120)]
	[TestCase (1, 0)]
	[TestCase (1, 1441)]
	public void LimitsRejectOutOfRange (int maximum, int interval)
		{
		var draft = Draft ();
		draft.MaximumRunsPerDay = maximum;
		draft.MinimumIntervalMinutes = interval;
		Assert.Throws<ArgumentOutOfRangeException> (() => draft.Encode (101));
		}
	[Test]
	public void AllMatchRejectsTwoIndependentTimes ()
		{
		var draft = Draft ();
		draft.MatchAll = true;
		draft.Conditions = [draft.Conditions[0], RainPointSceneCondition.Once (new (2026, 10, 1))];
		Assert.Throws<ArgumentException> (() => draft.Encode (101));
		}
	[Test]
	public void DateWindowIsEncodedAndReversedDatesRejected ()
		{
		var draft = Draft ();
		draft.StartsOn = new (2026, 9, 24);
		draft.EndsOn = new (2026, 10, 1);
		var wire = draft.Encode (101);
		Assert.That (wire.StartDate, Is.EqualTo (0x0D38));
		Assert.That (wire.EndDate, Is.EqualTo (0x0D41));
		draft.EndsOn = new (2026, 9, 1);
		Assert.Throws<ArgumentException> (() => draft.Encode (101));
		}
	[Test]
	public void MissingEnabledStateIsUnknownAndCannotBeWritten ()
		{
		var condition = new RainPointSceneCondition (new ()
			{
			Type = 1,
			Code = 4,
			Value1 = "32000000"
			});
		Assert.That (condition.Enabled, Is.Null);
		Assert.That (condition.Kind, Is.EqualTo (RainPointSceneConditionKind.Unsupported));
		var draft = Draft ();
		draft.Conditions = [condition];
		Assert.Throws<ArgumentException> (() => draft.Encode (101));
		}
	[Test] public void CatalogExplicitlyDisablingScenesDoesNotNeedRuntimeMetadata () => Assert.That (SceneFunction.Supports (null, 0, 2), Is.False);
	[Test]
	public async Task ConcurrentSceneAttemptsSendAtMostOneWrite ()
		{
		var scene = await Read ();
		var entered = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource<HttpResponseMessage> (TaskCreationOptions.RunContinuationsAsynchronously);
		_handler.Reply (LIST);
		// Hold the first response directly, without a generated async callback state machine.
		_handler.Steps.Enqueue ((request, token) => { entered.TrySetResult (true); return release.Task; });
		Task first = _client.SetSceneEnabledAsync (scene, false);
		bool rejected = false;
		try
			{
			Task ready = await Task.WhenAny (entered.Task, first, Task.Delay (5000));
			if (ready == first)
				await first; // Report an early operation failure instead of hiding it behind a timeout.
			Assert.That (ready, Is.SameAs (entered.Task), "The first write must reach its blocked fresh-read response before the competing call.");
			_handler.Reply (LIST);
			_handler.Reply (DETAIL);
			_handler.Reply ("{\"code\":0}");
			await _client.DeleteSceneAsync (scene);
			}
		finally
			{
			release.TrySetResult (new HttpResponseMessage (HttpStatusCode.OK) { Content = new StringContent (DETAIL) });
			try
				{
				await first;
				}
			catch (InvalidOperationException) { rejected = true; }
			}
		Assert.That (rejected, Is.True, "The first caller must reject its consumed observation.");
		Assert.That (_handler.Requests.Count (r => r.Path.StartsWith ("/app/scene/") && r.Method == HttpMethod.Post), Is.EqualTo (1));
		}

	[Test]
	public async Task EditingPreservesEffectivePeriodAndRejectsUnknownRecurrence ()
		{
		var scene = await Read ();
		var draft = scene.CreateDraft ();
		Assert.That (draft.StartsOn, Is.Null);
		Assert.That (draft.Actions.Single ().Message, Is.EqualTo ("Rain expected"));
		scene.Wire.StartDate = 0x0D38;
		scene.Wire.EndDate = 0x0D41;
		scene.Wire.StartTime = 512;
		scene.Wire.EndTime = 1024;
		draft = scene.CreateDraft ();
		Assert.That (draft.StartsOn, Is.EqualTo (new DateTime (2026, 9, 24)));
		Assert.That (draft.WindowStartsAt, Is.EqualTo (TimeSpan.FromHours (8)));
		scene.Wire.DateRepeat = 6;
		Assert.Throws<NotSupportedException> (() => scene.CreateDraft ());
		}

	[TestCase (RainPointSceneRepeat.Daily, RainPointSceneWeekdays.None, 0)]
	[TestCase (RainPointSceneRepeat.OddDates, RainPointSceneWeekdays.None, 1)]
	[TestCase (RainPointSceneRepeat.EvenDates, RainPointSceneWeekdays.None, 2)]
	[TestCase (RainPointSceneRepeat.Weekdays, RainPointSceneWeekdays.Monday | RainPointSceneWeekdays.Friday, 162)]
	public void EffectiveDateRecurrencePreservesVendorBitOrder (RainPointSceneRepeat repeat, RainPointSceneWeekdays days, int encoded)
		{
		var draft = Draft ();
		draft.EffectiveRepeat = repeat;
		draft.EffectiveWeekdays = days;
		Assert.That (draft.Encode (101).DateRepeat, Is.EqualTo (encoded));
		}

	[TestCase (RainPointSceneSolarPeriod.Daytime, 16384, 16385)]
	[TestCase (RainPointSceneSolarPeriod.Nighttime, 16385, 16384)]
	public async Task SolarEffectivePeriodRoundTripsAndRequiresLocation (RainPointSceneSolarPeriod solar, int start, int end)
		{
		var scene = await Read ();
		scene.Wire.StartTime = start;
		scene.Wire.EndTime = end;
		var draft = scene.CreateDraft ();
		Assert.That (draft.SolarPeriod, Is.EqualTo (solar));
		Assert.That (draft.WindowStartsAt, Is.Null);
		var wire = draft.Encode (101);
		Assert.That (wire.StartTime, Is.EqualTo (start));
		Assert.That (wire.EndTime, Is.EqualTo (end));
		_handler.Reply (HUBS);
		var hub = (await _client.GetHubsAsync (5)).Single ();
		draft.Conditions = [RainPointSceneCondition.Weather (RainPointSceneWeatherMetric.HumidityPercent, RainPointSceneComparison.GreaterThan, 80)];
		_handler.Reply (HUBS);
		_handler.Reply (CATALOG);
		_handler.Reply ("""{"code":0,"data":[{"uid":10}]}""");
		_handler.Reply ("""{"code":0,"data":{"hid":5,"rooms":[],"lat":0,"lon":0}}""");
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.CreateSceneAsync (hub, draft));
		Assert.That (_handler.Requests.Count (q => q.Method == HttpMethod.Post), Is.EqualTo (1));
		}
	[TestCase (RainPointSceneSolarPeriod.Daytime)]
	[TestCase (RainPointSceneSolarPeriod.Nighttime)]
	public void SolarWindowsRejectClockBoundariesAndTimeTriggers (RainPointSceneSolarPeriod solar)
		{
		var draft = Draft ();
		draft.SolarPeriod = solar;
		draft.WindowStartsAt = TimeSpan.FromHours (8);
		draft.WindowEndsAt = TimeSpan.FromHours (9);
		Assert.Throws<ArgumentException> (() => draft.Encode (101));
		draft.WindowStartsAt = draft.WindowEndsAt = null;
		draft.Conditions = [RainPointSceneCondition.Repeating (RainPointSceneRepeat.Daily, RainPointSceneTime.Sunset)];
		Assert.Throws<ArgumentException> (() => draft.Encode (101));
		}
	[TestCase (16384, 16384)]
	[TestCase (16385, 65535)]
	[TestCase (512, 16385)]
	public async Task UnsupportedMixedSolarBoundariesCannotBeDroppedDuringEditing (int start, int end)
		{
		var scene = await Read ();
		scene.Wire.StartTime = start;
		scene.Wire.EndTime = end;
		Assert.Throws<NotSupportedException> (() => scene.CreateDraft ());
		}

	}
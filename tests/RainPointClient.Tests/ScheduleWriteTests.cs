using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class ScheduleWriteTests
	{
	private const string EMPTY = "01020304,/,aux,646464,tail|z2,8000483c00/,z2aux,percent|z3,/,z3aux,other";
	private ScriptedHandler _handler = null!;
	private HttpClient _http = null!;
	private RainPointCloudClient _client = null!;
	private RainPointHub _hub = null!;

	[SetUp]
	public async Task SetUp ()
		{
		_handler = new ScriptedHandler ();
		_http = new HttpClient (_handler, disposeHandler: false);
		_client = new RainPointCloudClient (_http);
		_hub = new RainPointHub
			{
			Id = 101,
			HomeId = 5,
			Model = "HWG023WBRF",
			DeviceName = "hub",
			ProductKey = "product",
			Devices = new[] { new RainPointDevice { Id = 42, Address = 2, Model = "HTV345FRF" } }
			};
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

	private void Discovery (string parameter, string firmware = "130", int id = 42)
		{
		_handler.Reply (JsonSerializer.Serialize (new
			{
			code = 0,
			data = new[] { new { mid = 101, model = "HWG023WBRF", deviceName = "hub", productKey = "product",
				subDevices = new[] { new { sid = id, addr = 2, model = "HTV345FRF", portNumber = 3, softVer = firmware, param = parameter } } } }
			}));
		}

	private async Task<RainPointScheduleSnapshot> Read (string parameter = EMPTY, string firmware = "130")
		{
		Discovery (parameter, firmware);
		return await _client.GetTimerSchedulesAsync (_hub, 2, 1);
		}

	public static System.Collections.Generic.IEnumerable<object[]> OnceWriteCases ()
		{
		foreach (int zone in new[] { 1, 2, 3 })
			foreach (int mode in new[] { 1, 2, 3 })
				foreach (bool enabled in new[] { false, true })
					yield return new object[] { zone, mode, enabled };
		}
	[TestCaseSource (nameof (OnceWriteCases))]
	public async Task OnceCreateAndReplacementAreRejectedBeforeNetwork (int zone, int mode, bool enabled)
		{
		string parameter = string.Join ("|", Enumerable.Repeat ("settings,8000483c00/,aux", 3));
		Discovery (parameter);
		RainPointScheduleSnapshot snapshot = await _client.GetTimerSchedulesAsync (_hub, 2, zone);
		int requests = _handler.Requests.Count;
		DateTime date = new (2026, 9, 25);
		Task Write (bool replace) => mode switch
			{
				1 => replace ? _client.UpdateTimerScheduleAsync (_hub, snapshot, 0, new RainPointIrrigationSchedule { Enabled = enabled, Repeat = RainPointScheduleRepeat.Once, EffectiveDate = date })
					: _client.AddTimerScheduleAsync (_hub, snapshot, new RainPointIrrigationSchedule { Enabled = enabled, Repeat = RainPointScheduleRepeat.Once, EffectiveDate = date }),
				2 => replace ? _client.UpdateTimerScheduleAsync (_hub, snapshot, 0, new RainPointMistingSchedule { Enabled = enabled, Repeat = RainPointScheduleRepeat.Once, EffectiveDate = date })
					: _client.AddTimerScheduleAsync (_hub, snapshot, new RainPointMistingSchedule { Enabled = enabled, Repeat = RainPointScheduleRepeat.Once, EffectiveDate = date }),
				_ => replace ? _client.UpdateTimerScheduleAsync (_hub, snapshot, 0, new RainPointCycleAndSoakSchedule { Enabled = enabled, Repeat = RainPointScheduleRepeat.Once, EffectiveDate = date })
					: _client.AddTimerScheduleAsync (_hub, snapshot, new RainPointCycleAndSoakSchedule { Enabled = enabled, Repeat = RainPointScheduleRepeat.Once, EffectiveDate = date })
				};
		Assert.ThrowsAsync<NotSupportedException> (async () => await Write (false));
		Assert.ThrowsAsync<NotSupportedException> (async () => await Write (true));
		Assert.That (_handler.Requests, Has.Count.EqualTo (requests));
		}

	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public async Task ExistingOnceCanBeReadDisabledAndDeletedButNeverEnabled (int zone)
		{
		string original = string.Join ("|", Enumerable.Repeat ("settings,8000403c000000390d/,aux", 3));
		Discovery (original);
		RainPointScheduleSnapshot snapshot = await _client.GetTimerSchedulesAsync (_hub, 2, zone);
		Assert.That (snapshot.Schedules.Single ().Repeat, Is.EqualTo (RainPointScheduleRepeat.Once));
		Assert.ThrowsAsync<NotSupportedException> (async () => await _client.SetTimerScheduleEnabledAsync (_hub, snapshot, 0, true));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		Discovery (original);
		_handler.Reply ("{\"code\":0}");
		await _client.SetTimerScheduleEnabledAsync (_hub, snapshot, 0, false);
		string disabled = JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (_handler.Requests.Last ().Body!)!.Parameter;
		string[] expected = original.Split ('|');
		expected[zone - 1] = expected[zone - 1].Replace ("800040", "000040");
		Assert.That (disabled, Is.EqualTo (string.Join ("|", expected)));
		Discovery (disabled);
		snapshot = await _client.GetTimerSchedulesAsync (_hub, 2, zone);
		Discovery (disabled);
		_handler.Reply ("{\"code\":0}");
		await _client.DeleteTimerScheduleAsync (_hub, snapshot, 0);
		expected[zone - 1] = "settings,/,aux";
		Assert.That (JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (_handler.Requests.Last ().Body!)!.Parameter, Is.EqualTo (string.Join ("|", expected)));
		}

	[Test]
	public async Task AddIsDisabledByDefaultAndPreservesOtherSettingsAndZones ()
		{
		RainPointScheduleSnapshot before = await Read ();
		Discovery (EMPTY);
		_handler.Reply ("""{"code":0,"data":{"homeVersion":7}}""");
		await _client.AddTimerScheduleAsync (_hub, before, new RainPointIrrigationSchedule ());
		CapturedRequest request = _handler.Requests.Last ();
		Protocol.TimerParameterRequest body = JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (request.Body!)!;
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (request.Path, Is.EqualTo ("/app/device/sub/update"));
			Assert.That (request.Method, Is.EqualTo (HttpMethod.Post));
			Assert.That (body.HubId, Is.EqualTo (101));
			Assert.That (body.DeviceId, Is.EqualTo (42));
			Assert.That (body.Parameter, Is.EqualTo (EMPTY.Replace ("01020304,/,", "01020304,00004a3c0000000000/,")));
			Assert.That (_handler.Requests[_handler.Requests.Count - 2].Method, Is.EqualTo (HttpMethod.Get));
			Assert.That (before.Schedules, Is.Empty);
			}
		}

	[TestCase (false, "01020304,094862780000000000/,aux|z2,|z3,")]
	[TestCase (true, "01020304,894862780000000000/,aux|z2,|z3,")]
	public async Task UpdateSerializesSelectedWeekdaysAndLocalTime (bool enabled, string expected)
		{
		const string ORIGINAL = "01020304,8000483c00/,aux|z2,|z3,";
		RainPointScheduleSnapshot before = await Read (ORIGINAL);
		Discovery (ORIGINAL);
		_handler.Reply ("{\"code\":0}");
		await _client.UpdateTimerScheduleAsync (_hub, before, 0, new RainPointIrrigationSchedule
			{
			Enabled = enabled,
			StartTime = new TimeSpan (9, 8, 0),
			Duration = TimeSpan.FromMinutes (2),
			Repeat = RainPointScheduleRepeat.Weekdays,
			Weekdays = new[] { DayOfWeek.Sunday, DayOfWeek.Wednesday }
			});
		string parameter = JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (_handler.Requests.Last ().Body!)!.Parameter;
		Assert.That (parameter, Is.EqualTo (expected));
		}

	[TestCase (true, "81c0c914000000380d05001e00")]
	[TestCase (false, "01c0c914000000380d05001e00")]
	public async Task ToggleRetainsEveryTimingByte (bool enabled, string changed)
		{
		string record = enabled ? "01c0c914000000380d05001e00" : "81c0c914000000380d05001e00";
		string original = "settings," + record + "/8000483c00,,season,extra|zone2,|zone3,";
		RainPointScheduleSnapshot before = await Read (original);
		Discovery (original);
		_handler.Reply ("{\"code\":0}");
		await _client.SetTimerScheduleEnabledAsync (_hub, before, 0, enabled);
		Assert.That (JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (_handler.Requests.Last ().Body!)!.Parameter,
			 Is.EqualTo (original.Replace (record, changed)));
		}

	[TestCase ("settings,8000483c00/,a,b|z2,|z3,", "settings,/,a,b|z2,|z3,")]
	[TestCase ("settings,8000483c00|z2,|z3,", "settings,|z2,|z3,")]
	[TestCase ("settings,8000483c00,8100487800|z2,|z3,", "settings,8100487800|z2,|z3,")]
	public async Task DeletePreservesContainerAndOtherPlans (string original, string expected)
		{
		RainPointScheduleSnapshot before = await Read (original);
		Discovery (original);
		_handler.Reply ("{\"code\":0}");
		await _client.DeleteTimerScheduleAsync (_hub, before, 0);
		Assert.That (JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (_handler.Requests.Last ().Body!)!.Parameter, Is.EqualTo (expected));
		}

	[TestCase ("parameter")]
	[TestCase ("firmware")]
	[TestCase ("device")]
	public async Task StaleSnapshotCannotOverwriteLaterChanges (string change)
		{
		RainPointScheduleSnapshot before = await Read ();
		Discovery (change == "parameter" ? EMPTY.Replace ("z3aux", "changed") : EMPTY, change == "firmware" ? "131" : "130", change == "device" ? 43 : 42);
		Assert.ThrowsAsync<RainPointException> (async () => await _client.AddTimerScheduleAsync (_hub, before, new RainPointIrrigationSchedule ()));
		Assert.That (_handler.Requests.Skip (1).All (request => request.Method == HttpMethod.Get), Is.True);
		}

	[TestCase ("119")]
	[TestCase ("")]
	[TestCase ("next")]
	public async Task UnknownFirmwareCannotBeWritten (string version)
		{
		RainPointScheduleSnapshot before = await Read (firmware: version);
		Assert.ThrowsAsync<NotSupportedException> (async () => await _client.AddTimerScheduleAsync (_hub, before, new RainPointIrrigationSchedule ()));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[Test]
	public async Task SeventhPlanIsRejectedBeforeNetwork ()
		{
		RainPointScheduleSnapshot before = await Read ("settings," + string.Join ("/", Enumerable.Repeat ("8000483c00", 6)) + ",,|z2,|z3,");
		Assert.ThrowsAsync<ArgumentException> (async () => await _client.AddTimerScheduleAsync (_hub, before, new RainPointIrrigationSchedule ()));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[TestCase (-1)]
	[TestCase (1)]
	public async Task InvalidIndexCannotTargetAnotherPlan (int index)
		{
		RainPointScheduleSnapshot before = await Read ("settings,8000483c00/,,|z2,|z3,");
		Assert.ThrowsAsync<ArgumentOutOfRangeException> (async () => await _client.DeleteTimerScheduleAsync (_hub, before, index));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[Test]
	public async Task UncertainWriteIsNeverReplayedUsingTheSameSnapshot ()
		{
		RainPointScheduleSnapshot before = await Read ();
		Discovery (EMPTY);
		_handler.Steps.Enqueue ((_, _) => throw new HttpRequestException ("Simulated lost response"));
		Assert.ThrowsAsync<HttpRequestException> (async () => await _client.AddTimerScheduleAsync (_hub, before, new RainPointIrrigationSchedule ()));
		Discovery (EMPTY);
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.AddTimerScheduleAsync (_hub, before, new RainPointIrrigationSchedule ()));
		Assert.That (_handler.Requests.Count (request => request.Path == "/app/device/sub/update"), Is.EqualTo (1));
		}

	[Test]
	public async Task CancellationBeforeWriteSendsNoUpdate ()
		{
		RainPointScheduleSnapshot before = await Read ();
		using CancellationTokenSource canceled = new ();
		canceled.Cancel ();
		Assert.CatchAsync<OperationCanceledException> (async () => await _client.AddTimerScheduleAsync (_hub, before, new RainPointIrrigationSchedule (), canceled.Token));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[TestCase (59)]
	[TestCase (43201)]
	[TestCase (60.5)]
	public void InvalidDurationFailsWithoutNetwork (double seconds)
		{
		Assert.Throws<ArgumentException> (() => Protocol.ScheduleEditor.Encode (new RainPointIrrigationSchedule { Duration = TimeSpan.FromSeconds (seconds) }));
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		}

	[Test]
	public void EncoderValidatesCalendarAndRecurrenceWithoutGuessing ()
		{
		using (Assert.EnterMultipleScope ())
			{
			Assert.Throws<ArgumentException> (() => Protocol.ScheduleEditor.Encode (new () { StartTime = TimeSpan.FromDays (1) }));
			Assert.Throws<ArgumentException> (() => Protocol.ScheduleEditor.Encode (new () { StartTime = TimeSpan.FromSeconds (1) }));
			Assert.Throws<NotSupportedException> (() => Protocol.ScheduleEditor.Encode (new () { Repeat = RainPointScheduleRepeat.Once }));
			Assert.Throws<ArgumentException> (() => Protocol.ScheduleEditor.Encode (new () { Repeat = RainPointScheduleRepeat.IntervalDays, Interval = 0 }));
			Assert.Throws<ArgumentException> (() => Protocol.ScheduleEditor.Encode (new () { Repeat = RainPointScheduleRepeat.Weekdays }));
			Assert.Throws<ArgumentException> (() => Protocol.ScheduleEditor.Encode (new () { Repeat = RainPointScheduleRepeat.Weekdays, Weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Monday } }));
			Assert.Throws<ArgumentException> (() => Protocol.ScheduleEditor.Encode (new () { EffectiveDate = new DateTime (2084, 1, 1) }));
			Assert.Throws<ArgumentException> (() => Protocol.ScheduleEditor.Encode (new () { EffectiveDate = DateTime.UtcNow.Date }));
			Assert.Throws<ArgumentException> (() => Protocol.ScheduleEditor.Encode (new () { WaterLimitLitres = 0.01m }));
			Assert.Throws<ArgumentException> (() => Protocol.ScheduleEditor.Encode (new () { WaterLimitLitres = 6553.6m }));
			Assert.Throws<NotSupportedException> (() => Protocol.ScheduleEditor.Encode (new () { Repeat = RainPointScheduleRepeat.IntervalHours }));
			}
		}

	[Test]
	public void EncoderUsesDateIntervalAndTenthsOfLitres ()
		{
		Assert.That (Protocol.ScheduleEditor.Encode (new RainPointIrrigationSchedule
			{
			Enabled = true,
			StartTime = new TimeSpan (8, 30, 0),
			Duration = TimeSpan.FromMinutes (10),
			Repeat = RainPointScheduleRepeat.IntervalDays,
			Interval = 3,
			WaterLimitLitres = 1.4m,
			EffectiveDate = new DateTime (2026, 9, 24)
			}), Is.EqualTo ("831e6a58020e00380d"));
		}
	[TestCase (false)]
	[TestCase (true)]
	public async Task CyclePlanCanBeAddedOrReplaceNormalPlanWithoutChangingOtherSettings (bool replace)
		{
		string original = replace ? EMPTY.Replace ("01020304,/,", "01020304,8000483c00/,") : EMPTY;
		RainPointScheduleSnapshot before = await Read (original);
		Discovery (original);
		_handler.Reply ("{\"code\":0}");
		RainPointCycleAndSoakSchedule plan = new ()
			{
			StartTime = new TimeSpan (23, 57, 0),
			EffectiveDate = new DateTime (2083, 12, 31)
			};
		if (replace)
			await _client.UpdateTimerScheduleAsync (_hub, before, 0, plan);
		else
			await _client.AddTimerScheduleAsync (_hub, before, plan);
		CapturedRequest request = _handler.Requests.Last ();
		string parameter = JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (request.Body!)!.Parameter;
		Assert.That (request.Path, Is.EqualTo ("/app/device/sub/update"));
		Assert.That (parameter, Is.EqualTo (EMPTY.Replace ("01020304,/,", "01020304,00f9cd0a0000009f7f05001e00/,")));
		RainPointSchedule decoded = Protocol.ScheduleDecoder.Decode (new RainPointDevice { Address = 2, PortNumber = 3, Parameter = parameter }, 1).Schedules.Single ();
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (decoded.Enabled, Is.False);
			Assert.That (decoded.Mode, Is.EqualTo (RainPointScheduleMode.CycleAndSoak));
			Assert.That (decoded.Duration, Is.EqualTo (TimeSpan.FromMinutes (10)));
			Assert.That (decoded.CycleWateringTime, Is.EqualTo (TimeSpan.FromMinutes (5)));
			Assert.That (decoded.CyclePauseTime, Is.EqualTo (TimeSpan.FromMinutes (30)));
			Assert.That (decoded.EffectiveDate, Is.EqualTo (plan.EffectiveDate));
			}
		}

	[Test]
	public void InvalidCyclePlanFailsBeforeDiscovery ()
		{
		Assert.Throws<ArgumentException> (() => _client.AddTimerScheduleAsync (_hub, null!, new RainPointCycleAndSoakSchedule { Duration = TimeSpan.FromMinutes (4) }));
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task MistingPlanCanBeAddedOrReplaceNormalPlanWithIndependentSecondTimings (bool replace)
		{
		string original = replace ? EMPTY.Replace ("01020304,/,", "01020304,8000483c00/,") : EMPTY;
		RainPointScheduleSnapshot before = await Read (original);
		Discovery (original);
		_handler.Reply ("{\"code\":0}");
		RainPointMistingSchedule plan = new ()
			{
			StartTime = new TimeSpan (23, 57, 0),
			EffectiveDate = new DateTime (2083, 12, 31)
			};
		if (replace)
			await _client.UpdateTimerScheduleAsync (_hub, before, 0, plan);
		else
			await _client.AddTimerScheduleAsync (_hub, before, plan);
		CapturedRequest request = _handler.Requests.Last ();
		string parameter = JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (request.Body!)!.Parameter;
		Assert.That (request.Path, Is.EqualTo ("/app/device/sub/update"));
		Assert.That (parameter, Is.EqualTo (EMPTY.Replace ("01020304,/,", "01020304,00f98d580200009f7f0a001400/,")));
		RainPointSchedule decoded = Protocol.ScheduleDecoder.Decode (new RainPointDevice { Address = 2, PortNumber = 3, Parameter = parameter }, 1).Schedules.Single ();
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (decoded.Enabled, Is.False);
			Assert.That (decoded.Mode, Is.EqualTo (RainPointScheduleMode.Misting));
			Assert.That (decoded.Duration, Is.EqualTo (TimeSpan.FromMinutes (10)));
			Assert.That (decoded.CycleWateringTime, Is.EqualTo (TimeSpan.FromSeconds (10)));
			Assert.That (decoded.CyclePauseTime, Is.EqualTo (TimeSpan.FromSeconds (20)));
			Assert.That (decoded.EffectiveDate, Is.EqualTo (plan.EffectiveDate));
			}
		}

	[Test]
	public void InvalidMistingPlanFailsBeforeDiscovery ()
		{
		Assert.Throws<ArgumentException> (() => _client.AddTimerScheduleAsync (_hub, null!, new RainPointMistingSchedule { CycleWateringTime = TimeSpan.FromSeconds (4) }));
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		}

	}
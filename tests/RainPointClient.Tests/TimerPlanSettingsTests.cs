using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class TimerPlanSettingsTests
	{
	private const string EMPTY = "58020a001e00008000004200fed7aabb,/,aux,646464646464646464646464,tail|z2,8000483c00/,z2aux,percent|z3,/,z3aux,other";
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

	[Test]
	public async Task ReadsExplicitMonthsAndExpiredRainDelayWithoutClaimingItIsActive ()
		{
		RainPointScheduleSnapshot snapshot = await Read ();
		Assert.That (snapshot.SeasonalAdjustmentAvailability, Is.EqualTo (TimerReadingAvailability.Decoded));
		Assert.That (snapshot.SeasonalPercentages, Is.EqualTo (Enumerable.Repeat (100, 12)));
		Assert.That (snapshot.SeasonalPercentages, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<int>> ());
		Assert.That (snapshot.RainDelayAvailability, Is.EqualTo (TimerReadingAvailability.Decoded));
		Assert.That (snapshot.RainDelayUntil, Is.EqualTo (new DateTime (2020, 1, 1)));
		Assert.That (snapshot.RainDelayUntil!.Value.Kind, Is.EqualTo (DateTimeKind.Unspecified));
		}

	[Test]
	public async Task SeasonWriteChangesOnlyTwelveMonthBytes ()
		{
		RainPointScheduleSnapshot before = await Read ();
		Discovery (EMPTY);
		_handler.Reply ("{\"code\":0}");
		int[] months = { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 150, 200 };
		await _client.SetTimerSeasonalAdjustmentAsync (_hub, before, months);
		Assert.That (JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (_handler.Requests.Last ().Body!)!.Parameter,
			Is.EqualTo (EMPTY.Replace ("646464646464646464646464", "0a141e28323c46505a6496c8")));
		}

	[Test]
	public async Task RainDelayWritePreservesSensorFlagsCalibrationPlansAndUnknownSuffix ()
		{
		RainPointScheduleSnapshot before = await Read ();
		Discovery (EMPTY);
		_handler.Reply ("{\"code\":0}");
		await _client.SetTimerRainDelayAsync (_hub, before, new DateTime (2026, 9, 25, 12, 34, 56));
		string parameter = JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (_handler.Requests.Last ().Body!)!.Parameter;
		Assert.That (parameter, Is.EqualTo (EMPTY.Replace ("00004200", "b8c8721a")));
		RainPointScheduleSnapshot after = await Read (parameter);
		Assert.That (after.RainDelayUntil, Is.EqualTo (new DateTime (2026, 9, 25, 12, 34, 56)));
		}

	[Test]
	public async Task ClearUsesTheObservedZeroField ()
		{
		string original = EMPTY.Replace ("00004200", "b8c8721a");
		RainPointScheduleSnapshot before = await Read (original);
		Discovery (original);
		_handler.Reply ("{\"code\":0}");
		await _client.SetTimerRainDelayAsync (_hub, before, null);
		Assert.That (JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (_handler.Requests.Last ().Body!)!.Parameter, Is.EqualTo (EMPTY.Replace ("00004200", "00000000")));
		}

	[TestCase (true)]
	[TestCase (false)]
	public async Task SettingsWritesUseTheSameStaleSnapshotGuard (bool season)
		{
		RainPointScheduleSnapshot before = await Read ();
		Discovery (EMPTY.Replace ("tail", "changed"));
		Assert.ThrowsAsync<RainPointException> (async () => { if (season) await _client.SetTimerSeasonalAdjustmentAsync (_hub, before, Enumerable.Repeat (90, 12).ToArray ()); else await _client.SetTimerRainDelayAsync (_hub, before, new DateTime (2026, 9, 25)); });
		Assert.That (_handler.Requests.Skip (1).All (request => request.Method == HttpMethod.Get), Is.True);
		}

	[Test]
	public async Task UncertainSettingsWriteCannotReuseSnapshot ()
		{
		RainPointScheduleSnapshot before = await Read ();
		Discovery (EMPTY);
		_handler.Steps.Enqueue ((_, _) => throw new HttpRequestException ("Simulated lost response"));
		Assert.ThrowsAsync<HttpRequestException> (async () => await _client.SetTimerSeasonalAdjustmentAsync (_hub, before, Enumerable.Repeat (90, 12).ToArray ()));
		Discovery (EMPTY);
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.SetTimerRainDelayAsync (_hub, before, new DateTime (2026, 9, 25)));
		Assert.That (_handler.Requests.Count (request => request.Path == "/app/device/sub/update"), Is.EqualTo (1));
		}

	[TestCase (9, 12)]
	[TestCase (201, 12)]
	[TestCase (100, 11)]
	[TestCase (100, 13)]
	public async Task InvalidMonthListsNeverSendRequests (int value, int count)
		{
		RainPointScheduleSnapshot before = await Read ();
		Assert.Throws<ArgumentException> (() => _client.SetTimerSeasonalAdjustmentAsync (_hub, before, Enumerable.Repeat (value, count).ToArray ()));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[TestCase (DateTimeKind.Utc)]
	[TestCase (DateTimeKind.Local)]
	public async Task RainDelayRequiresExplicitHomeLocalTime (DateTimeKind kind)
		{
		RainPointScheduleSnapshot before = await Read ();
		Assert.Throws<ArgumentException> (() => _client.SetTimerRainDelayAsync (_hub, before, new DateTime (2026, 9, 25, 12, 0, 0, kind)));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[TestCase ("", TimerReadingAvailability.NotReported)]
	[TestCase ("64", TimerReadingAvailability.Malformed)]
	[TestCase ("6464646464646464646464gg", TimerReadingAvailability.Malformed)]
	[TestCase ("096464646464646464646464", TimerReadingAvailability.Malformed)]
	[TestCase ("c96464646464646464646464", TimerReadingAvailability.Malformed)]
	public async Task MissingOrMalformedMonthsAreNotInvented (string encoded, TimerReadingAvailability availability)
		{
		RainPointScheduleSnapshot before = await Read (EMPTY.Replace ("646464646464646464646464", encoded));
		Assert.That (before.SeasonalAdjustmentAvailability, Is.EqualTo (availability));
		Assert.That (before.SeasonalPercentages, Is.Empty);
		Assert.Throws<NotSupportedException> (() => _client.SetTimerSeasonalAdjustmentAsync (_hub, before, Enumerable.Repeat (100, 12).ToArray ()));
		}

	[TestCase ("00000000", TimerReadingAvailability.Decoded)]
	[TestCase ("ffffffff", TimerReadingAvailability.Malformed)]
	[TestCase ("00003e18", TimerReadingAvailability.Malformed)] // Month zero.
	public async Task RainDelayUnknownDataIsNotPresentedAsClear (string encoded, TimerReadingAvailability availability)
		{
		RainPointScheduleSnapshot before = await Read (EMPTY.Replace ("00004200", encoded));
		Assert.That (before.RainDelayAvailability, Is.EqualTo (availability));
		Assert.That (before.RainDelayUntil, Is.Null);
		}

	[Test]
	public async Task SeasonRejectsAdjustedDurationBeyondTwelveHours ()
		{
		string parameter = EMPTY.Replace (",/,aux", ",000048c0a800000000/,aux");
		RainPointScheduleSnapshot before = await Read (parameter);
		Assert.Throws<ArgumentException> (() => _client.SetTimerSeasonalAdjustmentAsync (_hub, before, Enumerable.Repeat (200, 12).ToArray ()));
		}

	[Test]
	public async Task SeasonChecksCyclePausesAgainstRepeatSpacing ()
		{
		string record = Protocol.ScheduleEditor.EncodeCycleAndSoak (new ()
			{
			Duration = TimeSpan.FromHours (12),
			CycleWateringTime = TimeSpan.FromHours (12),
			CyclePauseTime = TimeSpan.FromMinutes (1)
			});
		RainPointScheduleSnapshot before = await Read (EMPTY.Replace (",/,aux", "," + record + "/,aux"));
		Assert.Throws<ArgumentException> (() => _client.SetTimerSeasonalAdjustmentAsync (_hub, before, Enumerable.Repeat (200, 12).ToArray ()));
		}
	[Test]
	public async Task FinalRepresentableDateRoundTripsWithoutSignedIntegerOverflow ()
		{
		RainPointScheduleSnapshot before = await Read ();
		string encoded = Protocol.TimerPlanSettings.EditRainDelay (before, new DateTime (2083, 12, 31, 23, 59, 59));
		RainPointScheduleSnapshot after = await Read (encoded);
		Assert.That (after.RainDelayUntil, Is.EqualTo (new DateTime (2083, 12, 31, 23, 59, 59)));
		}

	[TestCase (2019)]
	[TestCase (2084)]
	public async Task OutOfRangeYearCannotBeTruncated (int year)
		{
		RainPointScheduleSnapshot before = await Read ();
		Assert.Throws<ArgumentException> (() => _client.SetTimerRainDelayAsync (_hub, before, new DateTime (year, 1, 1)));
		}

	[Test]
	public async Task NoOpClearDoesNotPost ()
		{
		string original = EMPTY.Replace ("00004200", "00000000");
		RainPointScheduleSnapshot before = await Read (original);
		Discovery (original);
		await _client.SetTimerRainDelayAsync (_hub, before, null);
		Assert.That (_handler.Requests.Skip (1).All (request => request.Method == HttpMethod.Get), Is.True);
		}

	[Test]
	public async Task FractionalSecondCannotBeTruncated ()
		{
		RainPointScheduleSnapshot before = await Read ();
		Assert.Throws<ArgumentException> (() => _client.SetTimerRainDelayAsync (_hub, before, new DateTime (2026, 9, 25).AddTicks (1)));
		}

	}
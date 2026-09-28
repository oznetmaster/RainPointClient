using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class ZoneDefaultsTests
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
	public async Task ReadsKnownFieldsWithoutConvertingZeroToMadeUpStoredValues ()
		{
		var snapshot = await Read ();
		Assert.That (snapshot.ZoneDefaultsAvailability, Is.EqualTo (TimerReadingAvailability.Decoded));
		Assert.That (snapshot.ZoneDefaults!.WateringDuration, Is.EqualTo (TimeSpan.FromMinutes (10)));
		Assert.That (snapshot.ZoneDefaults.MistingRunTime, Is.EqualTo (TimeSpan.FromSeconds (10)));
		Assert.That (snapshot.ZoneDefaults.MistingInterval, Is.EqualTo (TimeSpan.FromSeconds (30)));
		snapshot = await Read (EMPTY.Replace ("58020a001e00", "000000000000"));
		Assert.That (snapshot.ZoneDefaultsAvailability, Is.EqualTo (TimerReadingAvailability.Decoded));
		Assert.That (snapshot.ZoneDefaults!.WateringDuration, Is.Null);
		Assert.That (snapshot.ZoneDefaults.MistingRunTime, Is.Null);
		Assert.That (snapshot.ZoneDefaults.MistingInterval, Is.Null);
		}

	[TestCase (60, "3c00")]
	[TestCase (43200, "c0a8")]
	[TestCase (-1, "0000")]
	public async Task DurationWritePreservesEveryUnrelatedByte (int seconds, string encoded)
		{
		var before = await Read ();
		Discovery (EMPTY);
		_handler.Reply ("{\"code\":0}");
		await _client.SetTimerDefaultWateringDurationAsync (_hub, before, seconds < 0 ? null : TimeSpan.FromSeconds (seconds));
		string parameter = JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (_handler.Requests.Last ().Body!)!.Parameter;
		Assert.That (parameter, Is.EqualTo (encoded + EMPTY.Substring (4)));
		Assert.That (_handler.Requests.Last ().Path, Is.EqualTo ("/app/device/sub/update"));
		}

	[TestCase (5, 3600, "0500100e")]
	[TestCase (3600, 5, "100e0500")]
	[TestCase (-1, -1, "00000000")]
	[TestCase (-1, 45, "00002d00")]
	public async Task MistingWritePreservesDurationPlansRainSensorCalibrationAndOtherZones (int run, int pause, string encoded)
		{
		var before = await Read ();
		Discovery (EMPTY);
		_handler.Reply ("{\"code\":0}");
		await _client.SetTimerMistingDefaultsAsync (_hub, before, run < 0 ? null : TimeSpan.FromSeconds (run), pause < 0 ? null : TimeSpan.FromSeconds (pause));
		Assert.That (JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (_handler.Requests.Last ().Body!)!.Parameter,
		 Is.EqualTo (EMPTY.Substring (0, 4) + encoded + EMPTY.Substring (12)));
		}

	[TestCase (0)]
	[TestCase (59)]
	[TestCase (61)]
	[TestCase (43260)]
	[TestCase (-1)]
	[TestCase (60.001)]
	public async Task InvalidDurationNeverSendsARequest (double seconds)
		{
		var before = await Read ();
		Assert.That (async () => await _client.SetTimerDefaultWateringDurationAsync (_hub, before, TimeSpan.FromSeconds (seconds)), Throws.TypeOf<ArgumentOutOfRangeException> ());
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[TestCase (0)]
	[TestCase (4)]
	[TestCase (3601)]
	[TestCase (-1)]
	[TestCase (5.001)]
	public async Task InvalidMistingTimesNeverSendARequest (double seconds)
		{
		var before = await Read ();
		Assert.That (async () => await _client.SetTimerMistingDefaultsAsync (_hub, before, TimeSpan.FromSeconds (seconds), TimeSpan.FromSeconds (30)), Throws.TypeOf<ArgumentOutOfRangeException> ());
		Assert.That (async () => await _client.SetTimerMistingDefaultsAsync (_hub, before, TimeSpan.FromSeconds (10), TimeSpan.FromSeconds (seconds)), Throws.TypeOf<ArgumentOutOfRangeException> ());
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[TestCase ("", TimerReadingAvailability.NotReported)]
	[TestCase ("5802", TimerReadingAvailability.Malformed)]
	[TestCase ("zz020a001e00008000004200", TimerReadingAvailability.Malformed)]
	[TestCase ("580204001e00008000004200", TimerReadingAvailability.Malformed)]
	[TestCase ("58020a00110e008000004200", TimerReadingAvailability.Malformed)]
	[TestCase ("01000a001e00008000004200", TimerReadingAvailability.Malformed)]
	[TestCase ("58020a001e00008000004200a", TimerReadingAvailability.Malformed)]
	public async Task IncompleteOrInvalidSettingsCannotBeEdited (string field, TimerReadingAvailability availability)
		{
		var before = await Read (field + EMPTY.Substring (EMPTY.IndexOf (',')));
		Assert.That (before.ZoneDefaultsAvailability, Is.EqualTo (availability));
		Assert.That (before.ZoneDefaults, Is.Null);
		Assert.That (async () => await _client.SetTimerDefaultWateringDurationAsync (_hub, before, TimeSpan.FromMinutes (5)), Throws.TypeOf<NotSupportedException> ());
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[Test]
	public async Task UnsupportedFirmwareIsReadOnly ()
		{
		var before = await Read (EMPTY, "119");
		Assert.That (before.ZoneDefaultsAvailability, Is.EqualTo (TimerReadingAvailability.Decoded));
		Assert.That (async () => await _client.SetTimerMistingDefaultsAsync (_hub, before, null, null), Throws.TypeOf<NotSupportedException> ());
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[Test]
	public async Task StaleSnapshotCannotOverwriteAnotherSettingsChange ()
		{
		var before = await Read ();
		Discovery (EMPTY.Replace ("aabb", "aacc"));
		Assert.That (async () => await _client.SetTimerDefaultWateringDurationAsync (_hub, before, TimeSpan.FromMinutes (5)), Throws.TypeOf<RainPointException> ());
		Assert.That (_handler.Requests.All (request => request.Path != "/app/device/sub/update"), Is.True);
		}

	[Test]
	public async Task UncertainWriteCannotBeReplayed ()
		{
		var before = await Read ();
		Discovery (EMPTY);
		_handler.Steps.Enqueue ((_, _) => throw new System.IO.IOException ("Synthetic transport failure"));
		Assert.That (async () => await _client.SetTimerMistingDefaultsAsync (_hub, before, TimeSpan.FromSeconds (15), TimeSpan.FromSeconds (45)), Throws.TypeOf<System.IO.IOException> ());
		Discovery (EMPTY);
		Assert.That (async () => await _client.SetTimerMistingDefaultsAsync (_hub, before, TimeSpan.FromSeconds (15), TimeSpan.FromSeconds (45)), Throws.InvalidOperationException);
		Assert.That (_handler.Requests.Count (request => request.Path == "/app/device/sub/update"), Is.EqualTo (1));
		}

	[Test]
	public async Task UnchangedValueSendsNoWrite ()
		{
		var before = await Read ();
		Discovery (EMPTY);
		await _client.SetTimerDefaultWateringDurationAsync (_hub, before, TimeSpan.FromMinutes (10));
		Assert.That (_handler.Requests, Has.Count.EqualTo (3));
		Assert.That (_handler.Requests.Last ().Method, Is.EqualTo (HttpMethod.Get));
		}

	[TestCase (2)]
	[TestCase (3)]
	public async Task OtherZoneEditsPreserveTheRemainingZones (int zone)
		{
		string port = EMPTY.Substring (0, EMPTY.IndexOf ('|'));
		string parameter = string.Join ("|", Enumerable.Repeat (port, 3));
		Discovery (parameter);
		var before = await _client.GetTimerSchedulesAsync (_hub, 2, zone);
		Discovery (parameter);
		_handler.Reply ("{\"code\":0}");
		await _client.SetTimerDefaultWateringDurationAsync (_hub, before, TimeSpan.FromMinutes (11));
		string[] expected = Enumerable.Repeat (port, 3).ToArray ();
		expected[zone - 1] = "9402" + port.Substring (4);
		Assert.That (JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (_handler.Requests.Last ().Body!)!.Parameter,
			Is.EqualTo (string.Join ("|", expected)));
		}

	[Test]
	public async Task LegacySecondsAreReadFaithfullyButCannotBeRewrittenAsNewMinuteDefaults ()
		{
		var before = await Read (EMPTY.Replace ("5802", "5902"));
		Assert.That (before.ZoneDefaults!.WateringDuration, Is.EqualTo (TimeSpan.FromSeconds (601)));
		Assert.That (async () => await _client.SetTimerDefaultWateringDurationAsync (_hub, before, before.ZoneDefaults.WateringDuration), Throws.TypeOf<ArgumentOutOfRangeException> ());
		}
	}
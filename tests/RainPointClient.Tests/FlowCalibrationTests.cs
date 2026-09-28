// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class FlowCalibrationTests
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

	[TestCase (-20, "ec")]
	[TestCase (-2, "fe")]
	[TestCase (0, "00")]
	[TestCase (20, "14")]
	public async Task ReadsSignedCorrectionIncludingNeutralZero (int percent, string encoded)
		{
		var before = await Read (EMPTY.Replace ("fed7", encoded + "d7"));
		Assert.That (before.FlowCalibrationAvailability, Is.EqualTo (TimerReadingAvailability.Decoded));
		Assert.That (before.FlowCalibrationPercent, Is.EqualTo (percent));
		}

	[TestCase (-20, "ec")]
	[TestCase (0, "00")]
	[TestCase (20, "14")]
	public async Task WritesOnlyCorrectionBytePreservingPressurePlansSensorsAndOtherZones (int percent, string encoded)
		{
		var before = await Read ();
		Discovery (EMPTY);
		_handler.Reply ("{\"code\":0}");
		await _client.SetTimerFlowCalibrationAsync (_hub, before, percent);
		Assert.That (JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (_handler.Requests.Last ().Body!)!.Parameter,
		 Is.EqualTo (EMPTY.Substring (0, 24) + encoded + EMPTY.Substring (26)));
		Assert.That (_handler.Requests.Last ().Path, Is.EqualTo ("/app/device/sub/update"));
		}

	[TestCase (-21)]
	[TestCase (21)]
	[TestCase (int.MinValue)]
	[TestCase (int.MaxValue)]
	public async Task InvalidPercentageNeverSendsARequest (int percent)
		{
		var before = await Read ();
		Assert.That (async () => await _client.SetTimerFlowCalibrationAsync (_hub, before, percent), Throws.TypeOf<ArgumentOutOfRangeException> ());
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[TestCase ("", TimerReadingAvailability.NotReported)]
	[TestCase ("58020a001e00008000004200", TimerReadingAvailability.NotReported)]
	[TestCase ("5802", TimerReadingAvailability.Malformed)]
	[TestCase ("58020a001e00008000004200fe", TimerReadingAvailability.Malformed)]
	[TestCase ("58020a001e00008000004200fed", TimerReadingAvailability.Malformed)]
	[TestCase ("58020a001e00008000004200fzd7", TimerReadingAvailability.Malformed)]
	[TestCase ("58020a001e0000800000420015d7", TimerReadingAvailability.Malformed)]
	[TestCase ("58020a001e00008000004200ebd7", TimerReadingAvailability.Malformed)]
	[TestCase ("58020a001e0000800000420000zz", TimerReadingAvailability.Malformed)]
	public async Task MissingOrUnrecognizedCalibrationIsNotInventedOrWritable (string field, TimerReadingAvailability availability)
		{
		var before = await Read (field + EMPTY.Substring (EMPTY.IndexOf (',')));
		Assert.That (before.FlowCalibrationAvailability, Is.EqualTo (availability));
		Assert.That (before.FlowCalibrationPercent, Is.Null);
		Assert.That (async () => await _client.SetTimerFlowCalibrationAsync (_hub, before, 1), Throws.TypeOf<NotSupportedException> ());
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[TestCase (1, 1, "normal", TimerReadingAvailability.UnsupportedFormat)]
	[TestCase (3, 1, "legacy=field", TimerReadingAvailability.UnsupportedFormat)]
	[TestCase (3, 1, "normal", TimerReadingAvailability.Malformed)]
	[TestCase (3, 4, EMPTY, TimerReadingAvailability.Malformed)]
	public void RejectsUnsupportedOrMalformedStructure (int ports, int zone, string parameter, TimerReadingAvailability availability)
		{
		var snapshot = new RainPointScheduleSnapshot (2, zone, TimerReadingAvailability.Decoded, Array.Empty<RainPointSchedule> ()) { PortNumber = ports, Parameter = parameter };
		Protocol.TimerFlowCalibration.Decode (snapshot);
		Assert.That (snapshot.FlowCalibrationAvailability, Is.EqualTo (availability));
		Assert.That (snapshot.FlowCalibrationPercent, Is.Null);
		}

	[Test]
	public async Task UnsupportedFirmwareIsReadOnly ()
		{
		var before = await Read (EMPTY, "119");
		Assert.That (before.FlowCalibrationPercent, Is.EqualTo (-2));
		Assert.That (async () => await _client.SetTimerFlowCalibrationAsync (_hub, before, 1), Throws.TypeOf<NotSupportedException> ());
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[Test]
	public async Task StaleSnapshotCannotOverwriteAnUnrelatedChange ()
		{
		var before = await Read ();
		Discovery (EMPTY.Replace ("aabb", "aacc"));
		Assert.That (async () => await _client.SetTimerFlowCalibrationAsync (_hub, before, 1), Throws.TypeOf<RainPointException> ());
		Assert.That (_handler.Requests.All (request => request.Path != "/app/device/sub/update"), Is.True);
		}

	[Test]
	public async Task UncertainWriteCannotBeReplayed ()
		{
		var before = await Read ();
		Discovery (EMPTY);
		_handler.Steps.Enqueue ((_, _) => throw new System.IO.IOException ("Synthetic transport failure"));
		Assert.That (async () => await _client.SetTimerFlowCalibrationAsync (_hub, before, 1), Throws.TypeOf<System.IO.IOException> ());
		Discovery (EMPTY);
		Assert.That (async () => await _client.SetTimerFlowCalibrationAsync (_hub, before, 1), Throws.InvalidOperationException);
		Assert.That (_handler.Requests.Count (request => request.Path == "/app/device/sub/update"), Is.EqualTo (1));
		}

	[Test]
	public async Task UnchangedValueSendsNoWrite ()
		{
		var before = await Read ();
		Discovery (EMPTY);
		await _client.SetTimerFlowCalibrationAsync (_hub, before, -2);
		Assert.That (_handler.Requests, Has.Count.EqualTo (3));
		Assert.That (_handler.Requests.Last ().Method, Is.EqualTo (HttpMethod.Get));
		}

	[TestCase (2)]
	[TestCase (3)]
	public async Task EditsSelectedZoneOnly (int zone)
		{
		string port = EMPTY.Substring (0, EMPTY.IndexOf ('|'));
		string parameter = string.Join ("|", Enumerable.Repeat (port, 3));
		Discovery (parameter);
		var before = await _client.GetTimerSchedulesAsync (_hub, 2, zone);
		Discovery (parameter);
		_handler.Reply ("{\"code\":0}");
		await _client.SetTimerFlowCalibrationAsync (_hub, before, 1);
		string[] expected = Enumerable.Repeat (port, 3).ToArray ();
		expected[zone - 1] = port.Substring (0, 24) + "01" + port.Substring (26);
		Assert.That (JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (_handler.Requests.Last ().Body!)!.Parameter, Is.EqualTo (string.Join ("|", expected)));
		}
	}
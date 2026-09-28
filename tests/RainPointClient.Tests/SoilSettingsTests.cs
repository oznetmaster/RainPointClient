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
public sealed class SoilSettingsTests
	{
	private const string Port = "58020a001e00038000004200fed7aabb,/,aux,646464646464646464646464,tail";
	private static readonly string EMPTY = string.Join ("|", Enumerable.Repeat (Port, 3));
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

	private void Discovery (string parameter, string firmware = "130", int id = 42, string sensor = "HCS021FRF")
		{
		_handler.Reply (JsonSerializer.Serialize (new
			{
			code = 0,
			data = new[] { new { mid = 101, model = "HWG023WBRF", deviceName = "hub", productKey = "product",
				subDevices = new object[] { new { sid = id, addr = 2, model = "HTV345FRF", portNumber = 3, softVer = firmware, param = parameter }, new { sid = 43, addr = 3, model = sensor } } } }
			}));
		}

	private async Task<RainPointScheduleSnapshot> Read (string? parameter = null, string firmware = "130", int zone = 1)
		{
		Discovery (parameter ?? EMPTY, firmware);
		return await _client.GetTimerSchedulesAsync (_hub, 2, zone);
		}

	[TestCase (1, 1)]
	[TestCase (2, 100)]
	[TestCase (3, -1)]
	public async Task MoistureWritePreservesFlagsPlansAndOtherZones (int zone, int percent)
		{
		var before = await Read (zone: zone);
		Discovery (EMPTY);
		_handler.Reply ("{\"code\":0}");
		await _client.SetTimerMoistureStopAsync (_hub, before, percent < 0 ? null : percent);
		string? actual = percent > 0 ? JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (_handler.Requests.Last ().Body!)!.Parameter : null;
		string[] zones = EMPTY.Split ('|');
		zones[zone - 1] = Port.Substring (0, 14) + (128 | Math.Max (0, percent)).ToString ("x2") + Port.Substring (16);
		if (percent > 0)
			Assert.That (actual, Is.EqualTo (string.Join ("|", zones)));
		else
			Assert.That (_handler.Requests.Last ().Method, Is.EqualTo (HttpMethod.Get), "Unchanged threshold is a no-op.");
		}
	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public async Task BindingAndUnbindingPreserveEveryOtherByte (int zone)
		{
		string original = EMPTY.Replace ("1e000380", "1e000080");
		var before = await Read (original, zone: zone);
		Discovery (original);
		Discovery (original);
		_handler.Reply ("{\"code\":0}");
		await _client.SetTimerSoilSensorAsync (_hub, before, 3);
		string[] parts = original.Split ('|');
		parts[zone - 1] = Port;
		string bound = string.Join ("|", parts);
		Assert.That (JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (_handler.Requests.Last ().Body!)!.Parameter, Is.EqualTo (bound));
		var after = await Read (bound, zone: zone);
		Discovery (bound);
		_handler.Reply ("{\"code\":0}");
		await _client.SetTimerSoilSensorAsync (_hub, after, null);
		Assert.That (JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (_handler.Requests.Last ().Body!)!.Parameter, Is.EqualTo (original));
		}
	[TestCase (0)]
	[TestCase (-1)]
	[TestCase (101)]
	public async Task BadThresholdIsRejectedWithoutNetwork (int percent)
		{
		var before = await Read ();
		int requests = _handler.Requests.Count;
		Assert.ThrowsAsync<ArgumentOutOfRangeException> (() => _client.SetTimerMoistureStopAsync (_hub, before, percent));
		Assert.That (_handler.Requests.Count, Is.EqualTo (requests));
		}
	[TestCase (0)]
	[TestCase (-1)]
	[TestCase (256)]
	public async Task BadAddressIsRejectedWithoutNetwork (int address)
		{
		var before = await Read ();
		int requests = _handler.Requests.Count;
		Assert.ThrowsAsync<ArgumentOutOfRangeException> (() => _client.SetTimerSoilSensorAsync (_hub, before, address));
		Assert.That (_handler.Requests.Count, Is.EqualTo (requests));
		}
	[Test]
	public async Task CannotEnableThresholdWithoutSensor ()
		{
		var before = await Read (EMPTY.Replace ("1e000380", "1e000080"));
		Assert.ThrowsAsync<InvalidOperationException> (() => _client.SetTimerMoistureStopAsync (_hub, before, 50));
		}
	[TestCase ("HCS005FRF")]
	[TestCase ("HCS021FRF")]
	[TestCase ("unknown")]
	public async Task OnlySupportedUnassignedSensorsAreOffered (string model)
		{
		string original = EMPTY.Replace ("1e000380", "1e000080");
		var before = await Read (original);
		Discovery (original, sensor: model);
		var available = await _client.GetAvailableSoilSensorsAsync (_hub, before);
		Assert.That (available.Count, Is.EqualTo (model == "unknown" ? 0 : 1));
		}
	[Test]
	public async Task SensorAlreadyUsedByAnotherZoneIsNotAssignable ()
		{
		var before = await Read ();
		Discovery (EMPTY);
		Assert.ThrowsAsync<ArgumentException> (() => _client.SetTimerSoilSensorAsync (_hub, before, 3));
		Assert.That (_handler.Requests.Count (r => r.Path == "/app/device/sub/update"), Is.Zero);
		}
	[TestCase ("58020a001e0003ff00004200fed7")]
	[TestCase ("58020a001e0003")]
	[TestCase ("58020a001e00038000004200fedX")]
	public async Task MalformedSensorFieldsCannotBeEdited (string field)
		{
		var before = await Read (EMPTY.Replace (Port.Split (',')[0], field));
		Assert.That (before.SoilSensorAvailability, Is.EqualTo (TimerReadingAvailability.Malformed));
		Assert.ThrowsAsync<NotSupportedException> (() => _client.SetTimerMoistureStopAsync (_hub, before, 50));
		}
	[Test]
	public async Task StaleAndUncertainWritesAreNotReplayed ()
		{
		var before = await Read ();
		Discovery (EMPTY.Replace ("aux", "changed"));
		Assert.ThrowsAsync<RainPointException> (() => _client.SetTimerMoistureStopAsync (_hub, before, 50));
		before = await Read ();
		Discovery (EMPTY);
		_handler.Steps.Enqueue ((_, _) => throw new HttpRequestException ("uncertain"));
		Assert.ThrowsAsync<HttpRequestException> (() => _client.SetTimerMoistureStopAsync (_hub, before, 50));
		Discovery (EMPTY);
		Assert.ThrowsAsync<InvalidOperationException> (() => _client.SetTimerMoistureStopAsync (_hub, before, 50));
		Assert.That (_handler.Requests.Count (r => r.Path == "/app/device/sub/update"), Is.EqualTo (1));
		}
	}
// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using NUnit.Framework;

using RainPointClient.Protocol;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class DeviceInformationTests
	{
	private ScriptedHandler _handler = null!;
	private HttpClient _http = null!;
	private RainPointCloudClient _client = null!;
	private static RainPointHub Hub () => new ()
		{
		HomeId = 42,
		Id = 101,
		Model = "HWG023WBRF-V2",
		DeviceName = "fixture-hub",
		ProductKey = "fixture-product",
		Devices = new[] { new RainPointDevice { Id = 9001, Address = 2, Model = "HTV345FRF" } }
		};

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

	[Test]
	public async Task DiscoveryCarriesMetadataWithoutPublicParameterPayload ()
		{
		_handler.Reply ("""{"code":0,"data":[{"mid":101,"deviceName":"fixture-hub","productKey":"fixture-product","model":"HWG023WBRF-V2","softVer":"1.2","mac":"fixture-mac","param":"keep|1||tail","subDevices":[{"sid":"9001","addr":2,"model":"HTV345FRF","softVer":"1.3"}]}]}""");
		RainPointHub hub = (await _client.GetHubsAsync (42)).Single ();
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (hub.HomeId, Is.EqualTo (42));
			Assert.That (hub.FirmwareVersion, Is.EqualTo ("1.2"));
			Assert.That (hub.MacAddress, Is.EqualTo ("fixture-mac"));
			Assert.That (hub.AutomaticTimeBroadcastEnabled, Is.True);
			Assert.That (hub.Devices[0].Id, Is.EqualTo (9001));
			Assert.That (hub.Devices[0].FirmwareVersion, Is.EqualTo ("1.3"));
			Assert.That (typeof (RainPointHub).GetProperty ("Parameter"), Is.Null);
			}
		}

	[Test]
	public async Task FirmwareChecksAddressHubAndTimerSeparately ()
		{
		_handler.Reply ("""{"code":0,"data":{"softVer":"1.0","info":null}}""");
		_handler.Reply ("""{"code":0,"data":{"softVer":"1.1","info":{"versionName":"1.2","mark":"Release notes"}}}""");
		RainPointFirmwareStatus hub = await _client.GetHubFirmwareAsync (Hub ());
		RainPointFirmwareStatus timer = await _client.GetTimerFirmwareAsync (Hub (), 2);
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (_handler.Requests[1].Path, Is.EqualTo ("/app/device/firmware/upgrade/info/v2?mid=101"));
			Assert.That (_handler.Requests[2].Path, Is.EqualTo ("/app/device/sub/firmware/upgrade/info?sid=9001"));
			Assert.That (hub.AvailableUpdate, Is.Null);
			Assert.That (timer.InstalledVersion, Is.EqualTo ("1.1"));
			Assert.That (timer.AvailableUpdate!.Version, Is.EqualTo ("1.2"));
			Assert.That (timer.AvailableUpdate.ReleaseNotes, Is.EqualTo ("Release notes"));
			}
		}

	[TestCase ("{}")]
	[TestCase ("{\"softVer\":\"1\"}")]
	[TestCase ("{\"softVer\":\"\",\"info\":null}")]
	[TestCase ("{\"softVer\":\"1\",\"info\":{}}")]
	[TestCase ("{\"softVer\":\"1\",\"info\":{\"versionName\":\"\"}}")]
	public async Task MalformedFirmwareIsNotReportedUpToDate (string data)
		{
		_handler.Reply ("{\"code\":0,\"data\":" + data + "}");
		await Assert.ThrowsAsync<RainPointException> (async () => await _client.GetHubFirmwareAsync (Hub ()));
		}

	[Test]
	public async Task HubStatusSeparatesWifiFromTimerRfSignal ()
		{
		_handler.Reply ("""{"code":0,"data":[{"mid":101,"status":[{"id":"connected","value":"1","time":1700000000000},{"id":"state","value":"0,-38"},{"id":"D02","value":"11#17E1C10019D800"}]}]}""");
		RainPointHubStatus status = await _client.GetHubStatusAsync (Hub ());
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (status.IsConnected, Is.True);
			Assert.That (status.WifiSignalStrengthDbm, Is.EqualTo (-38));
			Assert.That (status.LastConnectionChange, Is.EqualTo (DateTimeOffset.FromUnixTimeMilliseconds (1700000000000)));
			Assert.That (status.Timers.Single ().SignalStrengthDbm, Is.EqualTo (-63));
			Assert.That (status.Timers.Single ().Zones[0].IsOpen, Is.False);
			}
		}

	[Test]
	public async Task MissingHubReadingsStayUnknown ()
		{
		_handler.Reply ("""{"code":0,"data":[]}""");
		RainPointHubStatus status = await _client.GetHubStatusAsync (Hub ());
		Assert.That (status.IsConnected, Is.Null);
		Assert.That (status.WifiSignalStrengthDbm, Is.Null);
		Assert.That (status.Timers.Single ().Availability, Is.EqualTo (TimerReadingAvailability.NotReported));
		}

	[Test]
	public async Task AmbiguousConnectionReadingIsRejected ()
		{
		_handler.Reply ("""{"code":0,"data":[{"mid":101,"status":[{"id":"connected","value":"1"},{"id":"connected","value":"0"}]}]}""");
		await Assert.ThrowsAsync<RainPointException> (async () => await _client.GetHubStatusAsync (Hub ()));
		}

	[Test]
	public async Task BroadcastSettingReadsFreshAndPreservesUnknownFields ()
		{
		RainPointHub hub = Hub ();
		hub.Parameter = "stale|0|discard";
		_handler.Reply ("""{"code":0,"data":[{"mid":101,"deviceName":"fixture-hub","productKey":"fixture-product","model":"HWG023WBRF-V2","param":"keep|0|| trailing "}]}""");
		_handler.Reply ("""{"code":0}""");
		await _client.SetAutomaticTimeBroadcastAsync (hub, true);
		Assert.That (_handler.Requests[1].Path, Is.EqualTo ("/app/device/getDeviceByHid?hid=42"));
		Assert.That (_handler.Requests[2].Path, Is.EqualTo ("/app/device/main/update"));
		using JsonDocument body = JsonDocument.Parse (_handler.Requests[2].Body!);
		Assert.That (body.RootElement.GetProperty ("param").GetString (), Is.EqualTo ("keep|1|| trailing "));
		Assert.That (hub.Parameter, Is.EqualTo ("stale|0|discard"));
		}

	[TestCase (null)]
	[TestCase ("")]
	[TestCase ("missing")]
	[TestCase ("keep|unknown|tail")]
	public void UnknownBroadcastSettingsCannotBeSpliced (string? value) => Assert.That (HubSettings.SetBroadcast (value, true), Is.Null);

	[Test]
	public async Task BroadcastNowAddressesHubNotTimer ()
		{
		_handler.Reply ("""{"code":0}""");
		await _client.BroadcastTimeAsync (Hub ());
		using JsonDocument body = JsonDocument.Parse (_handler.Requests.Last ().Body!);
		Assert.That (body.RootElement.GetProperty ("addr").GetInt32 (), Is.Zero);
		Assert.That (body.RootElement.GetProperty ("mode").GetInt32 (), Is.Zero);
		Assert.That (body.RootElement.GetProperty ("duration").GetInt32 (), Is.Zero);
		}
	}
// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using RainPointClient.Desktop.Core;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class DashboardHubToolsTests
	{
	private const string Port = "58020a001e0000800000000000d7,/,aux,646464646464646464646464,tail";
	private static readonly string Parameter = string.Join ("|", Enumerable.Repeat (Port, 3));
	private ScriptedHandler _handler = null!;
	private HttpClient _http = null!;
	private Dashboard _dashboard = null!;

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
	private void Discovery (string? parameter = null, string firmware = "130", int? channel = 1) => _handler.Reply (JsonSerializer.Serialize (new
		{
		code = 0,
		data = new[]{new{mid=101,recich=channel,param=parameter??"7|0|suffix",name="Garden",deviceName="fixture",productKey="fixture",model="HWG023WBRF",subDevices=new[]
	{new{sid=42,addr=2,model="HTV345FRF",name="Timer",portNumber=3,softVer=firmware,param=parameter??Parameter}}}}
		}));
	private async Task SelectAsync ()
		{
		_handler.Reply ("""{"code":0,"data":{"token":"fixture","tokenExpired":3600}}""");
		_handler.Reply ("""{"code":0,"data":[{"hid":5,"homeName":"Garden"}]}""");
		await _dashboard.ConnectAsync ("fixture@example.invalid", "fixture", "44");
		Discovery ();
		await _dashboard.SelectHomeAsync (_dashboard.Homes.Single ());
		_dashboard.SelectHub (_dashboard.Hubs.Single ());
		_dashboard.SelectTimer (_dashboard.Timers.Single ());
		}
	private async Task LoadAsync (string? parameter = null, string firmware = "130")
		{
		Discovery (parameter, firmware);
		await _dashboard.LoadSettingsAsync ();
		}

	[TestCase (2)]
	[TestCase (3)]
	public async Task ChannelChangeVerifiesReadBackAndResetsOnSelection (int channel)
		{
		await SelectAsync ();
		Assert.That (_dashboard.CanSaveRfChannel, Is.False);
		Discovery ();
		await _dashboard.LoadHubSettingsAsync ();
		Assert.That (_dashboard.RfChannel, Is.EqualTo (1));
		_dashboard.RfChannel = channel;
		Assert.That (_dashboard.CanSaveRfChannel, Is.True);
		Discovery ();
		_handler.Reply ("{\"code\":0}");
		Discovery (channel: channel);
		await _dashboard.SaveRfChannelAsync ();
		Assert.That (_dashboard.SavedRfChannel, Is.EqualTo (channel.ToString ()));
		Assert.That (_dashboard.CanSaveRfChannel, Is.False);
		Assert.That (_dashboard.HubToolsMessage, Does.Contain ("matches the cloud read-back"));
		_dashboard.SelectHub (null);
		Assert.That (_dashboard.RfChannel, Is.Null);
		Assert.That (_dashboard.CanSelectRfChannel, Is.False);
		}
	[TestCase (null)]
	[TestCase (4)]
	public async Task UnknownChannelDoesNotPermitWriting (int? channel)
		{
		await SelectAsync ();
		Discovery (channel: channel);
		await _dashboard.LoadHubSettingsAsync ();
		_dashboard.RfChannel = 2;
		await _dashboard.SaveRfChannelAsync ();
		Assert.That (_dashboard.CanSaveRfChannel, Is.False);
		Assert.That (_handler.Requests, Has.Count.EqualTo (4));
		}
	[TestCase (false)]
	[TestCase (true)]
	public async Task FailedOrMismatchedChannelWriteCannotReplay (bool mismatch)
		{
		await SelectAsync ();
		Discovery ();
		await _dashboard.LoadHubSettingsAsync ();
		_dashboard.RfChannel = 2;
		Discovery ();
		if (mismatch)
			{
			_handler.Reply ("{\"code\":0}");
			Discovery ();
			}
		else
			_handler.Steps.Enqueue ((_, _) => throw new HttpRequestException ("private"));
		await _dashboard.SaveRfChannelAsync ();
		await _dashboard.SaveRfChannelAsync ();
		Assert.That (_dashboard.CanSaveRfChannel, Is.False);
		Assert.That (_dashboard.HubToolsMessage, Does.Contain ("no write was retried").And.Not.Contain ("private"));
		Assert.That (_handler.Requests.Count (x => x.Path == "/app/device/main/update"), Is.EqualTo (1));
		}
	[Test]
	public async Task SignedOutToolsCannotContactCloud ()
		{
		await _dashboard.LoadHubSettingsAsync ();
		await _dashboard.SaveBroadcastAsync ();
		await _dashboard.SaveRfChannelAsync ();
		await _dashboard.BroadcastTimeAsync ();
		await _dashboard.CheckFirmwareAsync (false);
		Assert.That (_handler.Requests, Is.Empty);
		}
	[TestCase (true)]
	[TestCase (false)]
	public async Task BroadcastSettingUsesFreshReadAndVerifiesReadBack (bool enabled)
		{
		await SelectAsync ();
		Discovery ();
		await _dashboard.LoadHubSettingsAsync ();
		_dashboard.AutomaticTimeBroadcast = enabled;
		Discovery ();
		_handler.Reply ("{\"code\":0}");
		Discovery (enabled ? "7|1|suffix" : "7|0|suffix");
		await _dashboard.SaveBroadcastAsync ();
		Assert.That (_dashboard.HubToolsMessage, Does.Contain ("matches the cloud read-back"));
		Assert.That (_handler.Requests.Single (x => x.Path == "/app/device/main/update").Body, Does.Contain (enabled ? "7|1|suffix" : "7|0|suffix"));
		Assert.That (_handler.Requests.Any (x => x.Path.Contains ("controlWorkMode")), Is.False);
		}
	[TestCase ("unknown")]
	[TestCase ("7|x|suffix")]
	public async Task UnreadableBroadcastCannotSave (string parameter)
		{
		await SelectAsync ();
		Discovery (parameter);
		await _dashboard.LoadHubSettingsAsync ();
		Assert.That (_dashboard.CanSaveBroadcast, Is.False);
		await _dashboard.SaveBroadcastAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (4));
		}
	[TestCase (false)]
	[TestCase (true)]
	public async Task FirmwareChecksAreReadOnlyAndPreserveOffer (bool timer)
		{
		await SelectAsync ();
		_handler.Reply ("{\"code\":0,\"data\":{\"softVer\":\"130\",\"info\":{\"versionName\":\"131\",\"mark\":\"Fixes\"}}}");
		await _dashboard.CheckFirmwareAsync (timer);
		Assert.That (timer ? _dashboard.TimerFirmware : _dashboard.HubFirmware, Does.Contain ("130").And.Contain ("131").And.Contain ("Fixes"));
		Assert.That (_handler.Requests.Last ().Method, Is.EqualTo (HttpMethod.Get));
		Assert.That (_handler.Requests.Last ().Path, Is.EqualTo (timer ? "/app/device/sub/firmware/upgrade/info?sid=42" : "/app/device/firmware/upgrade/info/v2?mid=101"));
		}
	[TestCase (0)]
	[TestCase (4)]
	public async Task OneShotTimeBroadcastAddressesHubOnly (int code)
		{
		await SelectAsync ();
		_handler.Reply ("{\"code\":" + code + "}");
		await _dashboard.BroadcastTimeAsync ();
		Assert.That (_handler.Requests.Last ().Body, Does.Contain ("\"addr\":0"));
		Assert.That (_dashboard.HubToolsMessage, Does.Contain ("RF delivery is not confirmed"));
		}
	[TestCase (false)]
	[TestCase (true)]
	public async Task UncertainOrMismatchedBroadcastNeedsReload (bool mismatch)
		{
		await SelectAsync ();
		Discovery ();
		await _dashboard.LoadHubSettingsAsync ();
		_dashboard.AutomaticTimeBroadcast = true;
		Discovery ();
		if (mismatch)
			{
			_handler.Reply ("{\"code\":0}");
			Discovery ();
			}
		else
			_handler.Steps.Enqueue ((_, _) => throw new HttpRequestException ("secret"));
		await _dashboard.SaveBroadcastAsync ();
		await _dashboard.SaveBroadcastAsync ();
		Assert.That (_dashboard.CanSaveBroadcast, Is.False);
		Assert.That (_dashboard.HubToolsMessage, Does.Contain ("no write was retried").And.Not.Contain ("secret"));
		Assert.That (_handler.Requests.Count (x => x.Path == "/app/device/main/update"), Is.EqualTo (1));
		}
	}
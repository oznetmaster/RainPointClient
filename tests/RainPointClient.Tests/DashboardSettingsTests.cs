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
public sealed class DashboardSettingsTests
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
	private void Discovery (string? parameter = null, string firmware = "130") => _handler.Reply (JsonSerializer.Serialize (new
		{
		code = 0,
		data = new[]{new{mid=101,name="Garden",deviceName="fixture",productKey="fixture",model="HWG023WBRF",subDevices=new[]
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

	[Test]
	public async Task SignedOutAndSelectionAloneNeverLoadOrWriteSettings ()
		{
		await _dashboard.LoadSettingsAsync ();
		await _dashboard.SaveSettingAsync ();
		Assert.That (_handler.Requests, Is.Empty);
		await SelectAsync ();
		await _dashboard.SaveSettingAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (3));
		Assert.That (_dashboard.SavedCalibration, Is.EqualTo ("Unknown"));
		Assert.That (_dashboard.CanReadSettings, Is.True);
		Assert.That (_dashboard.CanSaveSetting, Is.False);
		}

	[Test]
	public async Task LoadsSavedValuesWithoutAnyControlRequest ()
		{
		await SelectAsync ();
		await LoadAsync ();
		Assert.That (_dashboard.SavedDuration, Is.EqualTo ("10 minutes"));
		Assert.That (_dashboard.SavedMisting, Is.EqualTo ("On: 10 seconds · Off: 30 seconds"));
		Assert.That (_dashboard.SavedCalibration, Is.EqualTo ("0%"));
		Assert.That (_dashboard.SettingValue, Is.EqualTo ("10"));
		Assert.That (_dashboard.SettingsReadAt, Does.StartWith ("Zone 1"));
		Assert.That (_handler.Requests.Skip (1).All (x => x.Method == HttpMethod.Get), Is.True);
		}

	[Test]
	public async Task SentinelDefaultsAreDifferentFromMissingRecords ()
		{
		await SelectAsync ();
		await LoadAsync (Parameter.Replace ("58020a001e00", "000000000000"));
		Assert.That (_dashboard.SavedDuration, Is.EqualTo ("App default (10 minutes)"));
		Assert.That (_dashboard.SettingValue, Is.Empty);
		Assert.That (_dashboard.CanSaveSetting, Is.True);
		_dashboard.SettingChoice = "Misting intervals";
		Assert.That (_dashboard.SettingSecondValue, Is.Empty);
		Assert.That (_dashboard.SavedMisting, Does.Contain ("App default (30 seconds)"));
		await LoadAsync (string.Join ("|", Enumerable.Repeat (",/,aux,646464646464646464646464,tail", 3)));
		Assert.That (_dashboard.SavedDuration, Is.EqualTo ("Not reported"));
		Assert.That (_dashboard.SavedCalibration, Is.EqualTo ("Not reported"));
		Assert.That (_dashboard.CanSaveSetting, Is.False);
		}

	[TestCase ("Default duration", "0", "")]
	[TestCase ("Default duration", "721", "")]
	[TestCase ("Default duration", "1.5", "")]
	[TestCase ("Misting intervals", "4", "30")]
	[TestCase ("Misting intervals", "10", "3601")]
	[TestCase ("Misting intervals", "", "no")]
	[TestCase ("Flow calibration", "", "")]
	[TestCase ("Flow calibration", "-21", "")]
	[TestCase ("Flow calibration", "21", "")]
	[TestCase ("Flow calibration", "1.2", "")]
	public async Task InvalidInputNeverReachesTheCloud (string kind, string first, string second)
		{
		await SelectAsync ();
		await LoadAsync ();
		_dashboard.SettingChoice = kind;
		_dashboard.SettingValue = first;
		_dashboard.SettingSecondValue = second;
		Assert.That (_dashboard.CanSaveSetting, Is.False);
		await _dashboard.SaveSettingAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (4));
		}

	[TestCase (1, "Default duration", "11", "", "94020a001e0000800000000000d7")]
	[TestCase (2, "Default duration", "11", "", "94020a001e0000800000000000d7")]
	[TestCase (3, "Default duration", "11", "", "94020a001e0000800000000000d7")]
	[TestCase (1, "Default duration", "", "", "00000a001e0000800000000000d7")]
	[TestCase (2, "Default duration", "", "", "00000a001e0000800000000000d7")]
	[TestCase (3, "Default duration", "", "", "00000a001e0000800000000000d7")]
	[TestCase (1, "Misting intervals", "15", "45", "58020f002d0000800000000000d7")]
	[TestCase (2, "Misting intervals", "15", "45", "58020f002d0000800000000000d7")]
	[TestCase (3, "Misting intervals", "15", "45", "58020f002d0000800000000000d7")]
	[TestCase (1, "Misting intervals", "", "", "58020000000000800000000000d7")]
	[TestCase (2, "Misting intervals", "", "", "58020000000000800000000000d7")]
	[TestCase (3, "Misting intervals", "", "", "58020000000000800000000000d7")]
	[TestCase (1, "Flow calibration", "-20", "", "58020a001e00008000000000ecd7")]
	[TestCase (2, "Flow calibration", "-20", "", "58020a001e00008000000000ecd7")]
	[TestCase (3, "Flow calibration", "-20", "", "58020a001e00008000000000ecd7")]
	[TestCase (1, "Flow calibration", "+20", "", "58020a001e0000800000000014d7")]
	[TestCase (2, "Flow calibration", "+20", "", "58020a001e0000800000000014d7")]
	[TestCase (3, "Flow calibration", "+20", "", "58020a001e0000800000000014d7")]
	public async Task SaveEditsOnlySelectedZoneAndVerifiesReadBack (int zone, string kind, string first, string second, string settings)
		{
		await SelectAsync ();
		_dashboard.SettingsZone = zone;
		await LoadAsync ();
		_dashboard.SettingChoice = kind;
		_dashboard.SettingValue = first;
		_dashboard.SettingSecondValue = second;
		string[] ports = Parameter.Split ('|');
		ports[zone - 1] = settings + Port.Substring (Port.IndexOf (','));
		string expected = string.Join ("|", ports);
		Discovery ();
		_handler.Reply ("{\"code\":0}");
		Discovery (expected);
		await _dashboard.SaveSettingAsync ();
		Assert.That (_dashboard.SettingsMessage, Does.Contain ("matches the cloud read-back"));
		var writes = _handler.Requests.Where (x => x.Path == "/app/device/sub/update").ToArray ();
		Assert.That (writes, Has.Length.EqualTo (1));
		Assert.That (JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (writes[0].Body!)!.Parameter, Is.EqualTo (expected));
		Assert.That (_handler.Requests.Any (x => x.Path.Contains ("controlWorkMode")), Is.False);
		Assert.That (_dashboard.CanSaveSetting, Is.True);
		Assert.That (_dashboard.SettingsReadAt, Does.StartWith ("Zone " + zone));
		}

	[TestCase (2)]
	[TestCase (3)]
	public async Task SwitchingZoneClearsDraftUntilReload (int zone)
		{
		await SelectAsync ();
		await LoadAsync ();
		_dashboard.SettingValue = "11";
		_dashboard.SettingsZone = zone;
		Assert.That (_dashboard.SavedDuration, Is.EqualTo ("Unknown"));
		Assert.That (_dashboard.SettingValue, Is.Empty);
		await LoadAsync ();
		Assert.That (_dashboard.SettingsReadAt, Does.StartWith ("Zone " + zone));
		Assert.That (_dashboard.SavedDuration, Is.EqualTo ("10 minutes"));
		Assert.That (_dashboard.CanEditSetting, Is.True);
		}

	[Test]
	public async Task ChangingSettingDiscardsOnlyDraftAndDoesNotSendRequests ()
		{
		await SelectAsync ();
		await LoadAsync ();
		_dashboard.SettingValue = "11";
		_dashboard.SettingChoice = "Flow calibration";
		Assert.That (_dashboard.SettingValue, Is.EqualTo ("0"));
		_dashboard.SettingChoice = "Default duration";
		Assert.That (_dashboard.SettingValue, Is.EqualTo ("10"));
		Assert.That (_handler.Requests, Has.Count.EqualTo (4));
		}

	[Test]
	public async Task StaleSnapshotRequiresReloadWithoutWriting ()
		{
		await SelectAsync ();
		await LoadAsync ();
		_dashboard.SettingValue = "11";
		Discovery (Parameter.Replace ("tail", "changed"));
		await _dashboard.SaveSettingAsync ();
		Assert.That (_dashboard.SettingsMessage, Does.Contain ("Reload before another attempt"));
		Assert.That (_dashboard.CanSaveSetting, Is.False);
		await _dashboard.SaveSettingAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (5));
		Assert.That (_handler.Requests.Any (x => x.Path == "/app/device/sub/update"), Is.False);
		}

	[Test]
	public async Task UncertainWriteIsNotRetriedAndRequiresReload ()
		{
		await SelectAsync ();
		await LoadAsync ();
		_dashboard.SettingValue = "11";
		Discovery ();
		_handler.Steps.Enqueue ((_, _) => throw new System.IO.IOException ("private details"));
		await _dashboard.SaveSettingAsync ();
		await _dashboard.SaveSettingAsync ();
		Assert.That (_handler.Requests.Count (x => x.Path == "/app/device/sub/update"), Is.EqualTo (1));
		Assert.That (_dashboard.SettingsMessage, Does.Contain ("outcome may be unknown").And.Not.Contain ("private"));
		Assert.That (_dashboard.SavedDuration, Is.EqualTo ("10 minutes"));
		Assert.That (_dashboard.CanSaveSetting, Is.False);
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task AcceptanceWithoutMatchingReadBackIsNotSuccess (bool readFails)
		{
		await SelectAsync ();
		await LoadAsync ();
		_dashboard.SettingValue = "11";
		Discovery ();
		_handler.Reply ("{\"code\":0}");
		if (readFails)
			_handler.Steps.Enqueue ((_, _) => throw new HttpRequestException ("private details"));
		else
			Discovery ();
		await _dashboard.SaveSettingAsync ();
		Assert.That (_dashboard.SettingsMessage, Does.Contain ("Cloud accepted").And.Contain ("not retried").And.Not.Contain ("private"));
		Assert.That (_dashboard.CanSaveSetting, Is.False);
		Assert.That (_dashboard.SavedDuration, Is.EqualTo ("10 minutes"));
		await _dashboard.SaveSettingAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (7));
		}

	[Test]
	public async Task FailedReloadClearsPreviouslyWritableSnapshot ()
		{
		await SelectAsync ();
		await LoadAsync ();
		_handler.Steps.Enqueue ((_, _) => throw new HttpRequestException ("private details"));
		await _dashboard.LoadSettingsAsync ();
		Assert.That (_dashboard.CanSaveSetting, Is.False);
		Assert.That (_dashboard.SavedDuration, Is.EqualTo ("Unknown"));
		Assert.That (_dashboard.SettingsMessage, Does.Contain ("could not be read").And.Not.Contain ("private"));
		}

	[Test]
	public async Task SelectionAndSignOutDiscardSettings ()
		{
		await SelectAsync ();
		await LoadAsync ();
		var timer = _dashboard.SelectedTimer;
		_dashboard.SelectTimer (null);
		Assert.That (_dashboard.CanSaveSetting, Is.False);
		Assert.That (_dashboard.SavedCalibration, Is.EqualTo ("Unknown"));
		_dashboard.SelectTimer (timer);
		await LoadAsync ();
		_handler.Reply ("{\"code\":0}");
		await _dashboard.DisconnectAsync ();
		Assert.That (_dashboard.CanReadSettings, Is.False);
		Assert.That (_dashboard.SavedCalibration, Is.EqualTo ("Unknown"));
		}

	[Test]
	public async Task BusyReadBlocksEditsSelectionAndCloseCancels ()
		{
		await SelectAsync ();
		_handler.Steps.Enqueue (async (_, token) => { await Task.Delay (Timeout.Infinite, token); throw new InvalidOperationException ("Unreachable"); });
		Task read = _dashboard.LoadSettingsAsync ();
		Assert.That (_dashboard.IsBusy, Is.True);
		_dashboard.SettingsZone = 2;
		_dashboard.SettingChoice = "Flow calibration";
		_dashboard.SettingValue = "11";
		await _dashboard.SaveSettingAsync ();
		await _dashboard.CloseAsync ();
		await read;
		Assert.That (_dashboard.SettingsZone, Is.EqualTo (1));
		Assert.That (_dashboard.SettingChoice, Is.EqualTo ("Default duration"));
		Assert.That (_dashboard.SettingValue, Is.Empty);
		Assert.That (_dashboard.CanSaveSetting, Is.False);
		Assert.That (_handler.Requests, Has.Count.EqualTo (4));
		}
	[TestCase (1, false)]
	[TestCase (2, false)]
	[TestCase (3, false)]
	[TestCase (1, true)]
	[TestCase (2, true)]
	[TestCase (3, true)]
	public async Task SeasonalAndRainDelayEditsPreserveOtherZones (int zone, bool rain)
		{
		await SelectAsync ();
		_dashboard.SettingsZone = zone;
		await LoadAsync ();
		_dashboard.SettingChoice = rain ? "Rain delay" : "Seasonal adjustment";
		if (rain)
			_dashboard.SettingValue = "2020-01-01 00:00:00";
		else
			for (int i = 0; i < 12; i++)
				_dashboard.SeasonMonths[i].Value = (10 * (i + 1)).ToString ();
		string[] ports = Parameter.Split ('|');
		ports[zone - 1] = rain ? Port.Replace ("58020a001e0000800000000000d7", "58020a001e0000800000420000d7") : Port.Replace ("646464646464646464646464", "0a141e28323c46505a646e78");
		string expected = string.Join ("|", ports);
		Discovery ();
		_handler.Reply ("{\"code\":0}");
		Discovery (expected);
		Assert.That (_dashboard.CanSaveSetting, Is.True);
		await _dashboard.SaveSettingAsync ();
		Assert.That (_dashboard.SettingsMessage, Does.Contain ("matches the cloud read-back"));
		Assert.That (JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (_handler.Requests.Single (x => x.Path == "/app/device/sub/update").Body!)!.Parameter, Is.EqualTo (expected));
		Assert.That (_dashboard.SettingsReadAt, Does.StartWith ("Zone " + zone));
		Assert.That (rain ? _dashboard.SavedRainDelay : _dashboard.SavedSeasonal, Does.Contain (rain ? "2020-01-01" : "December 120%"));
		}
	[TestCase ("Rain delay", "2026-02-30 12:00:00")]
	[TestCase ("Rain delay", "2026-01-01T12:00:00Z")]
	[TestCase ("Rain delay", "2084-01-01 00:00:00")]
	[TestCase ("Rain delay", "2019-12-31 23:59:59")]
	[TestCase ("Seasonal adjustment", "9")]
	[TestCase ("Seasonal adjustment", "201")]
	[TestCase ("Seasonal adjustment", "1.5")]
	[TestCase ("Seasonal adjustment", "")]
	public async Task InvalidSeasonalOrRainDelayCannotSave (string choice, string value)
		{
		await SelectAsync ();
		await LoadAsync ();
		_dashboard.SettingChoice = choice;
		_dashboard.SettingValue = value;
		_dashboard.SeasonMonths[11].Value = value;
		Assert.That (_dashboard.CanSaveSetting, Is.False);
		await _dashboard.SaveSettingAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (4));
		}
	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public async Task BlankRainDelayClearsOnlySelectedZone (int zone)
		{
		await SelectAsync ();
		_dashboard.SettingsZone = zone;
		string before = Parameter.Replace ("58020a001e0000800000000000d7", "58020a001e0000800000420000d7");
		await LoadAsync (before);
		_dashboard.SettingChoice = "Rain delay";
		_dashboard.SettingValue = "";
		var ports = before.Split ('|');
		ports[zone - 1] = Port;
		string expected = string.Join ("|", ports);
		Discovery (before);
		_handler.Reply ("{\"code\":0}");
		Discovery (expected);
		await _dashboard.SaveSettingAsync ();
		Assert.That (_dashboard.SavedRainDelay, Is.EqualTo ("Cleared"));
		Assert.That (_dashboard.SettingsMessage, Does.Contain ("matches the cloud read-back"));
		Assert.That (JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (_handler.Requests.Single (x => x.Path == "/app/device/sub/update").Body!)!.Parameter, Is.EqualTo (expected));
		}
	[Test]
	public async Task MissingMonthsDoNotDisableReadableRainDelay ()
		{
		await SelectAsync ();
		await LoadAsync (Parameter.Replace ("646464646464646464646464", ""));
		_dashboard.SettingChoice = "Seasonal adjustment";
		Assert.That (_dashboard.CanSaveSetting, Is.False);
		Assert.That (_dashboard.SavedSeasonal, Is.EqualTo ("Not reported"));
		_dashboard.SettingChoice = "Rain delay";
		Assert.That (_dashboard.CanSaveSetting, Is.True);
		}

	}
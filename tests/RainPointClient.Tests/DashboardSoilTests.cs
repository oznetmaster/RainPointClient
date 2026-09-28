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
public sealed class DashboardSoilTests
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
		data = new[]{new{mid=101,name="Garden",deviceName="fixture",productKey="fixture",model="HWG023WBRF",subDevices=new object[]
	{new{sid=42,addr=2,model="HTV345FRF",name="Timer",portNumber=3,softVer=firmware,param=parameter??Parameter},new{sid=43,addr=3,model="HCS021FRF",name="Soil"}}}}
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

	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public async Task FailedAssociationConsumesDraftUntilExplicitReload (int zone)
		{
		await SelectAsync ();
		_dashboard.SettingsZone = zone;
		await LoadAsync ();
		Discovery ();
		await _dashboard.LoadSoilSensorsAsync ();
		_dashboard.SelectedSoilSensor = _dashboard.SoilSensors.Single (s => s.Address == 3);
		Discovery ();
		Discovery ();
		_handler.Steps.Enqueue ((_, _) => throw new HttpRequestException ("uncertain"));
		await _dashboard.SaveSoilSensorAsync ();
		Assert.That (_dashboard.CanSaveSoilSensor, Is.False);
		Assert.That (_dashboard.SoilSensors, Is.Empty);
		Assert.That (_dashboard.SettingsMessage, Does.Contain ("unknown"));
		int count = _handler.Requests.Count;
		await _dashboard.SaveSoilSensorAsync ();
		Assert.That (_handler.Requests.Count, Is.EqualTo (count));
		}
	[Test]
	public async Task SelectionChangeClearsPairingChoices ()
		{
		await SelectAsync ();
		await LoadAsync ();
		Discovery ();
		await _dashboard.LoadSoilSensorsAsync ();
		Assert.That (_dashboard.SoilSensors.Count, Is.EqualTo (2));
		_dashboard.SettingsZone = 2;
		Assert.That (_dashboard.SoilSensors, Is.Empty);
		Assert.That (_dashboard.SelectedSoilSensor, Is.Null);
		Assert.That (_dashboard.SavedSoilSettings, Is.EqualTo ("Unknown"));
		}
	[Test]
	public async Task MoistureCannotBeEnabledWithoutAnAssociatedSensor ()
		{
		await SelectAsync ();
		await LoadAsync ();
		_dashboard.SettingChoice = "Moisture stop";
		_dashboard.SettingValue = "60";
		Assert.That (_dashboard.CanSaveSetting, Is.False);
		_dashboard.SettingValue = "";
		Assert.That (_dashboard.CanSaveSetting, Is.True);
		}
	[Test]
	public async Task FailedChoiceReadDoesNotKeepOldChoices ()
		{
		await SelectAsync ();
		await LoadAsync ();
		Discovery ();
		await _dashboard.LoadSoilSensorsAsync ();
		_handler.Steps.Enqueue ((_, _) => throw new HttpRequestException ("offline"));
		await _dashboard.LoadSoilSensorsAsync ();
		Assert.That (_dashboard.SoilSensors, Is.Empty);
		Assert.That (_dashboard.CanSaveSoilSensor, Is.False);
		}
	}
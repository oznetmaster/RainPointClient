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
public sealed class DashboardMoistureRuleTests
	{
	private const string Port = "58020a001e0000800000000000d7,/,,646464646464646464646464,tail";
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
		await _dashboard.LoadMoistureRuleAsync ();
		}

	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public async Task UncertainAutomaticRuleWriteRequiresReloadAndIsNotReplayed (int zone)
		{
		await SelectAsync ();
		_dashboard.MoistureRuleZone = zone;
		await LoadAsync ();
		Assert.That (_dashboard.MoistureRuleEnabled, Is.False);
		Discovery ();
		_handler.Steps.Enqueue ((_, _) => throw new HttpRequestException ("uncertain"));
		await _dashboard.SaveMoistureRuleAsync ();
		Assert.That (_dashboard.CanSaveMoistureRule, Is.False);
		Assert.That (_dashboard.MoistureRuleMessage, Does.Contain ("unknown"));
		int count = _handler.Requests.Count;
		await _dashboard.SaveMoistureRuleAsync ();
		Assert.That (_handler.Requests.Count, Is.EqualTo (count));
		}
	[Test]
	public async Task ZoneAndAccountChangesDiscardRuleDraft ()
		{
		await SelectAsync ();
		await LoadAsync ();
		_dashboard.MoistureBelow = "50";
		_dashboard.MoistureRuleZone = 2;
		Assert.That (_dashboard.CanSaveMoistureRule, Is.False);
		Assert.That (_dashboard.MoistureRuleEnabled, Is.False);
		Assert.That (_dashboard.MoistureBelow, Is.EqualTo ("30"));
		}
	[TestCase ("0", "10", "", "", "")]
	[TestCase ("30", "", "", "", "")]
	[TestCase ("30", "10", "1.45", "", "")]
	[TestCase ("30", "10", "", "22:00", "")]
	[TestCase ("30", "10", "", "00:00", "23:59")]
	public async Task InvalidRuleInputsCannotBeSaved (string threshold, string minutes, string litres, string from, string until)
		{
		await SelectAsync ();
		await LoadAsync ();
		_dashboard.MoistureBelow = threshold;
		_dashboard.MoistureMinutes = minutes;
		_dashboard.MoistureLitres = litres;
		_dashboard.MoistureFrom = from;
		_dashboard.MoistureUntil = until;
		Assert.That (_dashboard.CanSaveMoistureRule, Is.False);
		int count = _handler.Requests.Count;
		await _dashboard.SaveMoistureRuleAsync ();
		Assert.That (_handler.Requests.Count, Is.EqualTo (count));
		}
	[Test]
	public async Task ExclusionTimesRoundTripIntoDraft ()
		{
		await SelectAsync ();
		await LoadAsync (Parameter.Replace (",/,,", ",/,28010b1858020000,"));
		Assert.That (_dashboard.MoistureFrom, Is.EqualTo ("22:00"));
		Assert.That (_dashboard.MoistureUntil, Is.EqualTo ("06:00"));
		Assert.That (_dashboard.CanSaveMoistureRule, Is.True);
		}
	}
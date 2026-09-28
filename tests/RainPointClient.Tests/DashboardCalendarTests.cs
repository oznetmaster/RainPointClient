using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using RainPointClient.Desktop.Core;
using RainPointClient.Protocol;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class DashboardCalendarTests
	{
	private const string Empty = "58020a001e0000800000000000d7,/,aux,646464646464646464646464,tail|z2,8000483c00/,z2aux,percent|z3,/,z3aux,other";
	private string _parameter = Empty;
	private ScriptedHandler _handler = null!; private HttpClient _http = null!; private Dashboard _dashboard = null!;
	[SetUp]
	public void SetUp ()
		{
		_parameter = Empty;
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
	private static string Discovery (string parameter) => JsonSerializer.Serialize (new { code = 0, data = new[] { new { mid = 101, model = "HWG023WBRF", deviceName = "hub", productKey = "product", subDevices = new[] { new { sid = 42, addr = 2, model = "HTV345FRF", portNumber = 3, softVer = "130", param = parameter } } } } });
	private async Task SelectAsync ()
		{
		_handler.Reply ("""{"code":0,"data":{"token":"fixture","tokenExpired":3600}}""");
		_handler.Reply ("""{"code":0,"data":[{"hid":5,"homeName":"Garden"}]}""");
		await _dashboard.ConnectAsync ("fixture@example.invalid", "fixture", "44");
		_handler.Reply (Discovery (_parameter));
		await _dashboard.SelectHomeAsync (_dashboard.Homes.Single ());
		_dashboard.SelectHub (_dashboard.Hubs.Single ());
		_dashboard.SelectTimer (_dashboard.Timers.Single ());
		}

	private async Task LoadCalendar ()
		{
		_dashboard.CalendarDate = new DateTime (2026, 9, 24);
		_handler.Reply (Discovery (_parameter));
		await _dashboard.LoadCalendarAsync ();
		}
	[TestCase (true)]
	[TestCase (false)]
	public async Task IntervalCalendarUsesReportedHomeRulesOrRemainsUnknown (bool reported)
		{
		string record = ScheduleEditor.Encode (new RainPointIrrigationSchedule { Enabled = true, Repeat = RainPointScheduleRepeat.IntervalDays, Interval = 2, EffectiveDate = new DateTime (2026, 9, 27), StartTime = new TimeSpan (23, 39, 0), Duration = TimeSpan.FromMinutes (1) });
		_parameter = Empty.Replace (",/,aux", "," + record + "/,aux");
		await SelectAsync ();
		_dashboard.CalendarDate = new DateTime (2026, 10, 26);
		_handler.Reply (Discovery (_parameter));
		_handler.Reply (JsonSerializer.Serialize (new
			{
			code = 0,
			data = new
				{
				hid = 5,
				homeName = "Garden",
				zoneOffset = 0,
				zoneDst = reported ? CalendarTimeZoneTests.LondonRules () : null
				}
			}));
		await _dashboard.LoadCalendarAsync ();
		int count = _handler.Requests.Count;
		if (reported)
			{
			Assert.That (_dashboard.CalendarRows, Has.Count.EqualTo (1));
			Assert.That (_dashboard.CalendarNext, Does.Contain ("2026-10-26 23:39"));
			_dashboard.CalendarDate = new DateTime (2026, 10, 27);
			Assert.That (_dashboard.CalendarRows, Is.Empty);
			Assert.That (_dashboard.CalendarNext, Does.Contain ("2026-10-28 23:39"));
			}
		else
			{
			Assert.That (_dashboard.CalendarRows, Is.Empty);
			Assert.That (_dashboard.CalendarNext, Does.Contain ("unknown"));
			Assert.That (_dashboard.CalendarMessage, Does.Contain ("timezone rules"));
			}
		Assert.That (_handler.Requests.Count, Is.EqualTo (count), "Date navigation remains local.");
		}

	[Test]
	public async Task SignedOutCalendarAndDateNavigationHaveNoNetworkSideEffects ()
		{
		_dashboard.CalendarDate = new DateTime (2026, 9, 24);
		await _dashboard.LoadCalendarAsync ();
		Assert.That (_handler.Requests, Is.Empty);
		Assert.That (_dashboard.CalendarRows, Is.Empty);
		Assert.That (_dashboard.CalendarNext, Does.Contain ("unknown"));
		}
	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public async Task FailedReloadClearsOldRowsAndDateNavigationCannotResurrectThem (int zone)
		{
		_parameter = string.Join ("|", Enumerable.Repeat (Empty.Split ('|')[0].Replace (",/,aux", ",80004a3c0000000000/,aux"), 3));
		await SelectAsync ();
		_dashboard.CalendarZone = zone;
		await LoadCalendar ();
		Assert.That (_dashboard.CalendarRows, Has.Count.EqualTo (1));
		_handler.Steps.Enqueue ((_, _) => throw new System.IO.IOException ("private diagnostic"));
		await _dashboard.LoadCalendarAsync ();
		_dashboard.CalendarDate = new DateTime (2026, 9, 25);
		Assert.That (_dashboard.CalendarRows, Is.Empty);
		Assert.That (_dashboard.CalendarNext, Does.Contain ("unknown"));
		Assert.That (_dashboard.CalendarMessage, Does.Not.Contain ("private diagnostic"));
		}
	[Test]
	public async Task SelectionAndPlanWritesInvalidateEvenAnEmptyCalendar ()
		{
		await SelectAsync ();
		await LoadCalendar ();
		Assert.That (_dashboard.CalendarNext, Does.StartWith ("No non-delayed"));
		_dashboard.SelectTimer (null);
		Assert.That (_dashboard.CalendarNext, Does.Contain ("unknown"));
		_dashboard.SelectTimer (_dashboard.Timers.Single ());
		await LoadCalendar ();
		_handler.Reply (Discovery (_parameter));
		await _dashboard.LoadPlansAsync ();
		_handler.Steps.Enqueue ((_, _) => throw new System.IO.IOException ("uncertain write"));
		await _dashboard.SavePlanAsync ();
		Assert.That (_dashboard.CalendarNext, Does.Contain ("unknown"));
		_dashboard.CalendarDate = new DateTime (2026, 9, 25);
		Assert.That (_dashboard.CalendarRows, Is.Empty);
		}
	[Test]
	public async Task SettingsWritesInvalidateCalendarBeforeAnUncertainOutcome ()
		{
		await SelectAsync ();
		await LoadCalendar ();
		_handler.Reply (Discovery (_parameter));
		await _dashboard.LoadSettingsAsync ();
		_dashboard.SettingChoice = "Seasonal adjustment";
		_dashboard.SeasonMonths[0].Value = "90";
		Assert.That (_dashboard.CanSaveSetting, Is.True);
		_handler.Steps.Enqueue ((_, _) => throw new System.IO.IOException ("uncertain"));
		await _dashboard.SaveSettingAsync ();
		Assert.That (_dashboard.CalendarNext, Does.Contain ("unknown"));
		}
	[Test]
	public async Task CloseCancelsPendingCalendarReadWithoutKeepingData ()
		{
		await SelectAsync ();
		var entered = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		_handler.Steps.Enqueue (async (_, token) => { entered.SetResult (true); await Task.Delay (Timeout.Infinite, token); throw new InvalidOperationException (); });
		Task read = _dashboard.LoadCalendarAsync ();
		Assert.That (await Task.WhenAny (entered.Task, Task.Delay (5000)), Is.SameAs (entered.Task));
		await _dashboard.CloseAsync ();
		await read;
		Assert.That (_dashboard.CalendarRows, Is.Empty);
		Assert.That (_dashboard.CanLoadCalendar, Is.False);
		}
	}
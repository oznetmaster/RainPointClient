// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

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
public sealed class DashboardPlanTests
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
	private async Task LoadAsync ()
		{
		_handler.Reply (Discovery (_parameter));
		await _dashboard.LoadPlansAsync ();
		}
	private void Write (bool ignore = false, bool failRead = false, bool uncertain = false)
		{
		_handler.Reply (Discovery (_parameter));
		_handler.Steps.Enqueue (async (request, _) =>
		{
			if (uncertain)
				throw new System.IO.IOException ("secret response details");
			if (!ignore)
				_parameter = JsonSerializer.Deserialize<TimerParameterRequest> (await request.Content!.ReadAsStringAsync ())!.Parameter;
			return new HttpResponseMessage (HttpStatusCode.OK) { Content = new StringContent ("{\"code\":0}") };
		});
		if (!uncertain)
			_handler.Steps.Enqueue ((_, _) => failRead ? throw new HttpRequestException ("secret transport details") : Task.FromResult (new HttpResponseMessage (HttpStatusCode.OK) { Content = new StringContent (Discovery (_parameter)) }));
		}
	private RainPointScheduleSnapshot ReadSaved () => ScheduleDecoder.Decode (new RainPointDevice { Address = 2, PortNumber = 3, Parameter = _parameter }, _dashboard.PlanZone);
	private static string WithPlans (string plans) => Empty.Replace (",/,aux", "," + plans + ",aux");

	private void AssertOtherZonesUnchanged (string before, int zone)
		{
		string[] original = before.Split ('|'), actual = _parameter.Split ('|');
		for (int i = 0; i < 3; i++)
			if (i != zone - 1)
				Assert.That (actual[i], Is.EqualTo (original[i]), "An unrelated zone changed.");
		}
	[TestCase (1, false)]
	[TestCase (2, false)]
	[TestCase (3, false)]
	[TestCase (1, true)]
	[TestCase (2, true)]
	[TestCase (3, true)]
	public async Task OnceIsNotOfferedAndExistingRecordsCanOnlyBeDisabledOrDeleted (int zone, bool enabled)
		{
		string record = (enabled ? "80" : "00") + "00403c000000390d";
		_parameter = string.Join ("|", Enumerable.Repeat (WithPlans (record + "/").Split ('|')[0], 3));
		await SelectAsync ();
		_dashboard.PlanZone = zone;
		await LoadAsync ();
		_dashboard.SelectedPlan = _dashboard.Plans.Single ();
		Assert.That (_dashboard.PlanDraft.Repeats, Does.Not.Contain ("Once"));
		Assert.That (_dashboard.PlanDraft.Validation, Does.Contain ("Once"));
		Assert.That (_dashboard.CanSavePlan, Is.False);
		Assert.That (_dashboard.CanToggleSelectedPlan, Is.EqualTo (enabled));
		if (enabled)
			{
			Write ();
			await _dashboard.TogglePlanAsync ();
			_dashboard.SelectedPlan = _dashboard.Plans.Single ();
			}
		Assert.That (_dashboard.CanToggleSelectedPlan, Is.False);
		int requests = _handler.Requests.Count;
		await _dashboard.TogglePlanAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (requests));
		Assert.That (_dashboard.CanChangeSelectedPlan, Is.True);
		Write ();
		await _dashboard.DeletePlanAsync ();
		Assert.That (_dashboard.Plans, Is.Empty);
		}

	[Test]
	public async Task SignedOutAndUnloadedActionsCannotReadOrWrite ()
		{
		await _dashboard.LoadPlansAsync ();
		await _dashboard.SavePlanAsync ();
		await _dashboard.TogglePlanAsync ();
		await _dashboard.DeletePlanAsync ();
		Assert.That (_handler.Requests, Is.Empty);
		await SelectAsync ();
		await _dashboard.SavePlanAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (3));
		Assert.That (_dashboard.CanManagePlans, Is.False);
		}
	[Test]
	public async Task EmptyLoadedPlanListPreparesDisabledDraftWithoutWriting ()
		{
		await SelectAsync ();
		await LoadAsync ();
		Assert.That (_dashboard.Plans, Is.Empty);
		Assert.That (_dashboard.PlanDraft.Enabled, Is.False);
		Assert.That (_dashboard.CanSavePlan, Is.True);
		Assert.That (_dashboard.PlansMessage, Is.EqualTo ("No saved plans in this zone."));
		Assert.That (_handler.Requests, Has.Count.EqualTo (4));
		}
	public static IEnumerable<object[]> ModesAndRepeats ()
		{
		foreach (int zone in new[] { 1, 2, 3 })
			foreach (string mode in new[] { "Normal irrigation", "Cycle and soak", "Misting" })
				foreach (string repeat in new[] { "Every day", "Odd days", "Even days", "Selected weekdays", "Every N days" })
					foreach (bool volume in new[] { false, true })
						yield return new object[] { zone, mode, repeat, volume };
		}
	[TestCaseSource (nameof (ModesAndRepeats))]
	public async Task AllSupportedModesAndRepeatsCreateAndReadBackDisabledPlan (int zone, string mode, string repeat, bool volume)
		{
		_parameter = string.Join ("|", Enumerable.Repeat (Empty.Split ('|')[0], 3));
		await SelectAsync ();
		_dashboard.PlanZone = zone;
		string before = _parameter;
		await LoadAsync ();
		_dashboard.PlanDraft.Mode = mode;
		_dashboard.PlanDraft.Repeat = repeat;
		_dashboard.PlanDraft.Volume = volume ? "1.4" : string.Empty;
		_dashboard.PlanDraft.Start = "08:30";
		_dashboard.PlanDraft.Date = "2026-09-24";
		_dashboard.PlanDraft.Interval = "3";
		_dashboard.PlanDraft.Weekdays.First (day => day.Name == "Monday").Selected = true;
		Assert.That (_dashboard.CanSavePlan, Is.True, _dashboard.PlanDraft.Validation);
		Write ();
		await _dashboard.SavePlanAsync ();
		var plan = ReadSaved ().Schedules.Single ();
		Assert.That (plan.Enabled, Is.False);
		Assert.That (plan.WaterLimitLitres, Is.EqualTo (volume ? 1.4m : (decimal?)null));
		Assert.That (plan.StartTime, Is.EqualTo (new TimeSpan (8, 30, 0)));
		Assert.That (plan.Mode, Is.EqualTo (mode == "Normal irrigation" ? RainPointScheduleMode.Irrigation : mode == "Misting" ? RainPointScheduleMode.Misting : RainPointScheduleMode.CycleAndSoak));
		Assert.That (plan.Duration, Is.EqualTo (mode == "Normal irrigation" ? TimeSpan.FromSeconds (60) : TimeSpan.FromMinutes (10)));
		Assert.That (plan.EffectiveDate!.Value.Kind, Is.EqualTo (DateTimeKind.Unspecified));
		Assert.That (plan.Repeat, Is.EqualTo ((RainPointScheduleRepeat)Array.IndexOf (new[] { "Once", "Every day", "Odd days", "Even days", "Selected weekdays", "Every N days" }, repeat)));
		AssertOtherZonesUnchanged (before, zone);
		Assert.That (_dashboard.PlansReadAt, Does.StartWith ("Zone " + zone));
		Assert.That (_dashboard.PlansMessage, Does.Contain ("matches the cloud read-back"));
		Assert.That (_handler.Requests.Any (x => x.Path.Contains ("controlWorkMode")), Is.False);
		}
	[TestCase (1, "Normal irrigation")]
	[TestCase (2, "Normal irrigation")]
	[TestCase (3, "Normal irrigation")]
	[TestCase (1, "Cycle and soak")]
	[TestCase (2, "Cycle and soak")]
	[TestCase (3, "Cycle and soak")]
	[TestCase (1, "Misting")]
	[TestCase (2, "Misting")]
	[TestCase (3, "Misting")]
	public async Task ReplaceToggleAndDeleteUseSelectedSnapshotAndVerifyFullList (int zone, string mode)
		{
		_parameter = string.Join ("|", Enumerable.Repeat (WithPlans ("00004a3c0000000000/00004b780000000000").Split ('|')[0], 3));
		string before = _parameter;
		await SelectAsync ();
		_dashboard.PlanZone = zone;
		await LoadAsync ();
		_dashboard.SelectedPlan = _dashboard.Plans[1];
		_dashboard.PlanDraft.Mode = mode;
		_dashboard.PlanDraft.Start = "09:00";
		_dashboard.PlanDraft.Volume = "1.4";
		_dashboard.PlanDraft.Duration = mode == "Normal irrigation" ? "120" : "12";
		Write ();
		await _dashboard.SavePlanAsync ();
		AssertOtherZonesUnchanged (before, zone);
		Assert.That (_dashboard.PlansMessage, Does.Contain ("matches the cloud read-back"));
		Assert.That (ReadSaved ().Schedules[1].WaterLimitLitres, Is.EqualTo (1.4m));
		_dashboard.SelectedPlan = _dashboard.Plans[1];
		Assert.That (_dashboard.PlanDraft.Volume, Is.EqualTo ("1.4"));
		Assert.That (_dashboard.CanSavePlan, Is.True);
		_dashboard.PlanDraft.Volume = string.Empty;
		Write ();
		await _dashboard.SavePlanAsync ();
		Assert.That (ReadSaved ().Schedules[1].WaterLimitLitres, Is.Null);
		Assert.That (ReadSaved ().Schedules[0].StartTime, Is.EqualTo (TimeSpan.FromHours (8)));
		_dashboard.SelectedPlan = _dashboard.Plans[1];
		Write ();
		await _dashboard.TogglePlanAsync ();
		AssertOtherZonesUnchanged (before, zone);
		Assert.That (ReadSaved ().Schedules[1].Enabled, Is.True);
		_dashboard.SelectedPlan = _dashboard.Plans[1];
		Write ();
		await _dashboard.TogglePlanAsync ();
		AssertOtherZonesUnchanged (before, zone);
		Assert.That (ReadSaved ().Schedules[1].Enabled, Is.False);
		_dashboard.SelectedPlan = _dashboard.Plans[0];
		Write ();
		await _dashboard.DeletePlanAsync ();
		AssertOtherZonesUnchanged (before, zone);
		Assert.That (_dashboard.PlansMessage, Does.Contain ("matches the cloud read-back"));
		Assert.That (ReadSaved ().Schedules.Single ().StartTime, Is.EqualTo (TimeSpan.FromHours (9)));
		}
	[TestCase ("Normal irrigation", "Volume", "0.2")]
	[TestCase ("Normal irrigation", "Volume", "6000.1")]
	[TestCase ("Cycle and soak", "Volume", "0.2")]
	[TestCase ("Cycle and soak", "Volume", "6000.1")]
	[TestCase ("Cycle and soak", "Volume", "1.23")]
	[TestCase ("Misting", "Volume", "0.2")]
	[TestCase ("Misting", "Volume", "6000.1")]
	[TestCase ("Misting", "Volume", "1.23")]
	[TestCase ("Normal irrigation", "Start", "24:00")]
	[TestCase ("Normal irrigation", "Duration", "59")]
	[TestCase ("Normal irrigation", "Duration", "43201")]
	[TestCase ("Normal irrigation", "Volume", "0")]
	[TestCase ("Normal irrigation", "Volume", "1.23")]
	[TestCase ("Normal irrigation", "Volume", "6553.6")]
	[TestCase ("Normal irrigation", "Date", "2026-02-30")]
	[TestCase ("Cycle and soak", "Duration", "4")]
	[TestCase ("Cycle and soak", "Water", "11")]
	[TestCase ("Cycle and soak", "Pause", "721")]
	[TestCase ("Misting", "Duration", "1.5")]
	[TestCase ("Misting", "Water", "4")]
	[TestCase ("Misting", "Pause", "3601")]
	public async Task InvalidDraftCannotWrite (string mode, string property, string value)
		{
		await SelectAsync ();
		await LoadAsync ();
		_dashboard.PlanDraft.Mode = mode;
		typeof (PlanDraft).GetProperty (property)!.SetValue (_dashboard.PlanDraft, value);
		Assert.That (_dashboard.CanSavePlan, Is.False);
		await _dashboard.SavePlanAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (4));
		}
	[TestCase ("Every N days")]
	[TestCase ("Selected weekdays")]
	public async Task RecurrenceNeedsItsDateIntervalOrWeekdays (string repeat)
		{
		await SelectAsync ();
		await LoadAsync ();
		_dashboard.PlanDraft.Repeat = repeat;
		Assert.That (_dashboard.CanSavePlan, Is.False);
		_dashboard.PlanDraft.Date = "2026-09-24";
		_dashboard.PlanDraft.Interval = "128";
		Assert.That (_dashboard.CanSavePlan, Is.False);
		await _dashboard.SavePlanAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (4));
		}
	[Test]
	public async Task CycleElapsedTimeMustFitRepeatAndPartialFinalCycleIsAllowed ()
		{
		await SelectAsync ();
		await LoadAsync ();
		_dashboard.PlanDraft.Mode = "Cycle and soak";
		_dashboard.PlanDraft.Duration = "1440";
		_dashboard.PlanDraft.Water = "1";
		_dashboard.PlanDraft.Pause = "720";
		Assert.That (_dashboard.CanSavePlan, Is.False);
		Assert.That (_dashboard.PlanDraft.Validation, Does.Contain ("fit before the next repeat"));
		_dashboard.PlanDraft.Duration = "11";
		_dashboard.PlanDraft.Water = "5";
		_dashboard.PlanDraft.Pause = "30";
		Assert.That (_dashboard.CanSavePlan, Is.True);
		}
	[Test]
	public async Task OptionalVolumeAndSelectedDaysArePreservedWhenReplacing ()
		{
		_parameter = WithPlans ("891ea258020000380d07000d00/");
		await SelectAsync ();
		await LoadAsync ();
		Assert.That (_dashboard.PlanDraft.Weekdays.Where (d => d.Selected).Select (d => d.Name), Is.EquivalentTo (new[] { "Sunday", "Wednesday" }));
		_dashboard.PlanDraft.Mode = "Normal irrigation";
		_dashboard.PlanDraft.Volume = "1.4";
		Write ();
		await _dashboard.SavePlanAsync ();
		Assert.That (ReadSaved ().Schedules.Single ().WaterLimitLitres, Is.EqualTo (1.4m));
		Assert.That (ReadSaved ().Schedules.Single ().Enabled, Is.True);
		}

	[Test]
	public async Task SixPlanLimitBlocksNewButAllowsSelectedPlanActions ()
		{
		_parameter = WithPlans (string.Join ("/", Enumerable.Repeat ("00004a3c0000000000", 6)));
		await SelectAsync ();
		await LoadAsync ();
		Assert.That (_dashboard.CanNewPlan, Is.False);
		Assert.That (_dashboard.CanChangeSelectedPlan, Is.True);
		Assert.That (_dashboard.CanSavePlan, Is.True);
		_dashboard.NewPlan ();
		Assert.That (_dashboard.SelectedPlan, Is.Not.Null);
		}
	[Test]
	public async Task HourlyPlanIsNotSilentlyConvertedButCanBeDisabled ()
		{
		_parameter = WithPlans ("8200703c00/");
		await SelectAsync ();
		await LoadAsync ();
		Assert.That (_dashboard.PlanDetails, Does.Contain ("Every 2 hours"));
		Assert.That (_dashboard.CanEditPlanDraft, Is.False);
		Assert.That (_dashboard.CanSavePlan, Is.False);
		Write ();
		await _dashboard.TogglePlanAsync ();
		Assert.That (ReadSaved ().Schedules.Single ().Enabled, Is.False);
		Assert.That (ReadSaved ().Schedules.Single ().Repeat, Is.EqualTo (RainPointScheduleRepeat.IntervalHours));
		}
	[Test]
	public async Task NewPlanAlwaysStartsDisabledAndDoesNotCopySelectedEnabledPlan ()
		{
		_parameter = WithPlans ("80004a3c0000000000/");
		await SelectAsync ();
		await LoadAsync ();
		Assert.That (_dashboard.PlanDraft.Enabled, Is.True);
		_dashboard.NewPlan ();
		Assert.That (_dashboard.SelectedPlan, Is.Null);
		Assert.That (_dashboard.PlanDraft.Enabled, Is.False);
		Assert.That (_handler.Requests, Has.Count.EqualTo (4));
		}
	[Test]
	public async Task MalformedConfigurationIsNotShownAsAnEmptyValidList ()
		{
		_parameter = WithPlans ("garbage/");
		await SelectAsync ();
		await LoadAsync ();
		Assert.That (_dashboard.CanSavePlan, Is.False);
		Assert.That (_dashboard.PlansMessage, Does.Contain ("does not mean there are no plans"));
		}
	[Test]
	public async Task StaleSnapshotNeverWritesAndMustBeReloaded ()
		{
		await SelectAsync ();
		await LoadAsync ();
		_handler.Reply (Discovery (_parameter.Replace ("tail", "changed")));
		await _dashboard.SavePlanAsync ();
		await _dashboard.SavePlanAsync ();
		Assert.That (_dashboard.CanManagePlans, Is.False);
		Assert.That (_handler.Requests.Count (x => x.Path == "/app/device/sub/update"), Is.Zero);
		}
	[TestCase (false, false)]
	[TestCase (true, false)]
	[TestCase (false, true)]
	public async Task UncertainOrUnverifiedSaveRequiresReloadAndIsNotReplayed (bool ignore, bool failRead)
		{
		await SelectAsync ();
		await LoadAsync ();
		Write (ignore, failRead, uncertain: !ignore && !failRead);
		await _dashboard.SavePlanAsync ();
		await _dashboard.SavePlanAsync ();
		Assert.That (_dashboard.CanManagePlans, Is.False);
		Assert.That (_handler.Requests.Count (x => x.Path == "/app/device/sub/update"), Is.EqualTo (1));
		Assert.That (_dashboard.PlansMessage, Does.Contain ("no write was retried").And.Not.Contain ("secret"));
		}
	[Test]
	public async Task DeviceSelectionAndSignOutClearSavedPlansAndDrafts ()
		{
		_parameter = WithPlans ("80004a3c0000000000/");
		await SelectAsync ();
		await LoadAsync ();
		var timer = _dashboard.SelectedTimer;
		_dashboard.SelectTimer (null);
		Assert.That (_dashboard.Plans, Is.Empty);
		Assert.That (_dashboard.PlanDraft.Enabled, Is.False);
		_dashboard.SelectTimer (timer);
		await LoadAsync ();
		_handler.Reply ("{\"code\":0}");
		await _dashboard.DisconnectAsync ();
		Assert.That (_dashboard.Plans, Is.Empty);
		Assert.That (_dashboard.CanManagePlans, Is.False);
		}
	[Test]
	public async Task BusyOperationPreventsSecondWriteOrSelectionAndCloseCancels ()
		{
		await SelectAsync ();
		await LoadAsync ();
		_handler.Steps.Enqueue (async (_, token) => { await Task.Delay (Timeout.Infinite, token); throw new InvalidOperationException ("Unreachable"); });
		Task write = _dashboard.SavePlanAsync ();
		_dashboard.PlanZone = 2;
		await _dashboard.SavePlanAsync ();
		await _dashboard.DeletePlanAsync ();
		await _dashboard.CloseAsync ();
		await write;
		Assert.That (_dashboard.PlanZone, Is.EqualTo (1));
		Assert.That (_handler.Requests, Has.Count.EqualTo (5));
		Assert.That (_dashboard.CanManagePlans, Is.False);
		}
	}
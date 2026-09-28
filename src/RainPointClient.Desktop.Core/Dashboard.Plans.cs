using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace RainPointClient.Desktop.Core;

public sealed partial class Dashboard
	{
	private RainPointScheduleSnapshot? _plans;
	private PlanRow? _selectedPlan;
	private int _planZone = 1;
	public PlanDraft PlanDraft { get; } = new ();
	public ObservableCollection<PlanRow> Plans { get; } = new ();
	public IReadOnlyList<int> PlanZones { get; } = Array.AsReadOnly (new[] { 1, 2, 3 });
	public int PlanZone
		{
		get => _planZone; set
			{
			if (!CanEdit || value is < 1 or > 3 || value == _planZone)
				return;
			_planZone = value;
			ClearPlans ();
			Changed ();
			}
		}
	public PlanRow? SelectedPlan
		{
		get => _selectedPlan;
		set
			{
			if (!CanEdit || (value is not null && !Plans.Contains (value)) || ReferenceEquals (value, _selectedPlan))
				return;
			_selectedPlan = value;
			if (value is null)
				PlanDraft.Reset ();
			else
				PlanDraft.Load (value.Plan);
			Changed ();
			}
		}
	public string PlanDetails => _selectedPlan?.Details ?? "Select a saved plan to view it, or create a new disabled plan.";
	public string PlanEditorTitle => _selectedPlan is null ? "New plan (disabled by default)" : $"Replace plan {_selectedPlan.Number}";
	public string PlanSaveLabel => _selectedPlan is null ? $"Create zone {_planZone} plan" : "Replace selected plan";
	public string PlanToggleLabel => _selectedPlan?.Plan.Enabled == true ? "Disable selected plan" : "Enable selected plan";
	public string PlansMessage { get; private set; } = "Load saved plans for the selected timer and zone.";
	public string PlansReadAt { get; private set; } = "No saved plans loaded.";
	public bool CanLoadPlans => CanRefresh && _timer is not null;
	public bool CanManagePlans => CanLoadPlans && _plans?.Availability == TimerReadingAvailability.Decoded;
	public bool CanNewPlan => CanManagePlans && Plans.Count < 6;
	public bool CanToggleSelectedPlan => CanChangeSelectedPlan && (_selectedPlan!.Plan.Enabled || _selectedPlan.Plan.Repeat != RainPointScheduleRepeat.Once);
	public bool CanChangeSelectedPlan => CanManagePlans && _selectedPlan is not null;
	public bool CanEditPlanDraft => CanManagePlans && (_selectedPlan is null ? Plans.Count < 6 : PlanDraft.IsSupported);
	public bool CanSavePlan => CanEditPlanDraft && PlanDraft.TryBuild (out _, out _);
	public void NewPlan ()
		{
		if (!CanNewPlan)
			return;
		_selectedPlan = null;
		PlanDraft.Reset ();
		Changed ();
		}
	public Task LoadPlansAsync ()
		{
		if (!CanLoadPlans)
			return Task.CompletedTask;
		return RunAsync (async token =>
		{
			ClearPlans ();
			PlansMessage = "Loading saved plans…";
			Changed ();
			try
				{
				var result = await _client.GetTimerSchedulesAsync (_hub!, _timer!.Address, _planZone, token);
				token.ThrowIfCancellationRequested ();
				ShowPlans (result);
				Message = "Saved plans loaded. See the Plans tab.";
				}
			catch { ClearPlans (); PlansMessage = "Plans could not be read. Reload explicitly or sign in again."; throw; }
		}, "Plan read failed. Check the Plans tab.");
		}
	public Task SavePlanAsync ()
		{
		if (!CanSavePlan || !PlanDraft.TryBuild (out PlanValue? value, out _))
			return Task.CompletedTask;
		return WritePlanAsync (_selectedPlan is null ? "add" : "replace", value);
		}
	public Task DeletePlanAsync () => CanChangeSelectedPlan ? WritePlanAsync ("delete", null) : Task.CompletedTask;
	public Task TogglePlanAsync () => CanToggleSelectedPlan ? WritePlanAsync ("toggle", null) : Task.CompletedTask;
	private Task WritePlanAsync (string operation, PlanValue? value)
		{
		RainPointScheduleSnapshot before = _plans!;
		RainPointHub hub = _hub!;
		int index = _selectedPlan?.Plan.Index ?? -1;
		bool enabled = _selectedPlan?.Plan.Enabled != true;
		return RunAsync (async token =>
		{
			ClearCalendar ();
			_plans = null;
			PlansMessage = $"Saving the zone {before.Zone} plan change…";
			Changed ();
			bool accepted = false;
			try
				{
				if (operation == "delete")
					await _client.DeleteTimerScheduleAsync (hub, before, index, token);
				else if (operation == "toggle")
					await _client.SetTimerScheduleEnabledAsync (hub, before, index, enabled, token);
				else if (value!.Mode == RainPointScheduleMode.Irrigation)
					{
					if (operation == "add")
						await _client.AddTimerScheduleAsync (hub, before, value.Normal (), token);
					else
						await _client.UpdateTimerScheduleAsync (hub, before, index, value.Normal (), token);
					}
				else if (value.Mode == RainPointScheduleMode.Misting)
					{
					if (operation == "add")
						await _client.AddTimerScheduleAsync (hub, before, value.Mist (), token);
					else
						await _client.UpdateTimerScheduleAsync (hub, before, index, value.Mist (), token);
					}
				else
					{
					if (operation == "add")
						await _client.AddTimerScheduleAsync (hub, before, value.Cycle (), token);
					else
						await _client.UpdateTimerScheduleAsync (hub, before, index, value.Cycle (), token);
					}
				accepted = true;
				var after = await _client.GetTimerSchedulesAsync (hub, before.Address, before.Zone, token);
				token.ThrowIfCancellationRequested ();
				bool matches = MatchesPlans (before, after, operation, index, value, enabled);
				ShowPlans (after);
				if (!matches)
					_plans = null;
				PlansMessage = matches ? "Plan change matches the cloud read-back. Enabled plans may water at their scheduled times." : "Cloud accepted the change, but read-back does not match yet. Reload before another change; no write was retried.";
				Message = "Plan operation completed. See the Plans tab for verification.";
				}
			catch { _plans = null; PlansMessage = accepted ? "Cloud accepted the change, but read-back failed. Displayed plans are from the previous read. Reload before another change; no write was retried." : "Plan change did not complete. Values may be unsupported, configuration may have changed, or the outcome may be unknown. Reload before another attempt; no write was retried."; throw; }
		}, "Plan operation failed. Check the Plans tab.");
		}
	private static bool MatchesPlans (RainPointScheduleSnapshot before, RainPointScheduleSnapshot after, string operation, int index, PlanValue? value, bool enabled)
		{
		int count = before.Schedules.Count + (operation == "add" ? 1 : operation == "delete" ? -1 : 0);
		if (after.Availability != TimerReadingAvailability.Decoded || after.Schedules.Count != count)
			return false;
		for (int i = 0; i < count; i++)
			{
			if ((operation == "add" && i == count - 1) || (operation == "replace" && i == index))
				{
				if (!value!.Matches (after.Schedules[i]))
					return false;
				}
			else
				{
				int original = operation == "delete" && i >= index ? i + 1 : i;
				if (!PlanRow.Same (after.Schedules[i], before.Schedules[original], operation == "toggle" && i == index ? enabled : null))
					return false;
				}
			}
		return true;
		}
	private void ClearPlans ()
		{
		_plans = null;
		_selectedPlan = null;
		Plans.Clear ();
		PlanDraft.Reset ();
		PlansReadAt = "No saved plans loaded for this selection.";
		PlansMessage = "Load saved plans for the selected timer and zone.";
		}
	private void ShowPlans (RainPointScheduleSnapshot snapshot)
		{
		_plans = snapshot;
		_selectedPlan = null;
		Plans.Clear ();
		foreach (var p in snapshot.Schedules)
			Plans.Add (new PlanRow (p));
		_selectedPlan = Plans.FirstOrDefault ();
		if (_selectedPlan is null)
			PlanDraft.Reset ();
		else
			PlanDraft.Load (_selectedPlan.Plan);
		PlansReadAt = $"Zone {snapshot.Zone} · read {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}";
		PlansMessage = snapshot.Availability == TimerReadingAvailability.Decoded ? (Plans.Count == 0 ? "No saved plans in this zone." : $"{Plans.Count} saved plans. Select one to view or edit.") : $"Saved configuration is {snapshot.Availability}; an empty table does not mean there are no plans.";
		}
	}
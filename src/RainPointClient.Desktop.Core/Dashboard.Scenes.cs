// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
namespace RainPointClient.Desktop.Core;

public sealed partial class Dashboard
	{
	private RainPointScene? _scene;
	private bool _editingScene;
	private readonly System.Collections.Generic.List<RainPointHub> _sceneHubs = [];
	private RainPointSceneSummary? _selectedScene;
	public ObservableCollection<RainPointWeatherType> SceneWeatherTypes { get; } = new ();
	public RainPointWeatherType? SelectedSceneWeatherType
		{
		get; set;
		}
	public ObservableCollection<RainPointSceneSummary> Scenes { get; } = new ();
	public ObservableCollection<RainPointSceneCondition> SceneConditions { get; } = new ();
	public ObservableCollection<RainPointSceneAction> SceneActions { get; } = new ();
	public RainPointSceneSummary? SelectedScene
		{
		get => _selectedScene; set
			{
			_selectedScene = value;
			ClearSceneHistory ();
			_editingScene = false;
			_scene = null;
			SceneConditions.Clear ();
			SceneActions.Clear ();
			Changed ();
			}
		}
	public RainPointSceneCondition? SelectedSceneCondition
		{
		get; set;
		}
	public RainPointSceneAction? SelectedSceneAction
		{
		get; set;
		}
	public string SceneMessage { get; private set; } = "Load scenes for the selected home.";
	public bool CanReadScenes => CanReadAdministration;
	public bool CanLoadScene => CanReadScenes && SelectedScene is not null;
	public bool CanWriteScene => CanReadScenes && _scene is not null;
	public bool CanEditSceneDraft => CanReadScenes && (SelectedScene is null || _editingScene);
	public bool CanCreateScene => CanEditSceneDraft && _sceneHubs.Any (h => h.Id == _hub?.Id && h.SupportsSceneExecution == true);
	public string SceneNameDraft { get; set; } = string.Empty;
	public bool SceneMatchAll
		{
		get; set;
		}
	public string SceneMaximumRuns { get; set; } = "1";
	public string SceneIntervalMinutes { get; set; } = "120";
	public string SceneStartDate { get; set; } = string.Empty;
	public string SceneEndDate { get; set; } = string.Empty;
	public RainPointSceneSolarPeriod SceneSolarPeriod
		{
		get; set;
		}
	public RainPointSceneSolarPeriod[] SceneSolarPeriods => (RainPointSceneSolarPeriod[])Enum.GetValues (typeof (RainPointSceneSolarPeriod));
	public string SceneWindowStart { get; set; } = string.Empty;
	public string SceneWindowEnd { get; set; } = string.Empty;
	public string SceneClockTime { get; set; } = "08:00";
	public RainPointSceneRepeat SceneRepeat
		{
		get; set;
		}
	public RainPointSceneTime SceneTime
		{
		get; set;
		}
	public RainPointSceneWeatherMetric SceneMetric { get; set; } = RainPointSceneWeatherMetric.RainProbabilityPercent;
	public RainPointSceneComparison SceneComparison { get; set; } = RainPointSceneComparison.GreaterThan;
	public string SceneThreshold { get; set; } = "50";
	public string SceneNotificationText { get; set; } = "Rain expected";
	public string SceneNotificationEmail { get; set; } = string.Empty;
	public string SceneRainDelayDays { get; set; } = "1";
	public RainPointSceneRepeat[] SceneRepeats => (RainPointSceneRepeat[])Enum.GetValues (typeof (RainPointSceneRepeat));
	public System.Collections.Generic.IReadOnlyList<PlanWeekday> SceneWeekdays { get; } = Array.AsReadOnly (Enum.GetValues (typeof (DayOfWeek)).Cast<DayOfWeek> ().Select (d => new PlanWeekday (d)).ToArray ());
	public System.Collections.Generic.IReadOnlyList<PlanWeekday> SceneEffectiveWeekdays { get; } = Array.AsReadOnly (Enum.GetValues (typeof (DayOfWeek)).Cast<DayOfWeek> ().Select (d => new PlanWeekday (d)).ToArray ());
	public RainPointSceneRepeat SceneEffectiveRepeat
		{
		get; set;
		}
	public string SceneOnceDateTime { get; set; } = string.Empty;
	private static RainPointSceneWeekdays SelectedSceneDays (System.Collections.Generic.IEnumerable<PlanWeekday> days) => (RainPointSceneWeekdays)days.Where (d => d.Selected).Aggregate (0, (mask, day) => mask | 1 << (int)day.Day);
	public RainPointSceneTime[] SceneTimes => (RainPointSceneTime[])Enum.GetValues (typeof (RainPointSceneTime));
	public RainPointSceneWeatherMetric[] SceneMetrics => (RainPointSceneWeatherMetric[])Enum.GetValues (typeof (RainPointSceneWeatherMetric));
	public RainPointSceneComparison[] SceneComparisons => (RainPointSceneComparison[])Enum.GetValues (typeof (RainPointSceneComparison));
	private void ClearScenes ()
		{
		SceneWeatherTypes.Clear ();
		SelectedSceneWeatherType = null;
		_sceneHubs.Clear ();
		Scenes.Clear ();
		SelectedScene = null;
		SceneNameDraft = string.Empty;
		SceneStartDate = SceneEndDate = SceneWindowStart = SceneWindowEnd = SceneOnceDateTime = string.Empty;
		SceneSolarPeriod = RainPointSceneSolarPeriod.None;
		SceneEffectiveRepeat = RainPointSceneRepeat.Daily;
		foreach (var day in SceneEffectiveWeekdays)
			day.Selected = false;
		foreach (var day in SceneWeekdays)
			day.Selected = false;
		SceneMatchAll = false;
		SceneMaximumRuns = "1";
		SceneIntervalMinutes = "120";
		SceneNotificationEmail = string.Empty;
		SceneMessage = "Load scenes for the selected home.";
		}
	public Task LoadScenesAsync ()
		{
		if (!CanReadScenes)
			return Task.CompletedTask;
		long id = _home!.Id;
		return RunAsync (async token => { ClearScenes (); foreach (var item in await _client.GetScenesAsync (id, token)) Scenes.Add (item); _sceneHubs.AddRange (await _client.GetSceneDevicesAsync (id, token)); SceneMessage = $"{Scenes.Count} scene(s). Loading never executes a scene."; }, "Scene list could not be loaded.");
		}
	public Task LoadSelectedSceneAsync ()
		{
		if (!CanLoadScene)
			return Task.CompletedTask;
		long homeId = _home!.Id, sceneId = SelectedScene!.Id;
		return RunAsync (async token =>
		{
			_scene = null;
			SceneConditions.Clear ();
			SceneActions.Clear ();
			var scene = await _client.GetSceneAsync (homeId, sceneId, token);
			_scene = scene;
			foreach (var c in scene.Conditions)
				SceneConditions.Add (c);
			foreach (var a in scene.Actions)
				SceneActions.Add (a);
			SceneNameDraft = scene.Name;
			SceneMatchAll = scene.MatchAll == true;
			SceneMaximumRuns = scene.MaximumRunsPerDay?.ToString (CultureInfo.InvariantCulture) ?? "1";
			SceneIntervalMinutes = scene.MinimumIntervalMinutes?.ToString (CultureInfo.InvariantCulture) ?? "120";
			SceneMessage = $"Switch: {scene.Enabled?.ToString () ?? "unknown"}; available: {scene.Available?.ToString () ?? "unknown"}. Select Edit selected scene to change supported settings, or New draft to create another scene.";
		}, "Scene details could not be loaded.");
		}
	public Task SetSelectedSceneEnabledAsync (bool enabled) => ChangeSceneAsync (enabled);
	public Task DeleteSelectedSceneAsync () => ChangeSceneAsync (null);
	private Task ChangeSceneAsync (bool? enabled)
		{
		if (!CanWriteScene)
			return Task.CompletedTask;
		var expected = _scene!;
		return RunAsync (async token => { _scene = null; if (enabled.HasValue) await _client.SetSceneEnabledAsync (expected, enabled.Value, token); else await _client.DeleteSceneAsync (expected, token); SceneMessage = "Cloud accepted the change. Reload scenes to verify. Disabling does not stop an active valve."; }, "Scene change failed or its outcome is uncertain. Reload before another attempt.");
		}
	public void EditSelectedScene ()
		{
		if (!CanWriteScene)
			return;
		try
			{
			var draft = _scene!.CreateDraft ();
			if (_scene.ExecutingHubId != _hub?.Id)
				throw new InvalidOperationException ("Select the scene's executing hub before editing.");
			SceneSolarPeriod = draft.SolarPeriod;
			SceneEffectiveRepeat = draft.EffectiveRepeat;
			foreach (var day in SceneEffectiveWeekdays)
				day.Selected = ((int)draft.EffectiveWeekdays & 1 << (int)day.Day) != 0;
			SceneNameDraft = draft.Name;
			SceneMatchAll = draft.MatchAll;
			SceneMaximumRuns = draft.MaximumRunsPerDay.ToString (CultureInfo.InvariantCulture);
			SceneIntervalMinutes = draft.MinimumIntervalMinutes.ToString (CultureInfo.InvariantCulture);
			SceneStartDate = draft.StartsOn?.ToString ("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
			SceneEndDate = draft.EndsOn?.ToString ("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
			SceneWindowStart = draft.WindowStartsAt?.ToString (@"hh\:mm", CultureInfo.InvariantCulture) ?? string.Empty;
			SceneWindowEnd = draft.WindowEndsAt?.ToString (@"hh\:mm", CultureInfo.InvariantCulture) ?? string.Empty;
			_editingScene = true;
			SceneMessage = "Editing the loaded scene locally. Saving replaces its complete definition and may activate it.";
			}
		catch (Exception e) when (e is NotSupportedException or ArgumentException or InvalidOperationException) { SceneMessage = e.Message; }
		Changed ();
		}
	public void NewSceneDraft ()
		{
		if (!CanReadScenes)
			return;
		SelectedScene = null;
		SceneNameDraft = "New scene";
		SceneMatchAll = false;
		SceneMaximumRuns = "1";
		SceneIntervalMinutes = "120";
		SceneStartDate = SceneEndDate = SceneWindowStart = SceneWindowEnd = SceneOnceDateTime = string.Empty;
		SceneSolarPeriod = RainPointSceneSolarPeriod.None;
		SceneEffectiveRepeat = RainPointSceneRepeat.Daily;
		foreach (var day in SceneEffectiveWeekdays)
			day.Selected = false;
		foreach (var day in SceneWeekdays)
			day.Selected = false;
		SceneMessage = "New draft: saving can activate automation immediately. Only add actions you intend to execute.";
		Changed ();
		}
	private void EditSceneDraft (Action action)
		{
		if (!CanEditSceneDraft)
			return;
		try
			{
			action ();
			SceneMessage = "Draft changed locally. No scene has been saved.";
			}
		catch (ArgumentException e) { SceneMessage = e.Message; }
		catch (FormatException) { SceneMessage = "Enter a valid number or time."; }
		catch (OverflowException) { SceneMessage = "The entered number is too large."; }
		Changed ();
		}
	public Task LoadSceneWeatherTypesAsync () => !CanReadScenes ? Task.CompletedTask : RunAsync (async token => { SceneWeatherTypes.Clear (); SelectedSceneWeatherType = null; foreach (var type in (await _client.GetHomeOptionsAsync (token)).WeatherTypes) SceneWeatherTypes.Add (type); }, "Weather types could not be loaded.");
	public void AddSceneWeatherTypeCondition () => EditSceneDraft (() => { if (SelectedSceneWeatherType is null || !SceneWeatherTypes.Contains (SelectedSceneWeatherType)) throw new ArgumentException ("Select a weather type from the loaded list."); if (SceneConditions.Count >= 5) throw new ArgumentException ("At most five conditions."); SceneConditions.Add (RainPointSceneCondition.WeatherTypes ([SelectedSceneWeatherType])); });
	public void AddSceneWeatherCondition () => EditSceneDraft (() => { if (SceneConditions.Count >= 5) throw new ArgumentException ("At most five conditions."); SceneConditions.Add (RainPointSceneCondition.Weather (SceneMetric, SceneComparison, decimal.Parse (SceneThreshold, CultureInfo.InvariantCulture))); });
	public void AddSceneTimeCondition () => EditSceneDraft (() => { if (SceneConditions.Count >= 5) throw new ArgumentException ("At most five conditions."); SceneConditions.Add (RainPointSceneCondition.Repeating (SceneRepeat, SceneTime, SceneTime == RainPointSceneTime.Clock ? TimeSpan.ParseExact (SceneClockTime, @"hh\:mm", CultureInfo.InvariantCulture) : null, SceneRepeat == RainPointSceneRepeat.Weekdays ? SelectedSceneDays (SceneWeekdays) : RainPointSceneWeekdays.None)); });
	public void AddSceneOnceCondition () => EditSceneDraft (() => { if (SceneConditions.Count >= 5) throw new ArgumentException ("At most five conditions."); SceneConditions.Add (RainPointSceneCondition.Once (DateTime.SpecifyKind (DateTime.ParseExact (SceneOnceDateTime, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), DateTimeKind.Unspecified))); });
	public void AddSceneNotification () => EditSceneDraft (() => { if (SceneActions.Count >= 5) throw new ArgumentException ("At most five actions."); SceneActions.Add (RainPointSceneAction.Notify (SceneNotificationText, [], [SceneNotificationEmail])); });
	public void AddSceneRainDelay () => EditSceneDraft (() => { if (_hub is null || _timer is null) throw new ArgumentException ("Select a hub and timer first."); if (SceneActions.Count >= 5) throw new ArgumentException ("At most five actions."); SceneActions.Add (RainPointSceneAction.RainDelay (_hub, _timer.Address, ControlZone, int.Parse (SceneRainDelayDays, CultureInfo.InvariantCulture))); });
	public void RemoveSceneCondition () => EditSceneDraft (() => { if (SelectedSceneCondition is not null) SceneConditions.Remove (SelectedSceneCondition); });
	public void RemoveSceneAction () => EditSceneDraft (() => { if (SelectedSceneAction is not null) SceneActions.Remove (SelectedSceneAction); });
	public Task SaveNewSceneAsync ()
		{
		if (!CanCreateScene)
			return Task.CompletedTask;
		var hub = _sceneHubs.Single (h => h.Id == _hub!.Id);
		var expected = _editingScene ? _scene : null;
		RainPointSceneDraft draft;
		try
			{
			draft = new ()
				{
				Name = SceneNameDraft,
				EffectiveRepeat = SceneEffectiveRepeat,
				EffectiveWeekdays = SceneEffectiveRepeat == RainPointSceneRepeat.Weekdays ? SelectedSceneDays (SceneEffectiveWeekdays) : RainPointSceneWeekdays.None,
				MatchAll = SceneMatchAll,
				MaximumRunsPerDay = int.Parse (SceneMaximumRuns, CultureInfo.InvariantCulture),
				MinimumIntervalMinutes = int.Parse (SceneIntervalMinutes, CultureInfo.InvariantCulture),
				Conditions = SceneConditions.ToArray (),
				Actions = SceneActions.ToArray (),
				StartsOn = ParseSceneDate (SceneStartDate),
				EndsOn = ParseSceneDate (SceneEndDate),
				SolarPeriod = SceneSolarPeriod,
				WindowStartsAt = ParseSceneTime (SceneWindowStart),
				WindowEndsAt = ParseSceneTime (SceneWindowEnd)
				};
			}
		catch (Exception e) when (e is FormatException or OverflowException) { SceneMessage = "Use whole-number limits and optional yyyy-MM-dd dates."; Changed (); return Task.CompletedTask; }
		return RunAsync (async token =>
		{
			SceneConditions.Clear ();
			SceneActions.Clear ();
			_scene = null;
			_editingScene = false;
			if (expected is null)
				await _client.CreateSceneAsync (hub, draft, token);
			else
				await _client.ReplaceSceneAsync (expected, hub, draft, token);
			SceneMessage = "Cloud accepted the scene save. It may be active now. Reload scenes before any further operation.";
		}, "Scene save failed or its outcome is uncertain. Reload scenes before recreating it; do not duplicate an accepted scene.");
		}
	private static TimeSpan? ParseSceneTime (string value) => string.IsNullOrWhiteSpace (value) ? null : TimeSpan.ParseExact (value, @"hh\:mm", CultureInfo.InvariantCulture);
	private static DateTime? ParseSceneDate (string value) => string.IsNullOrWhiteSpace (value) ? null : DateTime.SpecifyKind (DateTime.ParseExact (value, "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Unspecified);
	}
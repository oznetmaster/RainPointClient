using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace RainPointClient.Desktop.Core;

public sealed partial class Dashboard
	{
	private RainPointScheduleSnapshot? _settings;
	private int _settingsZone = 1;
	private string _settingChoice = "Default duration";
	private string _settingValue = string.Empty;
	private string _settingSecondValue = string.Empty;

	public IReadOnlyList<int> SettingsZones { get; } = Array.AsReadOnly (new[] { 1, 2, 3 });
	public IReadOnlyList<string> SettingChoices { get; } = Array.AsReadOnly (new[] { "Default duration", "Misting intervals", "Flow calibration", "Seasonal adjustment", "Rain delay", "Moisture stop" });
	public int SettingsZone
		{
		get => _settingsZone;
		set
			{
			if (!CanEdit || value is < 1 or > 3 || value == _settingsZone)
				return;
			_settingsZone = value;
			ClearSettings ();
			Changed ();
			}
		}
	public string SettingChoice
		{
		get => _settingChoice;
		set
			{
			if (!CanEdit || !System.Linq.Enumerable.Contains (SettingChoices, value) || value == _settingChoice)
				return;
			_settingChoice = value;
			ResetSettingDraft ();
			Changed ();
			}
		}
	public string SettingValue
		{
		get => _settingValue;
		set
			{
			if (!CanEdit)
				return;
			_settingValue = value ?? string.Empty;
			Changed ();
			}
		}
	public string SettingSecondValue
		{
		get => _settingSecondValue;
		set
			{
			if (!CanEdit)
				return;
			_settingSecondValue = value ?? string.Empty;
			Changed ();
			}
		}
	public bool CanReadSettings => CanRefresh && _timer is not null;
	public IReadOnlyList<SeasonMonth> SeasonMonths { get; } = Array.AsReadOnly (new[] { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" }.Select (name => new SeasonMonth (name)).ToArray ());
	public bool HasSeasonalAdjustment => _settingChoice == "Seasonal adjustment";
	public bool HasSettingValue => !HasSeasonalAdjustment;
	public string SavedSeasonal { get; private set; } = "Unknown";
	public string SavedRainDelay { get; private set; } = "Unknown";
	public bool HasMistingInterval => _settingChoice == "Misting intervals";
	public bool CanEditSetting => CanReadSettings && _settings?.Availability == TimerReadingAvailability.Decoded
	 && (_settingChoice switch
		 {
			 "Flow calibration" => _settings.FlowCalibrationAvailability,
			 "Seasonal adjustment" => _settings.SeasonalAdjustmentAvailability,
			 "Rain delay" => _settings.RainDelayAvailability,
			 "Moisture stop" => _settings.SoilSensorAvailability,
			 _ => _settings.ZoneDefaultsAvailability
			 }) == TimerReadingAvailability.Decoded;
	public bool CanSaveSetting => CanEditSetting && ValidSetting (out _, out _, out _);
	public string SettingValueLabel => _settingChoice switch
		{
			"Default duration" => "Default watering duration (minutes)",
			"Moisture stop" => "Stop above soil moisture (%)",
			"Misting intervals" => "Water on (seconds)",
			"Rain delay" => "Expiry (yyyy-MM-dd HH:mm:ss, home local)",
			_ => "Flow correction (%)"
			};
	public string SettingHelp => _settingChoice switch
		{
			"Moisture stop" => "Use 1–100%, or leave blank to disable. Enabling requires an associated soil sensor. Disabling can allow watering that this threshold would otherwise stop.",
			"Default duration" => "Use 1–720 whole minutes, or leave blank for the app default (10 minutes). This does not start watering or change existing plans.",
			"Misting intervals" => "Use 5–3600 whole seconds for each interval. Blank uses the app default: 10 seconds on, 30 seconds off. This does not start misting.",
			"Seasonal adjustment" => "Set each month to a whole percentage from 10 to 200. Saved plans may run for longer or shorter. Existing plan timing is checked before saving.",
			"Rain delay" => "Enter an expiry in the home’s local calendar (2020–2083), or leave blank to clear the delay. Clearing can allow scheduled watering to resume. This does not send a valve stop command.",
			_ => "Use a whole percentage from −20 to +20. Zero means no correction. Physical measurement accuracy has not been verified."
			};
	public string SettingValidation => !CanEditSetting ? "Load readable settings before editing. Supported writes require timer firmware 120 or newer."
	 : ValidSetting (out _, out _, out _) ? "Save changes only the selected setting. Changing the setting or reloading discards unsaved edits."
	 : "Enter values within the limits shown above; fractions and invalid text cannot be saved.";
	public string SettingsMessage { get; private set; } = "Select a timer and load its saved settings.";
	public string SettingsReadAt { get; private set; } = "No settings loaded.";
	public string SavedDuration { get; private set; } = "Unknown";
	public string SavedMisting { get; private set; } = "Unknown";
	public string SavedCalibration { get; private set; } = "Unknown";

	public Task LoadSettingsAsync ()
		{
		if (!CanReadSettings)
			return Task.CompletedTask;
		return RunAsync (async token =>
		{
			ClearSettings ();
			SettingsMessage = "Loading saved settings…";
			Changed ();
			try
				{
				ShowSettings (await _client.GetTimerSchedulesAsync (_hub!, _timer!.Address, _settingsZone, token));
				SettingsMessage = "Settings read from the cloud. These are saved values, not confirmation of valve behavior.";
				}
			catch
				{
				SettingsMessage = "Settings could not be read. Reload explicitly or sign in again; no settings were changed.";
				throw;
				}
		}, "Settings could not be loaded. Check the zone-settings message.");
		}

	public Task SaveSettingAsync ()
		{
		if (!CanSaveSetting || !ValidSetting (out TimeSpan? first, out TimeSpan? second, out int percent))
			return Task.CompletedTask;
		RainPointScheduleSnapshot before = _settings!;
		string choice = _settingChoice;
		RainPointHub hub = _hub!;
		int address = _timer!.Address;
		int[] months = SeasonMonths.Select (m => int.TryParse (m.Value, out int n) ? n : 0).ToArray ();
		_ = ValidRainDelay (out DateTime? rainUntil);
		return RunAsync (async token =>
		{
			ClearCalendar ();
			// Consume our edit basis even if a preflight or write fails; only an explicit reload permits another attempt.
			_settings = null;
			SettingsMessage = $"Saving the selected zone {before.Zone} setting…";
			Changed ();
			bool accepted = false;
			try
				{
				switch (choice)
					{
					case "Moisture stop":
						await _client.SetTimerMoistureStopAsync (hub, before, percent == 0 ? null : percent, token);
						break;
					case "Default duration":
						await _client.SetTimerDefaultWateringDurationAsync (hub, before, first, token);
						break;
					case "Misting intervals":
						await _client.SetTimerMistingDefaultsAsync (hub, before, first, second, token);
						break;
					case "Seasonal adjustment":
						await _client.SetTimerSeasonalAdjustmentAsync (hub, before, months, token);
						break;
					case "Rain delay":
						await _client.SetTimerRainDelayAsync (hub, before, rainUntil, token);
						break;
					default:
						await _client.SetTimerFlowCalibrationAsync (hub, before, percent, token);
						break;
					}
				accepted = true;
				RainPointScheduleSnapshot after = await _client.GetTimerSchedulesAsync (hub, address, before.Zone, token);
				ShowSettings (after);
				bool matches = choice switch
					{
						"Default duration" => after.ZoneDefaultsAvailability == TimerReadingAvailability.Decoded && after.ZoneDefaults?.WateringDuration == first,
						"Misting intervals" => after.ZoneDefaultsAvailability == TimerReadingAvailability.Decoded && after.ZoneDefaults?.MistingRunTime == first && after.ZoneDefaults?.MistingInterval == second,
						"Seasonal adjustment" => after.SeasonalAdjustmentAvailability == TimerReadingAvailability.Decoded && after.SeasonalPercentages.SequenceEqual (months),
						"Rain delay" => after.RainDelayAvailability == TimerReadingAvailability.Decoded && after.RainDelayUntil == rainUntil,
						"Moisture stop" => after.SoilSensorAvailability == TimerReadingAvailability.Decoded && after.SoilSensorSettings?.StopAboveMoisturePercent == (percent == 0 ? (int?)null : percent),
						_ => after.FlowCalibrationAvailability == TimerReadingAvailability.Decoded && after.FlowCalibrationPercent == percent
						};
				if (!matches)
					_settings = null;
				SettingsMessage = matches ? "Saved setting matches the cloud read-back. No watering command was sent."
			: "Cloud accepted the save, but read-back does not yet match. Reload to check; the save was not retried.";
				}
			catch
				{
				_settings = null;
				SettingsMessage = accepted ? "Cloud accepted the save, but read-back failed. The displayed values are from the previous read. Reload to check; the save was not retried."
			: "Save did not complete. Settings may be stale, unsupported, or the outcome may be unknown. Reload before another attempt; the save was not retried.";
				throw;
				}
		}, "Settings operation did not complete. Check the zone-settings message.");
		}

	private void ClearSettings ()
		{
		ClearSoilChoices ();
		ClearMoistureRule ();
		SavedSoilSettings = "Unknown";
		_settings = null;
		SavedDuration = SavedMisting = SavedCalibration = SavedSeasonal = SavedRainDelay = "Unknown";
		SettingsReadAt = "No settings loaded for this selection.";
		SettingsMessage = "Load saved settings for the selected timer and zone.";
		ResetSettingDraft ();
		}

	private void ShowSettings (RainPointScheduleSnapshot snapshot)
		{
		ClearSoilChoices ();
		ClearMoistureRule ();
		SavedSoilSettings = snapshot.SoilSensorAvailability != TimerReadingAvailability.Decoded ? Unavailable (snapshot.SoilSensorAvailability) : "Sensor: " + (snapshot.SoilSensorSettings!.SensorAddress?.ToString (CultureInfo.InvariantCulture) ?? "None") + " · Stop above: " + (snapshot.SoilSensorSettings.StopAboveMoisturePercent?.ToString (CultureInfo.InvariantCulture) + (snapshot.SoilSensorSettings.StopAboveMoisturePercent.HasValue ? "%" : "Disabled"));
		_settings = snapshot;
		SavedSeasonal = snapshot.SeasonalAdjustmentAvailability == TimerReadingAvailability.Decoded ? string.Join (" · ", snapshot.SeasonalPercentages.Select ((value, i) => SeasonMonths[i].Name + " " + value + "%")) : Unavailable (snapshot.SeasonalAdjustmentAvailability);
		SavedRainDelay = snapshot.RainDelayAvailability != TimerReadingAvailability.Decoded ? Unavailable (snapshot.RainDelayAvailability) : snapshot.RainDelayUntil?.ToString ("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + (snapshot.RainDelayUntil.HasValue ? " (home local; check against the home's current time)" : "Cleared");
		SavedDuration = snapshot.ZoneDefaultsAvailability == TimerReadingAvailability.Decoded
		 ? SavedTime (snapshot.ZoneDefaults!.WateringDuration, true, "10 minutes") : Unavailable (snapshot.ZoneDefaultsAvailability);
		SavedMisting = snapshot.ZoneDefaultsAvailability == TimerReadingAvailability.Decoded
		 ? "On: " + SavedTime (snapshot.ZoneDefaults!.MistingRunTime, false, "10 seconds") + " · Off: " + SavedTime (snapshot.ZoneDefaults.MistingInterval, false, "30 seconds") : Unavailable (snapshot.ZoneDefaultsAvailability);
		SavedCalibration = snapshot.FlowCalibrationAvailability == TimerReadingAvailability.Decoded
		 ? snapshot.FlowCalibrationPercent!.Value.ToString (CultureInfo.InvariantCulture) + "%" : Unavailable (snapshot.FlowCalibrationAvailability);
		SettingsReadAt = $"Zone {snapshot.Zone} · settings read {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}";
		ResetSettingDraft ();
		}

	private static string Unavailable (TimerReadingAvailability availability) => availability switch
		{
			TimerReadingAvailability.NotReported => "Not reported",
			TimerReadingAvailability.UnsupportedFormat => "Unsupported format",
			_ => "Unrecognized configuration"
			};

	private static string SavedTime (TimeSpan? value, bool minutes, string fallback) => !value.HasValue ? "App default (" + fallback + ")"
	 : (minutes ? value.Value.TotalMinutes : value.Value.TotalSeconds).ToString ("0.########", CultureInfo.InvariantCulture) + (minutes ? " minutes" : " seconds");

	private void ResetSettingDraft ()
		{
		_settingValue = _settingSecondValue = string.Empty;
		foreach (var month in SeasonMonths)
			month.Value = string.Empty;
		if (_settings is null)
			return;
		for (int i = 0; i < _settings.SeasonalPercentages.Count; i++)
			SeasonMonths[i].Value = _settings.SeasonalPercentages[i].ToString (CultureInfo.InvariantCulture);
		if (_settingChoice == "Moisture stop")
			{
			_settingValue = _settings.SoilSensorSettings?.StopAboveMoisturePercent?.ToString (CultureInfo.InvariantCulture) ?? string.Empty;
			return;
			}
		if (_settingChoice == "Rain delay")
			{
			_settingValue = _settings.RainDelayUntil?.ToString ("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty;
			return;
			}
		if (_settingChoice == "Flow calibration")
			_settingValue = _settings.FlowCalibrationPercent?.ToString (CultureInfo.InvariantCulture) ?? string.Empty;
		else if (_settings.ZoneDefaults is { } defaults)
			{
			_settingValue = (_settingChoice == "Default duration" ? defaults.WateringDuration?.TotalMinutes : defaults.MistingRunTime?.TotalSeconds)?.ToString ("0.########", CultureInfo.InvariantCulture) ?? string.Empty;
			_settingSecondValue = defaults.MistingInterval?.TotalSeconds.ToString (CultureInfo.InvariantCulture) ?? string.Empty;
			}
		}

	private bool ValidSetting (out TimeSpan? first, out TimeSpan? second, out int percent)
		{
		first = second = null;
		percent = 0;
		if (_settingChoice == "Moisture stop")
			return string.IsNullOrWhiteSpace (_settingValue) || (_settings?.SoilSensorSettings?.SensorAddress.HasValue == true && int.TryParse (_settingValue.Trim (), NumberStyles.None, CultureInfo.InvariantCulture, out percent) && percent is >= 1 and <= 100);
		if (_settingChoice == "Seasonal adjustment")
			return SeasonMonths.All (m => int.TryParse (m.Value.Trim (), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n is >= 10 and <= 200);
		if (_settingChoice == "Rain delay")
			return ValidRainDelay (out _);
		if (_settingChoice == "Flow calibration")
			return int.TryParse (_settingValue.Trim (), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out percent) && percent is >= -20 and <= 20;
		if (_settingChoice == "Default duration")
			return OptionalTime (_settingValue, 1, 720, true, out first);
		return OptionalTime (_settingValue, 5, 3600, false, out first) && OptionalTime (_settingSecondValue, 5, 3600, false, out second);
		}
	private bool ValidRainDelay (out DateTime? until)
		{
		until = null;
		if (string.IsNullOrWhiteSpace (_settingValue))
			return true;
		if (!DateTime.TryParseExact (_settingValue.Trim (), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime value) || value.Year is < 2020 or > 2083)
			return false;
		until = DateTime.SpecifyKind (value, DateTimeKind.Unspecified);
		return true;
		}
	private static bool OptionalTime (string text, int min, int max, bool minutes, out TimeSpan? value)
		{
		value = null;
		if (string.IsNullOrWhiteSpace (text))
			return true;
		if (!int.TryParse (text.Trim (), NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < min || number > max)
			return false;
		value = minutes ? TimeSpan.FromMinutes (number) : TimeSpan.FromSeconds (number);
		return true;
		}
	}
// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.Threading.Tasks;

namespace RainPointClient.Desktop.Core;

public sealed partial class Dashboard
	{
	private RainPointScheduleSnapshot? _moistureRuleSnapshot;
	private int _moistureRuleZone = 1;
	private bool _moistureRuleEnabled, _moistureRuleMisting;
	private string _moistureBelow = "30", _moistureMinutes = "10", _moistureLitres = "", _moistureFrom = "", _moistureUntil = "";
	public int MoistureRuleZone
		{
		get => _moistureRuleZone; set
			{
			if (!CanEdit || value is < 1 or > 3 || value == _moistureRuleZone)
				return;
			_moistureRuleZone = value;
			ClearMoistureRule ();
			Changed ();
			}
		}
	public bool MoistureRuleEnabled
		{
		get => _moistureRuleEnabled; set
			{
			if (!CanEdit)
				return;
			_moistureRuleEnabled = value;
			Changed ();
			}
		}
	public bool MoistureRuleMisting
		{
		get => _moistureRuleMisting; set
			{
			if (!CanEdit)
				return;
			_moistureRuleMisting = value;
			Changed ();
			}
		}
	public string MoistureBelow
		{
		get => _moistureBelow; set
			{
			if (!CanEdit)
				return;
			_moistureBelow = value ?? "";
			Changed ();
			}
		}
	public string MoistureMinutes
		{
		get => _moistureMinutes; set
			{
			if (!CanEdit)
				return;
			_moistureMinutes = value ?? "";
			Changed ();
			}
		}
	public string MoistureLitres
		{
		get => _moistureLitres; set
			{
			if (!CanEdit)
				return;
			_moistureLitres = value ?? "";
			Changed ();
			}
		}
	public string MoistureFrom
		{
		get => _moistureFrom; set
			{
			if (!CanEdit)
				return;
			_moistureFrom = value ?? "";
			Changed ();
			}
		}
	public string MoistureUntil
		{
		get => _moistureUntil; set
			{
			if (!CanEdit)
				return;
			_moistureUntil = value ?? "";
			Changed ();
			}
		}
	public bool CanLoadMoistureRule => CanReadSettings;
	public bool CanSaveMoistureRule => CanLoadMoistureRule && _moistureRuleSnapshot is not null && TryMoistureRule (out _);
	public string MoistureRuleMessage { get; private set; } = "Load the selected zone's automatic watering rule.";
	private void ClearMoistureRule ()
		{
		_moistureRuleSnapshot = null;
		_moistureRuleEnabled = _moistureRuleMisting = false;
		_moistureBelow = "30";
		_moistureMinutes = "10";
		_moistureLitres = _moistureFrom = _moistureUntil = "";
		MoistureRuleMessage = "Load the selected zone's automatic watering rule.";
		}
	private bool TryMoistureRule (out RainPointMoistureWateringRule? rule)
		{
		rule = null;
		if (_moistureRuleSnapshot is null || !int.TryParse (_moistureBelow.Trim (), NumberStyles.None, CultureInfo.InvariantCulture, out int threshold))
			return false;
		TimeSpan? duration = null;
		if (!string.IsNullOrWhiteSpace (_moistureMinutes))
			{
			if (!int.TryParse (_moistureMinutes.Trim (), NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) || minutes is < 1 or > 30)
				return false;
			duration = TimeSpan.FromMinutes (minutes);
			}
		decimal? volume = null;
		if (!string.IsNullOrWhiteSpace (_moistureLitres))
			{
			if (!decimal.TryParse (_moistureLitres.Trim (), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal litres))
				return false;
			volume = litres;
			}
		if (!RuleTime (_moistureFrom, out TimeSpan? from) || !RuleTime (_moistureUntil, out TimeSpan? until))
			return false;
		var value = new RainPointMoistureWateringRule { Enabled = _moistureRuleEnabled, StartBelowMoisturePercent = threshold, Mode = _moistureRuleMisting ? RainPointScheduleMode.Misting : RainPointScheduleMode.Irrigation, Duration = duration, WaterLimitLitres = volume, ExcludedFrom = from, ExcludedUntil = until };
		if (_timer?.SupportsManualCycles != true || _moistureRuleSnapshot.Availability != TimerReadingAvailability.Decoded || threshold is < 1 or > 99)
			return false;
		if (volume.HasValue && (volume < .3m || volume > 6000m || volume.Value * 10 != decimal.Truncate (volume.Value * 10)))
			return false;
		if (!duration.HasValue && !volume.HasValue)
			return false;
		if (_moistureRuleEnabled && _moistureRuleSnapshot.SoilSensorSettings?.SensorAddress is null)
			return false;
		if (from.HasValue != until.HasValue || (from.HasValue && from == until))
			return false;
		if (from.HasValue && duration.HasValue)
			{
			int window = from > until ? (int)(from.Value - until!.Value).TotalMinutes : 1440 - (int)(until!.Value - from.Value).TotalMinutes;
			if (duration.Value.TotalMinutes > window)
				return false;
			}
		rule = value;
		return true;
		}
	private static bool RuleTime (string text, out TimeSpan? value)
		{
		value = null;
		if (string.IsNullOrWhiteSpace (text))
			return true;
		if (!DateTime.TryParseExact (text.Trim (), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time))
			return false;
		value = time.TimeOfDay;
		return true;
		}
	public Task LoadMoistureRuleAsync ()
		{
		if (!CanLoadMoistureRule)
			return Task.CompletedTask;
		return RunAsync (async token =>
			{
				ClearMoistureRule ();
				Changed ();
				var snapshot = await _client.GetTimerSchedulesAsync (_hub!, _timer!.Address, _moistureRuleZone, token);
				if (snapshot.MoistureRuleAvailability is not (TimerReadingAvailability.Decoded or TimerReadingAvailability.NotReported))
					{
					MoistureRuleMessage = "This rule is unreadable or unsupported; it cannot be overwritten.";
					return;
					}
				_moistureRuleSnapshot = snapshot;
				var rule = snapshot.MoistureWateringRule ?? new RainPointMoistureWateringRule ();
				_moistureRuleEnabled = rule.Enabled;
				_moistureRuleMisting = rule.Mode == RainPointScheduleMode.Misting;
				_moistureBelow = rule.StartBelowMoisturePercent.ToString (CultureInfo.InvariantCulture);
				_moistureMinutes = rule.Duration?.TotalMinutes.ToString (CultureInfo.InvariantCulture) ?? "";
				_moistureLitres = rule.WaterLimitLitres?.ToString (CultureInfo.InvariantCulture) ?? "";
				_moistureFrom = rule.ExcludedFrom?.ToString (@"hh\:mm") ?? "";
				_moistureUntil = rule.ExcludedUntil?.ToString (@"hh\:mm") ?? "";
				MoistureRuleMessage = snapshot.MoistureRuleAvailability == TimerReadingAvailability.NotReported ? "No saved rule reported. This disabled draft is not saved." : "Saved rule loaded. Enabling it can cause future watering.";
				Changed ();
			}, "Could not read the automatic watering rule.");
		}
	public Task SaveMoistureRuleAsync ()
		{
		if (!CanSaveMoistureRule || !TryMoistureRule (out var rule))
			return Task.CompletedTask;
		var snapshot = _moistureRuleSnapshot!;
		var hub = _hub!;
		return RunAsync (async token =>
			{
				_moistureRuleSnapshot = null;
				ClearCalendar ();
				Changed ();
				try
					{
					await _client.SetTimerMoistureWateringRuleAsync (hub, snapshot, rule!, token);
					var after = await _client.GetTimerSchedulesAsync (hub, snapshot.Address, snapshot.Zone, token);
					var saved = after.MoistureWateringRule;
					bool matches = after.MoistureRuleAvailability == TimerReadingAvailability.Decoded && saved is not null && saved.Enabled == rule!.Enabled && saved.StartBelowMoisturePercent == rule.StartBelowMoisturePercent && saved.Mode == rule.Mode && saved.Duration == rule.Duration && saved.WaterLimitLitres == rule.WaterLimitLitres && saved.ExcludedFrom == rule.ExcludedFrom && saved.ExcludedUntil == rule.ExcludedUntil;
					MoistureRuleMessage = matches ? "Rule matches cloud read-back. Reload before another edit." : "Cloud read-back differs. Reload to reconcile; no retry was sent.";
					}
				catch { MoistureRuleMessage = "Rule outcome may be unknown. Reload before another attempt; nothing was retried."; throw; }
				Changed ();
			}, "Automatic watering rule save did not complete.");
		}
	}
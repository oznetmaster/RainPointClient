using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace RainPointClient.Desktop.Core;

public sealed class PlanWeekday : INotifyPropertyChanged
	{
	internal PlanWeekday (DayOfWeek day)
		{
		Day = day;
		Name = day.ToString ();
		}
	internal DayOfWeek Day
		{
		get;
		}
	public string Name
		{
		get;
		}
	private bool _selected;
	public bool Selected
		{
		get => _selected; set
			{
			_selected = value;
			PropertyChanged?.Invoke (this, new PropertyChangedEventArgs (nameof (Selected)));
			}
		}
	public event PropertyChangedEventHandler? PropertyChanged;
	}

public sealed class PlanDraft : INotifyPropertyChanged
	{
	private string _mode = "Normal irrigation", _repeat = "Every day", _start = "08:00", _duration = "60", _water = "5", _pause = "30", _interval = "1", _date = string.Empty, _volume = string.Empty;
	private bool _enabled;
	private string? _unsupported;
	public PlanDraft ()
		{
		foreach (var day in Weekdays)
			day.PropertyChanged += (_, _) => Changed ();
		}
	public event PropertyChangedEventHandler? PropertyChanged;
	public IReadOnlyList<string> Modes { get; } = Array.AsReadOnly (new[] { "Normal irrigation", "Cycle and soak", "Misting" });
	public IReadOnlyList<string> Repeats { get; } = Array.AsReadOnly (new[] { "Every day", "Odd days", "Even days", "Selected weekdays", "Every N days" });
	public IReadOnlyList<PlanWeekday> Weekdays { get; } = Array.AsReadOnly (new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }.Select (day => new PlanWeekday (day)).ToArray ());
	public string Mode
		{
		get => _mode;
		set
			{
			if (!Modes.Contains (value) || value == _mode)
				return;
			_mode = value;
			_duration = value == "Normal irrigation" ? "60" : "10";
			_water = value == "Misting" ? "10" : "5";
			_pause = value == "Misting" ? "20" : "30";
			_volume = string.Empty;
			Changed ();
			}
		}
	public string Repeat
		{
		get => _repeat; set
			{
			if (!Repeats.Contains (value))
				return;
			_repeat = value;
			Changed ();
			}
		}
	public string Start
		{
		get => _start; set
			{
			_start = value ?? string.Empty;
			Changed ();
			}
		}
	public string Duration
		{
		get => _duration; set
			{
			_duration = value ?? string.Empty;
			Changed ();
			}
		}
	public string Water
		{
		get => _water; set
			{
			_water = value ?? string.Empty;
			Changed ();
			}
		}
	public string Pause
		{
		get => _pause; set
			{
			_pause = value ?? string.Empty;
			Changed ();
			}
		}
	public string Interval
		{
		get => _interval; set
			{
			_interval = value ?? string.Empty;
			Changed ();
			}
		}
	public string Date
		{
		get => _date; set
			{
			_date = value ?? string.Empty;
			Changed ();
			}
		}
	public string Volume
		{
		get => _volume; set
			{
			_volume = value ?? string.Empty;
			Changed ();
			}
		}
	public bool Enabled
		{
		get => _enabled; set
			{
			_enabled = value;
			Changed ();
			}
		}
	public bool HasCycles => _mode != "Normal irrigation";
	public bool HasWeekdays => _repeat == "Selected weekdays";
	public bool HasInterval => _repeat == "Every N days";
	public bool IsSupported => _unsupported is null;
	public string DurationLabel => _mode switch { "Normal irrigation" => "Duration (60–43200 seconds)", "Cycle and soak" => "Total watering (5–1440 minutes)", _ => "Duration (1–720 minutes)" };
	public string CycleLabel => _mode == "Misting" ? "Burst (5–3600 seconds)" : "Water per cycle (1–720 minutes)";
	public string PauseLabel => _mode == "Misting" ? "Pause (5–3600 seconds)" : "Soak per cycle (1–720 minutes)";
	public string Validation => TryBuild (out _, out string error) ? "Ready to save. Times use the home's local calendar." : error;
	private void Changed () => PropertyChanged?.Invoke (this, new PropertyChangedEventArgs (string.Empty));
	internal void Reset ()
		{
		_unsupported = null;
		_mode = "Normal irrigation";
		_repeat = "Every day";
		_start = "08:00";
		_duration = "60";
		_water = "5";
		_pause = "30";
		_interval = "1";
		_date = _volume = string.Empty;
		_enabled = false;
		foreach (var day in Weekdays)
			day.Selected = false;
		Changed ();
		}
	internal void Load (RainPointSchedule plan)
		{
		_unsupported = plan.Repeat == RainPointScheduleRepeat.Once ? "Once plans are not supported by this timer. You can view, disable or delete this record." : plan.Repeat == RainPointScheduleRepeat.IntervalHours ? "Hourly recurrence can be viewed, enabled/disabled or deleted, but cannot be replaced by this editor." : null;
		_mode = plan.Mode == RainPointScheduleMode.Irrigation ? "Normal irrigation" : plan.Mode == RainPointScheduleMode.Misting ? "Misting" : "Cycle and soak";
		_repeat = plan.Repeat is >= RainPointScheduleRepeat.EveryDay and <= RainPointScheduleRepeat.IntervalDays ? Repeats[(int)plan.Repeat - 1] : "Every day";
		_start = plan.StartTime.ToString (@"hh\:mm", CultureInfo.InvariantCulture);
		_duration = (HasCycles ? plan.Duration.TotalMinutes : plan.Duration.TotalSeconds).ToString ("0.########", CultureInfo.InvariantCulture);
		_water = (plan.Mode == RainPointScheduleMode.Misting ? plan.CycleWateringTime?.TotalSeconds : plan.CycleWateringTime?.TotalMinutes)?.ToString ("0.########", CultureInfo.InvariantCulture) ?? string.Empty;
		_pause = (plan.Mode == RainPointScheduleMode.Misting ? plan.CyclePauseTime?.TotalSeconds : plan.CyclePauseTime?.TotalMinutes)?.ToString ("0.########", CultureInfo.InvariantCulture) ?? string.Empty;
		_interval = plan.Interval?.ToString (CultureInfo.InvariantCulture) ?? "1";
		_date = plan.EffectiveDate?.ToString ("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
		_volume = plan.WaterLimitLitres?.ToString (CultureInfo.InvariantCulture) ?? string.Empty;
		_enabled = plan.Enabled;
		foreach (var day in Weekdays)
			day.Selected = plan.Weekdays.Contains (day.Day);
		Changed ();
		}
	internal bool TryBuild (out PlanValue? result, out string error)
		{
		result = null;
		error = _unsupported ?? string.Empty;
		if (_unsupported is not null)
			return false;
		if (!TimeSpan.TryParseExact (_start, @"hh\:mm", CultureInfo.InvariantCulture, out TimeSpan start) || start >= TimeSpan.FromDays (1))
			{
			error = "Use a start time from 00:00 to 23:59 (HH:mm).";
			return false;
			}
		int max = _mode == "Normal irrigation" ? 43200 : _mode == "Misting" ? 720 : 1440;
		int min = _mode == "Normal irrigation" ? 60 : _mode == "Misting" ? 1 : 5;
		if (!Integer (_duration, min, max, out int duration))
			{
			error = "Enter a whole duration within the displayed limits.";
			return false;
			}
		int water = 0, pause = 0;
		if (HasCycles && (!Integer (_water, _mode == "Misting" ? 5 : 1, _mode == "Misting" ? 3600 : 720, out water) || !Integer (_pause, _mode == "Misting" ? 5 : 1, _mode == "Misting" ? 3600 : 720, out pause) || (_mode == "Cycle and soak" && water > duration)))
			{
			error = "Enter valid watering and pause intervals; a cycle cannot exceed total watering.";
			return false;
			}
		var repeat = (RainPointScheduleRepeat)(Array.IndexOf (Repeats.ToArray (), _repeat) + 1);
		DayOfWeek[] days = HasWeekdays ? Weekdays.Where (day => day.Selected).Select (day => day.Day).OrderBy (day => day).ToArray () : Array.Empty<DayOfWeek> ();
		if (HasWeekdays && days.Length == 0)
			{
			error = "Select at least one weekday.";
			return false;
			}
		int? interval = null;
		if (HasInterval)
			{
			if (!Integer (_interval, 1, 127, out int n))
				{
				error = "Use a repeat interval from 1 to 127 days.";
				return false;
				}
			interval = n;
			}
		DateTime? date = null;
		if (!string.IsNullOrWhiteSpace (_date))
			{
			if (!DateTime.TryParseExact (_date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d) || d.Year is < 2020 or > 2083)
				{
				error = "Use a home-calendar date in 2020–2083 (yyyy-MM-dd).";
				return false;
				}
			date = d;
			}
		if (!date.HasValue && (repeat == RainPointScheduleRepeat.Once || HasInterval))
			{
			error = "Once and every-N-days plans need an effective date.";
			return false;
			}
		decimal? volume = null;
		if (!string.IsNullOrWhiteSpace (_volume))
			{
			if (!decimal.TryParse (_volume, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal v) || v < 0.3m || v > 6000m || decimal.Truncate (v * 10) != v * 10)
				{
				error = "Use 0.3–6000 litres in 0.1 L steps, with a decimal point; or leave blank.";
				return false;
				}
			volume = v;
			}
		if (_mode == "Cycle and soak")
			{
			int gap = repeat == RainPointScheduleRepeat.Once ? int.MaxValue : repeat == RainPointScheduleRepeat.EvenDays ? 2 : interval ?? 1;
			if (HasWeekdays)
				{
				gap = (int)days[0] + 7 - (int)days[days.Length - 1];
				for (int i = 1; i < days.Length; i++)
					gap = Math.Min (gap, (int)days[i] - (int)days[i - 1]);
				}
			long elapsed = duration + ((duration + water - 1L) / water - 1) * pause;
			if (elapsed > (long)gap * 1440)
				{
				error = "Watering plus soaking must fit before the next repeat at 100% seasonal adjustment.";
				return false;
				}
			}
		result = new PlanValue
			{
			Mode = _mode == "Normal irrigation" ? RainPointScheduleMode.Irrigation : _mode == "Misting" ? RainPointScheduleMode.Misting : RainPointScheduleMode.CycleAndSoak,
			Enabled = _enabled,
			Start = start,
			Duration = HasCycles ? TimeSpan.FromMinutes (duration) : TimeSpan.FromSeconds (duration),
			Repeat = repeat,
			Weekdays = days,
			Interval = interval,
			Date = date,
			Volume = volume,
			Water = HasCycles ? (_mode == "Misting" ? TimeSpan.FromSeconds (water) : TimeSpan.FromMinutes (water)) : null,
			Pause = HasCycles ? (_mode == "Misting" ? TimeSpan.FromSeconds (pause) : TimeSpan.FromMinutes (pause)) : null
			};
		return true;
		}
	private static bool Integer (string text, int min, int max, out int n) => int.TryParse (text, NumberStyles.None, CultureInfo.InvariantCulture, out n) && n >= min && n <= max;
	}

internal sealed class PlanValue
	{
	internal RainPointScheduleMode Mode; internal bool Enabled; internal TimeSpan Start, Duration; internal RainPointScheduleRepeat Repeat;
	internal DayOfWeek[] Weekdays = Array.Empty<DayOfWeek> (); internal int? Interval; internal DateTime? Date; internal decimal? Volume; internal TimeSpan? Water, Pause;
	internal RainPointIrrigationSchedule Normal () => new () { Enabled = Enabled, StartTime = Start, Duration = Duration, Repeat = Repeat, Weekdays = Weekdays, Interval = Interval, EffectiveDate = Date, WaterLimitLitres = Volume };
	internal RainPointCycleAndSoakSchedule Cycle () => new () { Enabled = Enabled, StartTime = Start, Duration = Duration, Repeat = Repeat, Weekdays = Weekdays, Interval = Interval, EffectiveDate = Date, WaterLimitLitres = Volume, CycleWateringTime = Water!.Value, CyclePauseTime = Pause!.Value };
	internal RainPointMistingSchedule Mist () => new () { Enabled = Enabled, StartTime = Start, Duration = Duration, Repeat = Repeat, Weekdays = Weekdays, Interval = Interval, EffectiveDate = Date, WaterLimitLitres = Volume, CycleWateringTime = Water!.Value, CyclePauseTime = Pause!.Value };
	internal bool Matches (RainPointSchedule p) => p.Mode == Mode && p.Enabled == Enabled && p.StartTime == Start && p.Duration == Duration && p.Repeat == Repeat && p.Interval == Interval && p.EffectiveDate == Date && p.WaterLimitLitres == Volume && p.CycleWateringTime == Water && p.CyclePauseTime == Pause && p.Weekdays.OrderBy (d => d).SequenceEqual (Weekdays);
	}
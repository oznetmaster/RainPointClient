// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;

namespace RainPointClient;

public enum RainPointScheduleMode
	{
	Irrigation = 1,
	Misting = 2,
	CycleAndSoak = 3
	}

public enum RainPointScheduleRepeat
	{
	/// <summary>Readable common-protocol value. Creation and enabling are unsupported for HTV345FRF.</summary>
	Once,
	EveryDay,
	OddDays,
	EvenDays,
	Weekdays,
	IntervalDays,
	IntervalHours
	}

/// <summary>Saved cloud configuration for one zone, not confirmation of RF delivery or execution.</summary>
public sealed class RainPointScheduleSnapshot
	{
	internal long HomeId
		{
		get; set;
		}
	internal long HubId
		{
		get; set;
		}
	internal long? DeviceId
		{
		get; set;
		}
	internal string? Parameter
		{
		get; set;
		}
	internal string? FirmwareVersion
		{
		get; set;
		}
	internal int? PortNumber
		{
		get; set;
		}
	/// <summary>Availability of sensor association and moisture stop settings, separate from live sensor data.</summary>
	public TimerReadingAvailability SoilSensorAvailability
		{
		get; internal set;
		}
	public RainPointSoilSensorSettings? SoilSensorSettings
		{
		get; internal set;
		}
	public TimerReadingAvailability SeasonalAdjustmentAvailability
		{
		get; internal set;
		}
	/// <summary>January through December percentages. Empty when unavailable; never inferred as 100%.</summary>
	public IReadOnlyList<int> SeasonalPercentages { get; internal set; } = Array.AsReadOnly (Array.Empty<int> ());
	public TimerReadingAvailability RainDelayAvailability
		{
		get; internal set;
		}
	/// <summary>Home-local end time with Unspecified kind; null for an explicit zero field. Compare with the home's current local time to determine activity. An expired value is retained.</summary>
	public DateTime? RainDelayUntil
		{
		get; internal set;
		}


	/// <summary>Availability of the zone-default configuration, independent of seasonal adjustment and rain delay.</summary>
	public TimerReadingAvailability ZoneDefaultsAvailability
		{
		get; internal set;
		}
	/// <summary>Null when defaults are absent, malformed or unsupported. Individual null durations denote encoded app-default sentinels.</summary>
	public RainPointZoneDefaults? ZoneDefaults
		{
		get; internal set;
		}


	/// <summary>Availability of the saved flow-calibration percentage.</summary>
	public TimerReadingAvailability FlowCalibrationAvailability
		{
		get; internal set;
		}
	/// <summary>Signed correction percentage, -20..20. Null when not decoded; zero is an explicit neutral setting.</summary>
	public int? FlowCalibrationPercent
		{
		get; internal set;
		}

	public TimerReadingAvailability MoistureRuleAvailability
		{
		get; internal set;
		}
	/// <summary>Decoded rule values, or null when absent/unreadable. Editing this object alone does not write to the device.</summary>
	public RainPointMoistureWateringRule? MoistureWateringRule
		{
		get; internal set;
		}
	internal int WriteAttempted;

	internal RainPointScheduleSnapshot (int address, int zone, TimerReadingAvailability availability,
		 IReadOnlyList<RainPointSchedule> schedules)
		{
		Address = address;
		Zone = zone;
		Availability = availability;
		Schedules = schedules;
		}

	public int Address
		{
		get;
		}
	public int Zone
		{
		get;
		}
	/// <summary>Only Decoded with an empty list means the zone has no saved schedules.</summary>
	public TimerReadingAvailability Availability
		{
		get;
		}
	public IReadOnlyList<RainPointSchedule> Schedules
		{
		get;
		}
	}

/// <summary>A saved plan. Times and dates use the home's local calendar, without timezone conversion.</summary>
public sealed class RainPointSchedule
	{
	internal RainPointSchedule (int index, bool enabled, RainPointScheduleMode mode, TimeSpan startTime,
		 TimeSpan duration, RainPointScheduleRepeat repeat, IReadOnlyList<DayOfWeek> weekdays,
		 int? interval, decimal? waterLimit, DateTime? effectiveDate, TimeSpan? wateringTime, TimeSpan? pauseTime)
		{
		Index = index;
		Enabled = enabled;
		Mode = mode;
		StartTime = startTime;
		Duration = duration;
		Repeat = repeat;
		Weekdays = weekdays;
		Interval = interval;
		WaterLimitLitres = waterLimit;
		EffectiveDate = effectiveDate;
		CycleWateringTime = wateringTime;
		CyclePauseTime = pauseTime;
		}

	/// <summary>Zero-based position in the returned configuration; not a durable plan identifier.</summary>
	public int Index
		{
		get;
		}
	public bool Enabled
		{
		get;
		}
	public RainPointScheduleMode Mode
		{
		get;
		}
	public TimeSpan StartTime
		{
		get;
		}
	/// <summary>Configured duration. For cycle/soak this is total watering time, excluding pauses.</summary>
	public TimeSpan Duration
		{
		get;
		}
	public RainPointScheduleRepeat Repeat
		{
		get;
		}
	public IReadOnlyList<DayOfWeek> Weekdays
		{
		get;
		}
	/// <summary>Repeat interval in days or hours according to Repeat; null for other repeat modes.</summary>
	public int? Interval
		{
		get;
		}
	/// <summary>Positive water limit in litres; null when absent or configured for duration.</summary>
	public decimal? WaterLimitLitres
		{
		get;
		}
	/// <summary>Local date with Unspecified kind; null when absent or disabled in the wire format.</summary>
	public DateTime? EffectiveDate
		{
		get;
		}
	public TimeSpan? CycleWateringTime
		{
		get;
		}
	public TimeSpan? CyclePauseTime
		{
		get;
		}
	}

/// <summary>A normal-irrigation plan to create or replace. Disabled by default.</summary>
public sealed class RainPointIrrigationSchedule
	{
	public bool Enabled
		{
		get; set;
		}
	/// <summary>Home-local start time, from midnight to 23:59, in whole minutes.</summary>
	public TimeSpan StartTime { get; set; } = TimeSpan.FromHours (8);
	/// <summary>Whole seconds from one minute to twelve hours.</summary>
	public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes (1);
	public RainPointScheduleRepeat Repeat { get; set; } = RainPointScheduleRepeat.EveryDay;
	public IReadOnlyList<DayOfWeek> Weekdays { get; set; } = [];
	/// <summary>Required for IntervalDays (1..127); null for other supported repeats.</summary>
	public int? Interval
		{
		get; set;
		}
	/// <summary>Optional 0.3..6000 litre limit in 0.1 litre steps. Watering ends at the volume or duration limit.</summary>
	public decimal? WaterLimitLitres
		{
		get; set;
		}
	/// <summary>Optional home-local date (2020..2083), with Unspecified kind and no time component.</summary>
	public DateTime? EffectiveDate
		{
		get; set;
		}
	}

/// <summary>A cycle-and-soak plan with an optional volume limit to create or replace. Disabled by default.</summary>
public sealed class RainPointCycleAndSoakSchedule
	{
	public bool Enabled
		{
		get; set;
		}
	/// <summary>Home-local start time, from midnight to 23:59, in whole minutes.</summary>
	public TimeSpan StartTime { get; set; } = TimeSpan.FromHours (8);
	/// <summary>Total watering time excluding pauses: 5..1440 whole minutes.</summary>
	public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes (10);
	/// <summary>Watering per cycle: 1..720 whole minutes, no greater than Duration.</summary>
	public TimeSpan CycleWateringTime { get; set; } = TimeSpan.FromMinutes (5);
	/// <summary>Pause between cycles: 1..720 whole minutes. There is no pause after the last cycle.</summary>
	public TimeSpan CyclePauseTime { get; set; } = TimeSpan.FromMinutes (30);
	public RainPointScheduleRepeat Repeat { get; set; } = RainPointScheduleRepeat.EveryDay;
	public IReadOnlyList<DayOfWeek> Weekdays { get; set; } = [];
	/// <summary>Required for IntervalDays (1..127); null for other supported repeats.</summary>
	public int? Interval
		{
		get; set;
		}
	/// <summary>Optional 0.3..6000 litre limit in 0.1 litre steps. Watering ends at the volume or duration limit.</summary>
	public decimal? WaterLimitLitres
		{
		get; set;
		}
	/// <summary>Home-local midnight date in 2020..2083, Unspecified kind; required for IntervalDays.</summary>
	public DateTime? EffectiveDate
		{
		get; set;
		}
	}

/// <summary>A misting plan with an optional volume limit to create or replace. Disabled by default.</summary>
public sealed class RainPointMistingSchedule
	{
	public bool Enabled
		{
		get; set;
		}
	/// <summary>Home-local start time, from midnight to 23:59, in whole minutes.</summary>
	public TimeSpan StartTime { get; set; } = TimeSpan.FromHours (8);
	/// <summary>Configured misting duration: 1..720 whole minutes. Physical treatment of pauses remains unverified.</summary>
	public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes (10);
	/// <summary>Each misting burst: 5..3600 whole seconds.</summary>
	public TimeSpan CycleWateringTime { get; set; } = TimeSpan.FromSeconds (10);
	/// <summary>Pause between misting bursts: 5..3600 whole seconds.</summary>
	public TimeSpan CyclePauseTime { get; set; } = TimeSpan.FromSeconds (20);
	public RainPointScheduleRepeat Repeat { get; set; } = RainPointScheduleRepeat.EveryDay;
	public IReadOnlyList<DayOfWeek> Weekdays { get; set; } = [];
	/// <summary>Required for IntervalDays (1..127); null for other supported repeats.</summary>
	public int? Interval
		{
		get; set;
		}
	/// <summary>Optional 0.3..6000 litre limit in 0.1 litre steps. Watering ends at the volume or duration limit.</summary>
	public decimal? WaterLimitLitres
		{
		get; set;
		}
	/// <summary>Home-local midnight date in 2020..2083, Unspecified kind; required for IntervalDays.</summary>
	public DateTime? EffectiveDate
		{
		get; set;
		}
	}
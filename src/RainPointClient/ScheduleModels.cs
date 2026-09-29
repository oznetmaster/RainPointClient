// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;

namespace RainPointClient;

/// <summary>
/// Selects normal irrigation, misting or cycle-and-soak for a saved plan.
/// </summary>
public enum RainPointScheduleMode
	{
	/// <summary>A continuous normal-irrigation plan.</summary>
	Irrigation = 1,
	/// <summary>A misting plan with alternating watering bursts and pauses.</summary>
	Misting = 2,
	/// <summary>A plan alternating watering cycles and soaking pauses.</summary>
	CycleAndSoak = 3
	}

/// <summary>
/// Describes a plan's repeat pattern; readable values can exceed the supported write capabilities.
/// </summary>
public enum RainPointScheduleRepeat
	{
	/// <summary>Readable common-protocol value. Creation and enabling are unsupported for HTV345FRF.</summary>
	Once,
	/// <summary>
	/// Repeat every home-local calendar day.
	/// </summary>
	EveryDay,
	/// <summary>
	/// Repeat on odd-numbered dates according to the vendor calendar rules.
	/// </summary>
	OddDays,
	/// <summary>
	/// Repeat on even-numbered dates according to the vendor calendar rules.
	/// </summary>
	EvenDays,
	/// <summary>
	/// Repeat on the selected days of the week.
	/// </summary>
	Weekdays,
	/// <summary>
	/// Repeat at the configured whole-day interval.
	/// </summary>
	IntervalDays,
	/// <summary>
	/// Readable protocol value for an hour interval; current plan writers do not enable this recurrence.
	/// </summary>
	IntervalHours
	}

/// <summary>Saved cloud configuration for one zone, not confirmation of RF delivery or execution.</summary>
public sealed class RainPointScheduleSnapshot
	{
	/// <summary>
	/// Stores the home id for rainpoint schedule snapshot.
	/// </summary>
	internal long HomeId
		{
		get; set;
		}
	/// <summary>
	/// Stores the hub id for rainpoint schedule snapshot.
	/// </summary>
	internal long HubId
		{
		get; set;
		}
	/// <summary>
	/// Stores the device id for rainpoint schedule snapshot.
	/// </summary>
	internal long? DeviceId
		{
		get; set;
		}
	/// <summary>
	/// Stores the original encoded configuration field for bounded decoding and guarded updates.
	/// </summary>
	internal string? Parameter
		{
		get; set;
		}
	/// <summary>
	/// Stores the firmware version for rainpoint schedule snapshot.
	/// </summary>
	internal string? FirmwareVersion
		{
		get; set;
		}
	/// <summary>
	/// Stores the reported port count used to validate configuration section boundaries.
	/// </summary>
	internal int? PortNumber
		{
		get; set;
		}
	/// <summary>Availability of sensor association and moisture stop settings, separate from live sensor data.</summary>
	public TimerReadingAvailability SoilSensorAvailability
		{
		get; internal set;
		}
	/// <summary>
	/// Gets decoded sensor association and moisture-stop settings, or null when not decoded.
	/// </summary>
	public RainPointSoilSensorSettings? SoilSensorSettings
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the independent decoding state of monthly seasonal percentages.
	/// </summary>
	public TimerReadingAvailability SeasonalAdjustmentAvailability
		{
		get; internal set;
		}
	/// <summary>January through December percentages. Empty when unavailable; never inferred as 100%.</summary>
	public IReadOnlyList<int> SeasonalPercentages { get; internal set; } = Array.AsReadOnly (Array.Empty<int> ());
	/// <summary>
	/// Gets the independent decoding state of the saved rain-delay field.
	/// </summary>
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

	/// <summary>
	/// Gets the independent decoding state of automatic low-moisture watering settings.
	/// </summary>
	public TimerReadingAvailability MoistureRuleAvailability
		{
		get; internal set;
		}
	/// <summary>Decoded rule values, or null when absent/unreadable. Editing this object alone does not write to the device.</summary>
	public RainPointMoistureWateringRule? MoistureWateringRule
		{
		get; internal set;
		}
	/// <summary>
	/// Tracks whether this configuration snapshot has already been used for a write attempt.
	/// </summary>
	internal int WriteAttempted;

	/// <summary>
	/// Initializes schedule snapshot from the supplied typed values.
	/// </summary>
	/// <param name="address">The paired child's RF address within its hub, distinct from its cloud database ID.</param>
	/// <param name="zone">The one-based zone number on the selected timer.</param>
	/// <param name="availability">The decoding result; unavailable data must not be interpreted as an empty configuration or closed valve.</param>
	/// <param name="schedules">The decoded saved plans in configuration order.</param>
	internal RainPointScheduleSnapshot (int address, int zone, TimerReadingAvailability availability,
		 IReadOnlyList<RainPointSchedule> schedules)
		{
		Address = address;
		Zone = zone;
		Availability = availability;
		Schedules = schedules;
		}

	/// <summary>
	/// Gets the timer's RF address within its hub.
	/// </summary>
	public int Address
		{
		get;
		}
	/// <summary>
	/// Gets the one-based zone whose configuration was observed.
	/// </summary>
	public int Zone
		{
		get;
		}
	/// <summary>Only Decoded with an empty list means the zone has no saved schedules.</summary>
	public TimerReadingAvailability Availability
		{
		get;
		}
	/// <summary>
	/// Gets the decoded saved plans in configuration order; an empty list means no plans only when availability is Decoded.
	/// </summary>
	public IReadOnlyList<RainPointSchedule> Schedules
		{
		get;
		}
	}

/// <summary>A saved plan. Times and dates use the home's local calendar, without timezone conversion.</summary>
public sealed class RainPointSchedule
	{
	/// <summary>
	/// Initializes schedule from the supplied typed values.
	/// </summary>
	/// <param name="index">The zero-based plan position in this snapshot, not a durable plan identifier.</param>
	/// <param name="enabled">Whether the selected feature or saved plan should be enabled.</param>
	/// <param name="mode">The decoded watering mode of this plan.</param>
	/// <param name="startTime">The plan's start time of day in the home's local calendar.</param>
	/// <param name="duration">The configured watering duration; use the operation's documented range and mode-specific treatment of pauses.</param>
	/// <param name="repeat">The recurrence pattern; the operation validates the supported combinations.</param>
	/// <param name="weekdays">The selected weekdays, used only with a weekly recurrence.</param>
	/// <param name="interval">The repeat interval in days or hours as indicated by the recurrence, or null for other patterns.</param>
	/// <param name="waterLimit">An optional positive volume limit in litres; null selects duration-only watering.</param>
	/// <param name="effectiveDate">The optional home-local effective date at midnight with Unspecified kind.</param>
	/// <param name="wateringTime">The watering burst length for a cyclic operation, in the mode-specific range documented above.</param>
	/// <param name="pauseTime">The pause between bursts, in the mode-specific range documented above.</param>
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
	/// <summary>
	/// Gets whether this saved plan is enabled in cloud configuration.
	/// </summary>
	public bool Enabled
		{
		get;
		}
	/// <summary>
	/// Gets the decoded irrigation, misting or cycle-and-soak mode.
	/// </summary>
	public RainPointScheduleMode Mode
		{
		get;
		}
	/// <summary>
	/// Gets the scheduled time of day in the home's local calendar.
	/// </summary>
	public TimeSpan StartTime
		{
		get;
		}
	/// <summary>Configured duration. For cycle/soak this is total watering time, excluding pauses.</summary>
	public TimeSpan Duration
		{
		get;
		}
	/// <summary>
	/// Gets the plan's decoded recurrence pattern.
	/// </summary>
	public RainPointScheduleRepeat Repeat
		{
		get;
		}
	/// <summary>
	/// Gets the selected days for weekly recurrence; other patterns need not supply days.
	/// </summary>
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
	/// <summary>
	/// Gets the watering burst duration for a cyclic plan, or null when not present.
	/// </summary>
	public TimeSpan? CycleWateringTime
		{
		get;
		}
	/// <summary>
	/// Gets the pause between watering bursts, or null when not present.
	/// </summary>
	public TimeSpan? CyclePauseTime
		{
		get;
		}
	}

/// <summary>A normal-irrigation plan to create or replace. Disabled by default.</summary>
public sealed class RainPointIrrigationSchedule
	{
	/// <summary>
	/// Gets or sets whether the submitted plan is enabled; defaults to false.
	/// </summary>
	public bool Enabled
		{
		get; set;
		}
	/// <summary>Home-local start time, from midnight to 23:59, in whole minutes.</summary>
	public TimeSpan StartTime { get; set; } = TimeSpan.FromHours (8);
	/// <summary>Whole seconds from one minute to twelve hours.</summary>
	public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes (1);
	/// <summary>
	/// Gets or sets the recurrence pattern; defaults to every day. The write operation validates supported patterns.
	/// </summary>
	public RainPointScheduleRepeat Repeat { get; set; } = RainPointScheduleRepeat.EveryDay;
	/// <summary>
	/// Gets or sets the selected days for weekly recurrence; leave empty for other supported patterns.
	/// </summary>
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
	/// <summary>
	/// Gets or sets whether the submitted plan is enabled; defaults to false.
	/// </summary>
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
	/// <summary>
	/// Gets or sets the recurrence pattern; defaults to every day. The write operation validates supported patterns.
	/// </summary>
	public RainPointScheduleRepeat Repeat { get; set; } = RainPointScheduleRepeat.EveryDay;
	/// <summary>
	/// Gets or sets the selected days for weekly recurrence; leave empty for other supported patterns.
	/// </summary>
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
	/// <summary>
	/// Gets or sets whether the submitted plan is enabled; defaults to false.
	/// </summary>
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
	/// <summary>
	/// Gets or sets the recurrence pattern; defaults to every day. The write operation validates supported patterns.
	/// </summary>
	public RainPointScheduleRepeat Repeat { get; set; } = RainPointScheduleRepeat.EveryDay;
	/// <summary>
	/// Gets or sets the selected days for weekly recurrence; leave empty for other supported patterns.
	/// </summary>
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
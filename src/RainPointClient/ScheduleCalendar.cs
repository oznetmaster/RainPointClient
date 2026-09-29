// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Linq;

namespace RainPointClient;

/// <summary>
/// Describes the rain-delay state known for a projected plan occurrence.
/// </summary>
public enum RainPointCalendarRainDelay
	{
	/// <summary>Rain-delay status is unavailable for this projection.</summary>
	Unknown,
	/// <summary>No rain-delay suppression is known for the projected occurrence.</summary>
	NotDelayed,
	/// <summary>The projected occurrence is suppressed by the known rain delay.</summary>
	Delayed
	}

/// <summary>A local-calendar projection of a saved plan, not confirmation that a valve will open.</summary>
public sealed class RainPointScheduleOccurrence
	{
	/// <summary>
	/// Initializes schedule occurrence from the supplied typed values.
	/// </summary>
	/// <param name="address">The paired child's RF address within its hub, distinct from its cloud database ID.</param>
	/// <param name="zone">The one-based zone number on the selected timer.</param>
	/// <param name="plan">The saved plan from which to project this occurrence.</param>
	/// <param name="start">The projected home-local start time with Unspecified kind.</param>
	/// <param name="percentage">The decoded monthly seasonal duration multiplier in percent, or null when unknown.</param>
	/// <param name="rainDelay">Whether known rain-delay settings cover the projected occurrence.</param>
	internal RainPointScheduleOccurrence (int address, int zone, RainPointSchedule plan, DateTime start, int? percentage, RainPointCalendarRainDelay rainDelay)
		{
		Address = address;
		Zone = zone;
		Plan = plan;
		StartsAt = start;
		SeasonalPercentage = percentage;
		RainDelay = rainDelay;
		// Matches the vendor calendar's display rounding; this is not elapsed time or metered flow.
		CalendarDuration = percentage.HasValue ? TimeSpan.FromMinutes ((double)Math.Max (1m, Math.Round ((decimal)plan.Duration.TotalMinutes * percentage.Value / 100m, 0, MidpointRounding.AwayFromZero))) : null;
		}
	/// <summary>
	/// Gets the RF address of the timer associated with this projected start.
	/// </summary>
	public int Address
		{
		get;
		}
	/// <summary>
	/// Gets the one-based zone associated with this projected start.
	/// </summary>
	public int Zone
		{
		get;
		}
	/// <summary>
	/// Gets the saved plan from which this occurrence was projected.
	/// </summary>
	public RainPointSchedule Plan
		{
		get;
		}
	/// <summary>Home-local wall time with Unspecified kind. No UTC/DST conversion is inferred.</summary>
	public DateTime StartsAt
		{
		get;
		}
	/// <summary>
	/// Gets the month's decoded duration multiplier in percent, or null when unavailable.
	/// </summary>
	public int? SeasonalPercentage
		{
		get;
		}
	/// <summary>Vendor-calendar display duration: scaled and rounded to whole minutes, minimum one. Null when seasonal settings are unavailable. Not an elapsed runtime or a volume-limit estimate.</summary>
	public TimeSpan? CalendarDuration
		{
		get;
		}
	/// <summary>
	/// Gets whether the projected start is covered by a known rain delay, or whether that setting is unknown.
	/// </summary>
	public RainPointCalendarRainDelay RainDelay
		{
		get;
		}
	}

/// <summary>A bounded, immutable calendar projection. Empty means no starts only when Availability is Decoded.</summary>
public sealed class RainPointCalendarPreview
	{
	/// <summary>
	/// Initializes calendar preview from the supplied typed values.
	/// </summary>
	/// <param name="from">The inclusive first home-local date at midnight with Unspecified kind.</param>
	/// <param name="through">The inclusive last home-local date at midnight with Unspecified kind.</param>
	/// <param name="availability">The decoding result; unavailable data must not be interpreted as an empty configuration or closed valve.</param>
	/// <param name="occurrences">The projected starts to copy into the bounded calendar result.</param>
	internal RainPointCalendarPreview (DateTime from, DateTime through, TimerReadingAvailability availability, IEnumerable<RainPointScheduleOccurrence> occurrences)
		{
		FromDate = from;
		ThroughDate = through;
		Availability = availability;
		Occurrences = Array.AsReadOnly (occurrences.ToArray ());
		}
	/// <summary>
	/// Gets the inclusive first home-local date of the projection.
	/// </summary>
	public DateTime FromDate
		{
		get;
		}
	/// <summary>
	/// Gets the inclusive last home-local date of the projection.
	/// </summary>
	public DateTime ThroughDate
		{
		get;
		}
	/// <summary>
	/// Gets whether the entire requested projection could be decoded without guessing unsupported recurrence.
	/// </summary>
	public TimerReadingAvailability Availability
		{
		get;
		}
	/// <summary>
	/// Gets projected starts within the bounded window; availability determines whether an empty list is meaningful.
	/// </summary>
	public IReadOnlyList<RainPointScheduleOccurrence> Occurrences
		{
		get;
		}
	/// <summary>First projected start at/after the supplied home-local wall time within this preview. Known rain-delayed starts are excluded. Inspect RainDelay: unknown settings do not imply no delay. Null is limited to this window, or unavailable projection.</summary>
	/// <param name="fromLocal">The home-local search instant within the preview window, with Unspecified kind.</param>
	/// <returns>The first eligible start in this preview, or null when unavailable or none exists within the bounded window.</returns>
	/// <exception cref="System.ArgumentOutOfRangeException">The search must begin within the preview window.</exception>
	public RainPointScheduleOccurrence? GetNextOccurrence (DateTime fromLocal)
		{
		ScheduleCalendar.RequireLocal (fromLocal, nameof (fromLocal));
		if (fromLocal.Date < FromDate || fromLocal.Date > ThroughDate)
			throw new ArgumentOutOfRangeException (nameof (fromLocal), "The search must begin within the preview window.");
		return Availability == TimerReadingAvailability.Decoded ? Occurrences.FirstOrDefault (item => item.StartsAt >= fromLocal && item.RainDelay != RainPointCalendarRainDelay.Delayed) : null;
		}
	}

/// <summary>Pure local-date calendar calculations from an existing typed snapshot; never performs I/O or executes plans.</summary>
public static class ScheduleCalendar
	{
	/// <summary>Projects enabled plans over inclusive home-local dates (1..366 days). Unsupported enabled recurrence invalidates the whole projection rather than returning a partial next-plan result.</summary>
	/// <param name="snapshot">The observed zone configuration; unrelated encoded fields are preserved when editing.</param>
	/// <param name="fromDate">The inclusive first home-local date at midnight with Unspecified kind.</param>
	/// <param name="throughDate">The inclusive final home-local date at midnight with Unspecified kind.</param>
	/// <returns>A bounded calendar projection with explicit availability; projected starts do not guarantee physical execution.</returns>
	public static RainPointCalendarPreview Create (RainPointScheduleSnapshot snapshot, DateTime fromDate, DateTime throughDate) => CreateCore (snapshot, fromDate, throughDate, null);

	/// <summary>Projects the vendor app calendar using reported home timezone rules, including its UTC-day interval calculation around daylight changes. This is not a firmware execution promise.</summary>
	/// <param name="snapshot">The observed zone configuration; unrelated encoded fields are preserved when editing.</param>
	/// <param name="fromDate">The inclusive first home-local date at midnight with Unspecified kind.</param>
	/// <param name="throughDate">The inclusive final home-local date at midnight with Unspecified kind.</param>
	/// <param name="timeZone">The home's reported offset and daylight-transition rules used for calendar calculations.</param>
	/// <returns>A bounded calendar projection with explicit availability; projected starts do not guarantee physical execution.</returns>
	/// <exception cref="System.ArgumentNullException">A required argument is null.</exception>
	public static RainPointCalendarPreview Create (RainPointScheduleSnapshot snapshot, DateTime fromDate, DateTime throughDate, RainPointCalendarTimeZone timeZone)
	 => CreateCore (snapshot, fromDate, throughDate, timeZone ?? throw new ArgumentNullException (nameof (timeZone)));

	private static RainPointCalendarPreview CreateCore (RainPointScheduleSnapshot snapshot, DateTime fromDate, DateTime throughDate, RainPointCalendarTimeZone? timeZone)
		{
		if (snapshot is null)
			throw new ArgumentNullException (nameof (snapshot));
		RequireLocal (fromDate, nameof (fromDate));
		RequireLocal (throughDate, nameof (throughDate));
		if (fromDate.TimeOfDay != TimeSpan.Zero || throughDate.TimeOfDay != TimeSpan.Zero)
			throw new ArgumentException ("Calendar bounds must be local midnight dates.");
		int days = (throughDate - fromDate).Days;
		if (days < 0 || days >= 366)
			throw new ArgumentOutOfRangeException (nameof (throughDate), "Choose an inclusive range of one to 366 days.");
		RainPointCalendarPreview Empty (TimerReadingAvailability state) => new (fromDate, throughDate, state, Array.Empty<RainPointScheduleOccurrence> ());
		if (snapshot.Availability != TimerReadingAvailability.Decoded)
			return Empty (snapshot.Availability);
		// This timer supports six plans per zone, so projection size is bounded too.
		if (snapshot.Schedules.Count > 6)
			return Empty (TimerReadingAvailability.UnsupportedFormat);
		RainPointSchedule[] plans = snapshot.Schedules.Where (plan => plan.Enabled).ToArray ();
		if (plans.Any (plan => plan.Repeat is < RainPointScheduleRepeat.Once or > RainPointScheduleRepeat.IntervalDays))
			return Empty (TimerReadingAvailability.UnsupportedFormat);
		if (plans.Any (plan => (plan.Repeat is RainPointScheduleRepeat.Once or RainPointScheduleRepeat.IntervalDays && plan.EffectiveDate is null)
		 || (plan.Repeat == RainPointScheduleRepeat.IntervalDays && plan.Interval is not (>= 1 and <= 127))))
			return Empty (TimerReadingAvailability.NotReported);
		List<RainPointScheduleOccurrence> items = [];
		for (int day = 0; day <= days; day++)
			{
			DateTime date = fromDate.AddDays (day);
			foreach (RainPointSchedule plan in plans)
				{
				if (plan.EffectiveDate is { } effective && date < effective)
					continue;
				bool matches = plan.Repeat switch
					{
						RainPointScheduleRepeat.Once => date == plan.EffectiveDate,
						RainPointScheduleRepeat.EveryDay => true,
						RainPointScheduleRepeat.OddDays => date.Day % 2 == 1,
						RainPointScheduleRepeat.EvenDays => date.Day % 2 == 0,
						RainPointScheduleRepeat.Weekdays => plan.Weekdays.Contains (date.DayOfWeek),
						RainPointScheduleRepeat.IntervalDays => (timeZone is null ? (date - plan.EffectiveDate!.Value).Days : timeZone.UtcDayNumber (date) - timeZone.UtcDayNumber (plan.EffectiveDate!.Value)) % plan.Interval!.Value == 0,
						_ => false
						};
				if (!matches)
					continue;
				DateTime start = date.Add (plan.StartTime);
				int? percent = snapshot.SeasonalAdjustmentAvailability == TimerReadingAvailability.Decoded && snapshot.SeasonalPercentages.Count == 12 ? snapshot.SeasonalPercentages[date.Month - 1] : null;
				RainPointCalendarRainDelay delay = snapshot.RainDelayAvailability != TimerReadingAvailability.Decoded ? RainPointCalendarRainDelay.Unknown
				 : snapshot.RainDelayUntil is { } until && start <= until ? RainPointCalendarRainDelay.Delayed : RainPointCalendarRainDelay.NotDelayed;
				items.Add (new (snapshot.Address, snapshot.Zone, plan, start, percent, delay));
				}
			}
		return new (fromDate, throughDate, TimerReadingAvailability.Decoded, items.OrderBy (item => item.StartsAt).ThenBy (item => item.Plan.Index));
		}
	/// <summary>
	/// Rejects a wall-clock value whose kind would imply an automatic UTC or machine-local conversion.
	/// </summary>
	/// <param name="value">The home-local date or wall time that must have Unspecified kind.</param>
	/// <param name="name">The parameter name to include in validation errors.</param>
	/// <exception cref="System.ArgumentException">Supply the home's local wall time with Unspecified kind; no timezone is inferred.</exception>
	internal static void RequireLocal (DateTime value, string name)
		{
		if (value.Kind != DateTimeKind.Unspecified)
			throw new ArgumentException ("Supply the home's local wall time with Unspecified kind; no timezone is inferred.", name);
		}
	}
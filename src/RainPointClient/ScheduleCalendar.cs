using System;
using System.Collections.Generic;
using System.Linq;

namespace RainPointClient;

public enum RainPointCalendarRainDelay
	{
	Unknown, NotDelayed, Delayed
	}

/// <summary>A local-calendar projection of a saved plan, not confirmation that a valve will open.</summary>
public sealed class RainPointScheduleOccurrence
	{
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
	public int Address
		{
		get;
		}
	public int Zone
		{
		get;
		}
	public RainPointSchedule Plan
		{
		get;
		}
	/// <summary>Home-local wall time with Unspecified kind. No UTC/DST conversion is inferred.</summary>
	public DateTime StartsAt
		{
		get;
		}
	public int? SeasonalPercentage
		{
		get;
		}
	/// <summary>Vendor-calendar display duration: scaled and rounded to whole minutes, minimum one. Null when seasonal settings are unavailable. Not an elapsed runtime or a volume-limit estimate.</summary>
	public TimeSpan? CalendarDuration
		{
		get;
		}
	public RainPointCalendarRainDelay RainDelay
		{
		get;
		}
	}

/// <summary>A bounded, immutable calendar projection. Empty means no starts only when Availability is Decoded.</summary>
public sealed class RainPointCalendarPreview
	{
	internal RainPointCalendarPreview (DateTime from, DateTime through, TimerReadingAvailability availability, IEnumerable<RainPointScheduleOccurrence> occurrences)
		{
		FromDate = from;
		ThroughDate = through;
		Availability = availability;
		Occurrences = Array.AsReadOnly (occurrences.ToArray ());
		}
	public DateTime FromDate
		{
		get;
		}
	public DateTime ThroughDate
		{
		get;
		}
	public TimerReadingAvailability Availability
		{
		get;
		}
	public IReadOnlyList<RainPointScheduleOccurrence> Occurrences
		{
		get;
		}
	/// <summary>First projected start at/after the supplied home-local wall time within this preview. Known rain-delayed starts are excluded. Inspect RainDelay: unknown settings do not imply no delay. Null is limited to this window, or unavailable projection.</summary>
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
	public static RainPointCalendarPreview Create (RainPointScheduleSnapshot snapshot, DateTime fromDate, DateTime throughDate) => CreateCore (snapshot, fromDate, throughDate, null);

	/// <summary>Projects the vendor app calendar using reported home timezone rules, including its UTC-day interval calculation around daylight changes. This is not a firmware execution promise.</summary>
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
	internal static void RequireLocal (DateTime value, string name)
		{
		if (value.Kind != DateTimeKind.Unspecified)
			throw new ArgumentException ("Supply the home's local wall time with Unspecified kind; no timezone is inferred.", name);
		}
	}
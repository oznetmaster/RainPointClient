// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;
namespace RainPointClient.Tests;

[TestFixture]
public sealed class ScheduleCalendarTests
	{
	private static DateTime Day (int year, int month, int day) => new (year, month, day);
	private static RainPointSchedule Plan (RainPointScheduleRepeat repeat = RainPointScheduleRepeat.EveryDay, int index = 0, int hour = 8, DateTime? effective = null, int? interval = null, bool enabled = true, RainPointScheduleMode mode = RainPointScheduleMode.Irrigation, TimeSpan? duration = null, DayOfWeek[]? weekdays = null)
	 => new (index, enabled, mode, TimeSpan.FromHours (hour), duration ?? TimeSpan.FromMinutes (10), repeat, Array.AsReadOnly (weekdays ?? []), interval, null, effective, null, null);
	private static RainPointScheduleSnapshot Snapshot (int zone = 1, params RainPointSchedule[] plans) => new (2, zone, TimerReadingAvailability.Decoded, Array.AsReadOnly (plans))
		{
		SeasonalAdjustmentAvailability = TimerReadingAvailability.Decoded,
		SeasonalPercentages = Array.AsReadOnly (Enumerable.Repeat (100, 12).ToArray ()),
		RainDelayAvailability = TimerReadingAvailability.Decoded
		};
	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public void DailyUsesSelectedZoneAndInclusiveLocalDates (int zone)
		{
		var snapshot = Snapshot (zone, Plan ());
		var start = Day (2026, 9, 24);
		var result = ScheduleCalendar.Create (snapshot, start, start.AddDays (2));
		Assert.That (result.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
		Assert.That (result.Occurrences.Select (o => o.StartsAt), Is.EqualTo (new[] { start.AddHours (8), start.AddDays (1).AddHours (8), start.AddDays (2).AddHours (8) }));
		Assert.That (result.Occurrences.All (o => o.Zone == zone && o.Address == 2 && o.StartsAt.Kind == DateTimeKind.Unspecified), Is.True);
		Assert.That (result.GetNextOccurrence (start.AddHours (8))!.StartsAt, Is.EqualTo (start.AddHours (8)));
		Assert.That (result.GetNextOccurrence (start.AddHours (8).AddSeconds (1))!.StartsAt, Is.EqualTo (start.AddDays (1).AddHours (8)));
		}
	[TestCase (RainPointScheduleRepeat.Once, new[] { 28 })]
	[TestCase (RainPointScheduleRepeat.EveryDay, new[] { 27, 28, 29 })]
	[TestCase (RainPointScheduleRepeat.OddDays, new[] { 27, 29 })]
	[TestCase (RainPointScheduleRepeat.EvenDays, new[] { 28 })]
	[TestCase (RainPointScheduleRepeat.Weekdays, new[] { 27, 29 })]
	[TestCase (RainPointScheduleRepeat.IntervalDays, new[] { 28 })]
	public void SixRecurrenceFormsAcrossLeapDay (RainPointScheduleRepeat repeat, int[] expected)
		{
		var plan = Plan (repeat, effective: repeat is RainPointScheduleRepeat.Once or RainPointScheduleRepeat.IntervalDays ? Day (2024, 2, 28) : null, interval: 2, weekdays: [DayOfWeek.Tuesday, DayOfWeek.Thursday]);
		var result = ScheduleCalendar.Create (Snapshot (1, plan), Day (2024, 2, 27), Day (2024, 2, 29));
		Assert.That (result.Occurrences.Select (o => o.StartsAt.Day), Is.EqualTo (expected));
		}
	[Test]
	public void IntervalIsAnchoredToCivilDateAcrossMonthYearAndDstBoundaries ()
		{
		foreach (DateTime anchor in new[] { Day (2026, 3, 28), Day (2026, 10, 24), Day (2026, 12, 31) })
			{
			var result = ScheduleCalendar.Create (Snapshot (1, Plan (RainPointScheduleRepeat.IntervalDays, effective: anchor, interval: 2)), anchor.AddDays (-1), anchor.AddDays (5));
			Assert.That (result.Occurrences.Select (o => o.StartsAt), Is.EqualTo (new[] { anchor.AddHours (8), anchor.AddDays (2).AddHours (8), anchor.AddDays (4).AddHours (8) }));
			}
		}
	[Test]
	public void OddDaysIncludeThirtyFirstAndFirstWithoutFabricatedAlternation ()
		{
		var result = ScheduleCalendar.Create (Snapshot (1, Plan (RainPointScheduleRepeat.OddDays)), Day (2026, 1, 31), Day (2026, 2, 2));
		Assert.That (result.Occurrences.Select (o => o.StartsAt.Date), Is.EqualTo (new[] { Day (2026, 1, 31), Day (2026, 2, 1) }));
		}
	[Test]
	public void EffectiveDateGatesRecurringPlansAndDisabledPlansNeverAppear ()
		{
		var result = ScheduleCalendar.Create (Snapshot (1, Plan (effective: Day (2026, 9, 25)), Plan (index: 1, enabled: false)), Day (2026, 9, 24), Day (2026, 9, 26));
		Assert.That (result.Occurrences.Select (o => o.StartsAt.Date), Is.EqualTo (new[] { Day (2026, 9, 25), Day (2026, 9, 26) }));
		}
	[Test]
	public void PastOnceAndEmptyWeekdaysTerminateWithNoNextPlan ()
		{
		var result = ScheduleCalendar.Create (Snapshot (1, Plan (RainPointScheduleRepeat.Once, effective: Day (2020, 1, 1)), Plan (RainPointScheduleRepeat.Weekdays, index: 1)), Day (2026, 9, 24), Day (2027, 9, 24));
		Assert.That (result.Occurrences, Is.Empty);
		Assert.That (result.GetNextOccurrence (Day (2026, 9, 24)), Is.Null);
		}
	[Test]
	public void RainDelayIncludesExactExpiryAndNextSkipsKnownDelayedStarts ()
		{
		var start = Day (2026, 9, 24);
		var snapshot = Snapshot (1, Plan ());
		snapshot.RainDelayUntil = start.AddDays (1).AddHours (8);
		var result = ScheduleCalendar.Create (snapshot, start, start.AddDays (2));
		Assert.That (result.Occurrences.Select (o => o.RainDelay), Is.EqualTo (new[] { RainPointCalendarRainDelay.Delayed, RainPointCalendarRainDelay.Delayed, RainPointCalendarRainDelay.NotDelayed }));
		Assert.That (result.GetNextOccurrence (start)!.StartsAt, Is.EqualTo (start.AddDays (2).AddHours (8)));
		}
	[TestCase (TimerReadingAvailability.NotReported)]
	[TestCase (TimerReadingAvailability.Malformed)]
	[TestCase (TimerReadingAvailability.UnsupportedFormat)]
	public void MissingSettingsStayUnknownWhileScheduleStartsRemainVisible (TimerReadingAvailability state)
		{
		var day = Day (2026, 9, 24);
		var snapshot = Snapshot (1, Plan ());
		snapshot.RainDelayAvailability = snapshot.SeasonalAdjustmentAvailability = state;
		var result = ScheduleCalendar.Create (snapshot, day, day);
		var next = result.GetNextOccurrence (day)!;
		Assert.That (next.RainDelay, Is.EqualTo (RainPointCalendarRainDelay.Unknown));
		Assert.That (next.SeasonalPercentage, Is.Null);
		Assert.That (next.CalendarDuration, Is.Null);
		}
	[TestCase (RainPointScheduleMode.Irrigation)]
	[TestCase (RainPointScheduleMode.Misting)]
	[TestCase (RainPointScheduleMode.CycleAndSoak)]
	public void CalendarDurationFollowsDisplayRoundingAndEachMonthsPercentage (RainPointScheduleMode mode)
		{
		var snapshot = Snapshot (1, Plan (mode: mode, duration: TimeSpan.FromSeconds (90)));
		var months = Enumerable.Repeat (100, 12).ToArray ();
		months[8] = 10;
		months[9] = 100;
		snapshot.SeasonalPercentages = Array.AsReadOnly (months);
		var result = ScheduleCalendar.Create (snapshot, Day (2026, 9, 30), Day (2026, 10, 1));
		Assert.That (result.Occurrences.Select (o => o.CalendarDuration), Is.EqualTo (new[] { TimeSpan.FromMinutes (1), TimeSpan.FromMinutes (2) }));
		Assert.That (result.Occurrences.Select (o => o.SeasonalPercentage), Is.EqualTo (new[] { 10, 100 }));
		}
	[TestCase (RainPointScheduleRepeat.Once)]
	[TestCase (RainPointScheduleRepeat.IntervalDays)]
	public void MissingEffectiveAnchorDoesNotProducePartialResults (RainPointScheduleRepeat repeat)
		{
		var day = Day (2026, 9, 24);
		var result = ScheduleCalendar.Create (Snapshot (1, Plan (), Plan (repeat, index: 1, interval: 2)), day, day);
		Assert.That (result.Availability, Is.EqualTo (TimerReadingAvailability.NotReported));
		Assert.That (result.Occurrences, Is.Empty);
		Assert.That (result.GetNextOccurrence (day), Is.Null);
		}
	[Test]
	public void EnabledHourlyPlanInvalidatesProjectionButDisabledHourlyDoesNot ()
		{
		var day = Day (2026, 9, 24);
		Assert.That (ScheduleCalendar.Create (Snapshot (1, Enumerable.Range (0, 7).Select (index => Plan (index: index)).ToArray ()), day, day).Availability, Is.EqualTo (TimerReadingAvailability.UnsupportedFormat));
		var unknown = ScheduleCalendar.Create (Snapshot (1, Plan (), Plan (RainPointScheduleRepeat.IntervalHours, index: 1, interval: 2)), day, day);
		Assert.That (unknown.Availability, Is.EqualTo (TimerReadingAvailability.UnsupportedFormat));
		Assert.That (unknown.Occurrences, Is.Empty);
		Assert.That (ScheduleCalendar.Create (Snapshot (1, Plan (RainPointScheduleRepeat.IntervalHours, enabled: false)), day, day).Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
		}
	[TestCase (TimerReadingAvailability.NotReported)]
	[TestCase (TimerReadingAvailability.Malformed)]
	[TestCase (TimerReadingAvailability.UnsupportedFormat)]
	public void UnreadableScheduleSnapshotDoesNotBecomeEmptySuccess (TimerReadingAvailability state)
		{
		var day = Day (2026, 9, 24);
		var result = ScheduleCalendar.Create (new (2, 1, state, Array.Empty<RainPointSchedule> ()), day, day);
		Assert.That (result.Availability, Is.EqualTo (state));
		Assert.That (result.Occurrences, Is.Empty);
		}
	[Test]
	public void OrderingUsesStartThenIndexAndCollectionCannotBeMutated ()
		{
		var day = Day (2026, 9, 24);
		var result = ScheduleCalendar.Create (Snapshot (1, Plan (index: 2), Plan (index: 0, hour: 6), Plan (index: 1)), day, day);
		Assert.That (result.Occurrences.Select (o => o.Plan.Index), Is.EqualTo (new[] { 0, 1, 2 }));
		Assert.Throws<NotSupportedException> (() => ((IList<RainPointScheduleOccurrence>)result.Occurrences).Clear ());
		}
	[Test]
	public void DateRangeBoundsAvoidUnboundedSearchAndMaxDateOverflow ()
		{
		var day = Day (2026, 9, 24);
		var snapshot = Snapshot (1, Plan ());
		Assert.Throws<ArgumentOutOfRangeException> (() => ScheduleCalendar.Create (snapshot, day, day.AddDays (366)));
		Assert.Throws<ArgumentOutOfRangeException> (() => ScheduleCalendar.Create (snapshot, day, day.AddDays (-1)));
		Assert.Throws<ArgumentException> (() => ScheduleCalendar.Create (snapshot, day.AddSeconds (1), day));
		Assert.Throws<ArgumentNullException> (() => ScheduleCalendar.Create (null!, day, day));
		var last = ScheduleCalendar.Create (snapshot, DateTime.MaxValue.Date, DateTime.MaxValue.Date);
		Assert.That (last.Occurrences.Single ().StartsAt, Is.EqualTo (DateTime.MaxValue.Date.AddHours (8)));
		}
	[TestCase (DateTimeKind.Local)]
	[TestCase (DateTimeKind.Utc)]
	public void RejectsImplicitTimezoneConversions (DateTimeKind kind)
		{
		var day = Day (2026, 9, 24);
		var snapshot = Snapshot (1, Plan ());
		Assert.Throws<ArgumentException> (() => ScheduleCalendar.Create (snapshot, DateTime.SpecifyKind (day, kind), day));
		var result = ScheduleCalendar.Create (snapshot, day, day);
		Assert.Throws<ArgumentException> (() => result.GetNextOccurrence (DateTime.SpecifyKind (day, kind)));
		Assert.Throws<ArgumentOutOfRangeException> (() => result.GetNextOccurrence (day.AddDays (1)));
		}
	}
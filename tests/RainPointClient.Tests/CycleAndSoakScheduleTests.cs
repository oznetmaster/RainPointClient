using System;

using NUnit.Framework;

using RainPointClient.Protocol;
namespace RainPointClient.Tests;

[TestFixture]
public sealed class CycleAndSoakScheduleTests
	{
	[Test]
	public void OmittedDateRetainsCycleOffsetsAndDoesNotIntroduceWaterLimit ()
		{
		Assert.That (ScheduleEditor.EncodeCycleAndSoak (new ()), Is.EqualTo ("0000ca0a000000000005001e00"));
		}

	[TestCase (4, 1, 1)]
	[TestCase (4.5, 1, 1)]
	[TestCase (1441, 1, 1)]
	[TestCase (10, 0, 1)]
	[TestCase (10, 1.5, 1)]
	[TestCase (10, 11, 1)]
	[TestCase (1440, 721, 1)]
	[TestCase (10, 1, 0)]
	[TestCase (10, 1, 1.5)]
	[TestCase (10, 1, 721)]
	public void InvalidTimingIsRejected (double duration, double watering, double pause)
		{
		Assert.Throws<ArgumentException> (() => ScheduleEditor.EncodeCycleAndSoak (new ()
			{
			Duration = TimeSpan.FromMinutes (duration),
			CycleWateringTime = TimeSpan.FromMinutes (watering),
			CyclePauseTime = TimeSpan.FromMinutes (pause)
			}));
		}

	[TestCase (5, 5, 720)] // No final pause: elapsed remains five minutes.
	[TestCase (7, 5, 720)] // Partial last cycle: one pause, not two.
	[TestCase (1440, 720, 720)]
	public void ValidTimingBoundariesEncodeInMinutes (int duration, int watering, int pause)
		{
		RainPointCycleAndSoakSchedule plan = new ()
			{
			Duration = TimeSpan.FromMinutes (duration),
			CycleWateringTime = TimeSpan.FromMinutes (watering),
			CyclePauseTime = TimeSpan.FromMinutes (pause),
			Repeat = RainPointScheduleRepeat.IntervalDays,
			Interval = 2,
			EffectiveDate = new DateTime (2026, 9, 24)
			};
		string encoded = ScheduleEditor.EncodeCycleAndSoak (plan);
		RainPointSchedule decoded = ScheduleDecoder.Decode (new RainPointDevice { PortNumber = 3, Parameter = "settings," + encoded + "/|z2,|z3," }, 1).Schedules[0];
		Assert.That (decoded.Duration, Is.EqualTo (plan.Duration));
		Assert.That (decoded.CycleWateringTime, Is.EqualTo (plan.CycleWateringTime));
		Assert.That (decoded.CyclePauseTime, Is.EqualTo (plan.CyclePauseTime));
		}

	[TestCase (RainPointScheduleRepeat.EveryDay, false)]
	[TestCase (RainPointScheduleRepeat.OddDays, false)]
	[TestCase (RainPointScheduleRepeat.EvenDays, true)]
	[TestCase (RainPointScheduleRepeat.IntervalDays, true)]
	public void ElapsedDurationIncludesOnlyPausesBetweenCycles (RainPointScheduleRepeat repeat, bool allowed)
		{
		// 24 watering hours plus one one-minute pause exceeds a daily repeat.
		RainPointCycleAndSoakSchedule plan = new ()
			{
			Duration = TimeSpan.FromHours (24),
			CycleWateringTime = TimeSpan.FromHours (12),
			CyclePauseTime = TimeSpan.FromMinutes (1),
			Repeat = repeat,
			Interval = repeat == RainPointScheduleRepeat.IntervalDays ? 2 : null,
			EffectiveDate = new DateTime (2026, 9, 24)
			};
		if (allowed)
			Assert.DoesNotThrow (() => ScheduleEditor.EncodeCycleAndSoak (plan));
		else
			Assert.Throws<ArgumentException> (() => ScheduleEditor.EncodeCycleAndSoak (plan));
		}

	[TestCase (DayOfWeek.Saturday, DayOfWeek.Sunday, false)]
	[TestCase (DayOfWeek.Friday, DayOfWeek.Sunday, true)]
	[TestCase (DayOfWeek.Monday, DayOfWeek.Tuesday, false)]
	public void WeekdayGapIncludesTheWeekBoundary (DayOfWeek first, DayOfWeek second, bool allowed)
		{
		RainPointCycleAndSoakSchedule plan = new ()
			{
			Duration = TimeSpan.FromHours (24),
			CycleWateringTime = TimeSpan.FromHours (12),
			CyclePauseTime = TimeSpan.FromMinutes (1),
			Repeat = RainPointScheduleRepeat.Weekdays,
			Weekdays = new[] { first, second }
			};
		if (allowed)
			Assert.DoesNotThrow (() => ScheduleEditor.EncodeCycleAndSoak (plan));
		else
			Assert.Throws<ArgumentException> (() => ScheduleEditor.EncodeCycleAndSoak (plan));
		}

	[Test]
	public void CyclePlanUsesTheSameCalendarValidationAsNormalPlans ()
		{
		using (Assert.EnterMultipleScope ())
			{
			Assert.Throws<NotSupportedException> (() => ScheduleEditor.EncodeCycleAndSoak (new () { Repeat = RainPointScheduleRepeat.Once }));
			Assert.Throws<ArgumentException> (() => ScheduleEditor.EncodeCycleAndSoak (new () { Repeat = RainPointScheduleRepeat.Weekdays }));
			Assert.Throws<ArgumentException> (() => ScheduleEditor.EncodeCycleAndSoak (new () { StartTime = TimeSpan.FromSeconds (1) }));
			Assert.Throws<ArgumentException> (() => ScheduleEditor.EncodeCycleAndSoak (new () { EffectiveDate = new DateTime (2084, 1, 1) }));
			Assert.Throws<NotSupportedException> (() => ScheduleEditor.EncodeCycleAndSoak (new () { Repeat = RainPointScheduleRepeat.IntervalHours }));
			}
		}
	}
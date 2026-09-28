// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;

using NUnit.Framework;

using RainPointClient.Protocol;
namespace RainPointClient.Tests;

[TestFixture]
public sealed class MistingScheduleTests
	{
	[Test]
	public void AbsentDateKeepsBurstAndPauseAtTheirDefinedOffsets ()
		{
		Assert.That (ScheduleEditor.EncodeMisting (new ()), Is.EqualTo ("00008a5802000000000a001400"));
		}

	[Test]
	public void EnabledWeekdayPlanUsesSecondsAndLocalDate ()
		{
		Assert.That (ScheduleEditor.EncodeMisting (new ()
			{
			Enabled = true,
			StartTime = new TimeSpan (8, 30, 0),
			Repeat = RainPointScheduleRepeat.Weekdays,
			Weekdays = new[] { DayOfWeek.Sunday, DayOfWeek.Wednesday },
			EffectiveDate = new DateTime (2026, 9, 24),
			CycleWateringTime = TimeSpan.FromSeconds (7),
			CyclePauseTime = TimeSpan.FromSeconds (13)
			}), Is.EqualTo ("891ea258020000380d07000d00"));
		}

	[TestCase (0)]
	[TestCase (59)]
	[TestCase (61)]
	[TestCase (43260)]
	public void DurationMustBeOneTo720WholeMinutes (int seconds)
		{
		Assert.Throws<ArgumentException> (() => ScheduleEditor.EncodeMisting (new () { Duration = TimeSpan.FromSeconds (seconds) }));
		}

	[TestCase (0, true)]
	[TestCase (4, true)]
	[TestCase (5.5, true)]
	[TestCase (3601, true)]
	[TestCase (0, false)]
	[TestCase (4, false)]
	[TestCase (5.5, false)]
	[TestCase (3601, false)]
	public void BurstAndPauseRequireFiveTo3600WholeSeconds (double seconds, bool burst)
		{
		RainPointMistingSchedule plan = new ();
		if (burst)
			plan.CycleWateringTime = TimeSpan.FromSeconds (seconds);
		else
			plan.CyclePauseTime = TimeSpan.FromSeconds (seconds);
		Assert.Throws<ArgumentException> (() => ScheduleEditor.EncodeMisting (plan));
		}

	[TestCase (1, 5, 3600)]
	[TestCase (720, 3600, 5)]
	[TestCase (1, 3600, 3600)] // Vendor picker does not clamp bursts to configured duration.
	public void BoundaryValuesRemainIndependentAndDecodeInSeconds (int minutes, int burst, int pause)
		{
		RainPointMistingSchedule plan = new ()
			{
			Duration = TimeSpan.FromMinutes (minutes),
			CycleWateringTime = TimeSpan.FromSeconds (burst),
			CyclePauseTime = TimeSpan.FromSeconds (pause)
			};
		string encoded = ScheduleEditor.EncodeMisting (plan);
		RainPointSchedule decoded = ScheduleDecoder.Decode (new RainPointDevice { PortNumber = 3, Parameter = "settings," + encoded + "/|z2,|z3," }, 1).Schedules[0];
		Assert.That (decoded.Duration, Is.EqualTo (plan.Duration));
		Assert.That (decoded.CycleWateringTime, Is.EqualTo (plan.CycleWateringTime));
		Assert.That (decoded.CyclePauseTime, Is.EqualTo (plan.CyclePauseTime));
		Assert.That (decoded.WaterLimitLitres, Is.Null);
		Assert.That (decoded.EffectiveDate, Is.Null);
		}

	[Test]
	public void CalendarValidationIsSharedWithOtherModes ()
		{
		using (Assert.EnterMultipleScope ())
			{
			Assert.Throws<ArgumentNullException> (() => ScheduleEditor.EncodeMisting (null!));
			Assert.Throws<NotSupportedException> (() => ScheduleEditor.EncodeMisting (new () { Repeat = RainPointScheduleRepeat.Once }));
			Assert.Throws<ArgumentException> (() => ScheduleEditor.EncodeMisting (new () { Repeat = RainPointScheduleRepeat.IntervalDays, Interval = 128 }));
			Assert.Throws<ArgumentException> (() => ScheduleEditor.EncodeMisting (new () { Repeat = RainPointScheduleRepeat.Weekdays, Weekdays = new[] { DayOfWeek.Sunday, DayOfWeek.Sunday } }));
			Assert.Throws<ArgumentException> (() => ScheduleEditor.EncodeMisting (new () { StartTime = TimeSpan.FromDays (1) }));
			Assert.Throws<ArgumentException> (() => ScheduleEditor.EncodeMisting (new () { EffectiveDate = new DateTime (2084, 1, 1) }));
			Assert.Throws<NotSupportedException> (() => ScheduleEditor.EncodeMisting (new () { Repeat = RainPointScheduleRepeat.IntervalHours }));
			}
		}
	}
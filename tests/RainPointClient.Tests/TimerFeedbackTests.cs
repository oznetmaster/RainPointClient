// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;

using NUnit.Framework;

using RainPointClient.Protocol;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class TimerFeedbackTests
	{
	[TestCase ("01", false)]
	[TestCase ("02", true)]
	[TestCase ("00", null)]
	[TestCase ("03", null)]
	[TestCase ("FF", null)]
	public void BatteryConditionMappingDoesNotInventPercentages (string value, bool? expected)
		{
		RainPointTimerStatus status = TimerDecoder.Decode (1, 3, "11#18DC" + value, null);
		Assert.That (status.IsBatteryLow, Is.EqualTo (expected));
		}

	[Test]
	public void AbsentBatteryAndAlarmRemainUnknown ()
		{
		RainPointTimerStatus status = TimerDecoder.Decode (1, 3, "11#19D800", null);
		Assert.That (status.IsBatteryLow, Is.Null);
		Assert.That (status.Zones[0].AlarmCode, Is.Null);
		Assert.That (status.ReportedAtLocal, Is.Null);
		}

	[Test]
	public void CompactAlarmReturnsOnlyTheValueNibble ()
		{
		RainPointTimerStatus status = TimerDecoder.Decode (1, 3, "11#1D201E231F2F", null);
		Assert.That (status.Zones[0].AlarmCode, Is.EqualTo (0));
		Assert.That (status.Zones[1].AlarmCode, Is.EqualTo (3));
		Assert.That (status.Zones[2].AlarmCode, Is.EqualTo (15));
		}

	[Test]
	public void PackedTimesPreserveLocalWallClockWithoutAssumingTimezone ()
		{
		RainPointTimerStatus status = TimerDecoder.Decode (1, 3, "11#21B701166F1AFEFF0F01166F1A", null);
		DateTime expected = new (2026, 9, 23, 17, 24, 1, DateTimeKind.Unspecified);
		Assert.That (status.ReportedAtLocal, Is.EqualTo (expected));
		Assert.That (status.ReportedAtLocal!.Value.Kind, Is.EqualTo (DateTimeKind.Unspecified));
		Assert.That (status.Zones[0].EventTimeLocal, Is.EqualTo (expected));
		Assert.That (status.Zones[1].EventTimeLocal, Is.Null);
		}

	[TestCase ("00000000")]
	[TestCase ("FFFFFFFF")]
	[TestCase ("01000000")]
	public void InvalidOrZeroPackedTimesAreUnknown (string value)
		{
		RainPointTimerStatus status = TimerDecoder.Decode (1, 3, "11#21B7" + value + "FEFF0F" + value, null);
		Assert.That (status.Zones[0].EventTimeLocal, Is.Null);
		Assert.That (status.ReportedAtLocal, Is.Null);
		}
	}
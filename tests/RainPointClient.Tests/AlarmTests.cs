using System;
using System.Collections.Generic;

using NUnit.Framework;

using RainPointClient.Protocol;
using RainPointClient.Desktop.Core;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class AlarmTests
	{
	private static IEnumerable<TestCaseData> Cases ()
		{
		for (int zone = 1; zone <= 3; zone++)
			for (int bits = 0; bits < 16; bits++)
				yield return new TestCaseData (zone, bits);
		}
	[TestCaseSource (nameof (Cases))]
	public void AllAlarmCombinationsKeepIndependentFlagsAndUnknownBits (int zone, int bits)
		{
		var status = TimerDecoder.Decode (2, 3, "11#" + (0x1c + zone).ToString ("x2") + (0x20 + bits).ToString ("x2"), null);
		var reading = status.Zones[zone - 1];
		Assert.Multiple (() =>
			{
				Assert.That (reading.AlarmCode, Is.EqualTo (bits));
				Assert.That (reading.WaterLeakReported, Is.EqualTo ((bits & 1) != 0));
				Assert.That (reading.WaterShortageReported, Is.EqualTo ((bits & 2) != 0));
				Assert.That (reading.FreezeReported, Is.EqualTo ((bits & 4) != 0));
				Assert.That (reading.UnknownAlarmBits, Is.EqualTo (bits & 8));
			});
		string alarm = new ZoneRow (reading).Alarm;
		Assert.That (alarm.Contains ("Leak reported"), Is.EqualTo ((bits & 1) != 0));
		Assert.That (alarm.Contains ("Water shortage reported"), Is.EqualTo ((bits & 2) != 0));
		Assert.That (alarm.Contains ("Freeze reported"), Is.EqualTo ((bits & 4) != 0));
		Assert.That (alarm.Contains ("Unknown bits: 08"), Is.EqualTo ((bits & 8) != 0));
		if (bits == 0)
			Assert.That (alarm, Is.EqualTo ("None reported"));
		}
	[Test]
	public void MissingAlarmNeverMeansNoFault ()
		{
		var reading = new RainPointZoneStatus (1, null, null, null);
		Assert.Multiple (() =>
			{
				Assert.That (reading.WaterLeakReported, Is.Null);
				Assert.That (reading.WaterShortageReported, Is.Null);
				Assert.That (reading.FreezeReported, Is.Null);
				Assert.That (reading.UnknownAlarmBits, Is.Null);
				Assert.That (new ZoneRow (reading).Alarm, Is.EqualTo ("Unknown"));
			});
		}
	}
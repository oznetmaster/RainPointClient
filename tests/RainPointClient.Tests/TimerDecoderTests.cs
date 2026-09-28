// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;

using NUnit.Framework;

using RainPointClient.Protocol;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class TimerDecoderTests
	{
	[TestCase (1, 0x00, false)]
	[TestCase (2, 0x20, false)]
	[TestCase (3, 0x10, false)]
	[TestCase (1, 0x01, true)]
	[TestCase (2, 0x21, true)]
	[TestCase (3, 0x11, true)]
	[TestCase (1, 0x02, true)]
	[TestCase (2, 0x22, true)]
	[TestCase (3, 0x12, true)]
	[TestCase (1, 0x03, true)]
	[TestCase (2, 0x23, true)]
	[TestCase (3, 0x13, true)]
	[TestCase (1, 0x07, true)]
	[TestCase (2, 0x27, true)]
	[TestCase (3, 0x17, true)]
	[TestCase (1, 0x04, null)]
	[TestCase (2, 0x25, null)]
	[TestCase (3, 0xFF, null)]
	public void WorkModeUsesTheWholeLowNibbleAndUnknownCodesStayUnknown (int zone, int packed, bool? active)
		{
		var result = TimerDecoder.Decode (1, 3, "11#" + (0x18 + zone).ToString ("X2") + "D8" + packed.ToString ("X2"), null);
		var reading = result.Zones[zone - 1];
		Assert.That (reading.WorkModeCode, Is.EqualTo (packed & 15));
		Assert.That (reading.WorkMode, Is.EqualTo ((packed & 15) is (>= 0 and <= 3) or 7 ? (RainPointWateringMode?)(packed & 15) : null));
		Assert.That (reading.IsOpen, Is.EqualTo (active));
		Assert.That (result.Zones[zone == 1 ? 1 : 0].WorkMode, Is.Null);
		}

	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public void CyclePauseRemainsActiveUntilIdleAndHasExplicitDisplay (int zone)
		{
		// Minimal synthetic frames reproduce the observed 3 -> 7 -> 3 -> 0 sequence.
		byte[] modes = { 3, 7, 3, 0 };
		string[] labels = { "Reported cycling", "Reported soaking (paused)", "Reported cycling", "Reported closed" };
		for (int i = 0; i < modes.Length; i++)
			{
			var decoded = TimerDecoder.Decode (1, 3, "11#" + (0x18 + zone).ToString ("X2") + "D8" + modes[i].ToString ("X2"), null);
			RainPointZoneStatus status = decoded.Zones[zone - 1];
			Assert.That (status.IsOpen, Is.EqualTo (i != 3), "The soaking pause must not appear as completed irrigation.");
			Assert.That (new RainPointClient.Desktop.Core.ZoneRow (status).State, Is.EqualTo (labels[i]));
			}
		}

	[TestCase ("11#")]
	[TestCase ("01#")]
	public void ThreeZonesDecodeWithLittleEndianDurationsAndUsage (string prefix)
		{
		RainPointTimerStatus status = TimerDecoder.Decode (1, 3,
			 prefix + "17E1D60018DC0219D8011AD8201BD82125AD580226AF100E000027AD3C00299FA5010000", null);
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (status.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
			Assert.That (status.SignalStrengthDbm, Is.EqualTo (-42));
			Assert.That (status.BatteryConditionCode, Is.EqualTo (2));
			Assert.That (status.Zones[0].IsOpen, Is.True);
			Assert.That (status.Zones[1].IsOpen, Is.False);
			Assert.That (status.Zones[2].IsOpen, Is.True);
			Assert.That (status.Zones[0].ConfiguredRunDuration, Is.EqualTo (TimeSpan.FromSeconds (600)));
			Assert.That (status.Zones[1].ConfiguredRunDuration, Is.EqualTo (TimeSpan.FromSeconds (3600)));
			Assert.That (status.Zones[2].ConfiguredRunDuration, Is.EqualTo (TimeSpan.FromSeconds (60)));
			Assert.That (status.Zones[0].LastWaterUsageCounts, Is.EqualTo (421));
			Assert.That (status.Zones[1].LastWaterUsageCounts, Is.Null);
			}
		}

	[Test]
	public void HeaderLikeBytesInsideValuesDoNotBecomeStates ()
		{
		RainPointTimerStatus status = TimerDecoder.Decode (1, 3, "11#29FF0F19D801001AD820", null);
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (status.Zones[0].IsOpen, Is.Null);
			Assert.That (status.Zones[1].IsOpen, Is.False);
			}
		}

	[Test]
	public void WrongFieldWithCorrectDpIdDoesNotBecomeState ()
		{
		RainPointTimerStatus status = TimerDecoder.Decode (1, 3, "11#19DC011AD800", null);
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (status.Zones[0].IsOpen, Is.Null);
			Assert.That (status.Zones[1].IsOpen, Is.False);
			}
		}

	[Test]
	public void ExtendedFieldCannotAliasAnotherDatapoint ()
		{
		// Extended field 286 exceeds one byte; it must not alias dp 0x19 / field 30.
		RainPointTimerStatus status = TimerDecoder.Decode (1, 3, "11#18FCF7011AD800", null);
		Assert.That (status.Zones[0].IsOpen, Is.Null);
		Assert.That (status.Zones[1].IsOpen, Is.False);
		}
	[Test]
	public void CapturedHtv345FrameHasThreeClosedZones ()
		{
		// Protocol-only capture from the owner's HTV345FRF; no account/device identifiers.
		const string FRAME = "11#2A9F00000000299F0000000017E1C10019D8001AD8001BD8001D201E201F2018DC0121B70000000022B70000000023B70000000025AD000026AD000027AD00002B9F00000000FEFF0F53136F1A";
		RainPointTimerStatus status = TimerDecoder.Decode (1, 3, FRAME, null);
		Assert.That (status.Zones, Has.Count.EqualTo (3));
		foreach (RainPointZoneStatus zone in status.Zones)
			{
			Assert.That (zone.IsOpen, Is.False);
			Assert.That (zone.ConfiguredRunDuration, Is.EqualTo (TimeSpan.Zero));
			}
		Assert.That (status.SignalStrengthDbm, Is.EqualTo (-63));
		Assert.That (status.BatteryConditionCode, Is.EqualTo (1));
		}

	[Test]
	public void Htv345UsageMatchesOwnerAppReading ()
		{
		// Minimal fixture encoding the observed count; not a full captured status frame.
		RainPointTimerStatus status = TimerDecoder.Decode (1, 3, "11#299F0E0000002A9F00000000", null);
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (status.Zones[0].LastWaterUsageCounts, Is.EqualTo (14));
			Assert.That (status.Zones[0].LastWaterUsageLitres, Is.EqualTo (1.4m));
			Assert.That (status.Zones[1].LastWaterUsageLitres, Is.EqualTo (0m));
			Assert.That (status.Zones[2].LastWaterUsageLitres, Is.Null);
			}
		}

	[Test]
	public void FullUnsignedUsageRangeConvertsWithoutOverflow ()
		{
		RainPointTimerStatus status = TimerDecoder.Decode (1, 3, "11#299FFFFFFFFF", null);
		Assert.That (status.Zones[0].LastWaterUsageLitres, Is.EqualTo (429496729.5m));
		}

	[Test]
	public void IncorrectUsageWidthRemainsUnknown ()
		{
		RainPointTimerStatus status = TimerDecoder.Decode (1, 3, "11#299D0E00", null);
		Assert.That (status.Zones[0].LastWaterUsageCounts, Is.Null);
		Assert.That (status.Zones[0].LastWaterUsageLitres, Is.Null);
		}

	[Test]
	public void RepeatedRecordsUseLastReading ()
		{
		RainPointTimerStatus status = TimerDecoder.Decode (1, 3, "11#19D80119D800", null);
		Assert.That (status.Zones[0].IsOpen, Is.False);
		}

	[TestCase ("11#")]
	[TestCase ("11#Z0")]
	[TestCase ("11#19D80")]
	[TestCase ("11#19")]
	[TestCase ("11#19D8")]
	[TestCase ("11#19D80125AD58")]
	[TestCase ("11#FEFF")]
	[TestCase ("11#FEFF0F01")]
	public void TruncatedOrMalformedFramesDoNotProducePartialStates (string value)
		{
		RainPointTimerStatus status = TimerDecoder.Decode (1, 3, value, null);
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (status.Availability, Is.EqualTo (TimerReadingAvailability.Malformed));
			Assert.That (status.Zones, Is.Empty);
			}
		}

	[TestCase ("10#D801")]
	[TestCase ("1,-84,1;0,149,0,0,0,0")]
	[TestCase ("12#19D801")]
	public void UnknownFormatsAreExplicit (string value) => Assert.That (
		 TimerDecoder.Decode (1, 3, value, null).Availability, Is.EqualTo (TimerReadingAvailability.UnsupportedFormat));

	[TestCase (null)]
	[TestCase ("")]
	public void MissingFramesAreExplicit (string? value) => Assert.That (
		 TimerDecoder.Decode (1, 3, value, null).Availability, Is.EqualTo (TimerReadingAvailability.NotReported));
	}
// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.Linq;

using NUnit.Framework;

using RainPointClient.Protocol;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class VolumeScheduleTests
	{
	private static string Encode (int mode, decimal? volume) => mode switch
		{
			1 => ScheduleEditor.Encode (new () { WaterLimitLitres = volume }),
			2 => ScheduleEditor.EncodeMisting (new () { WaterLimitLitres = volume }),
			_ => ScheduleEditor.EncodeCycleAndSoak (new () { WaterLimitLitres = volume })
			};
	private static RainPointScheduleSnapshot Decode (string record)
		{
		RainPointDevice device = new ()
			{
			PortNumber = 3,
			Parameter = "58020a001e0000800000000000d7," + record + "/,aux,646464646464646464646464,tail|z2,/,aux|z3,/,aux"
			};
		var snapshot = ScheduleDecoder.Decode (device, 1);
		snapshot.Parameter = device.Parameter;
		snapshot.PortNumber = 3;
		snapshot.FirmwareVersion = "130";
		TimerPlanSettings.Decode (snapshot);
		return snapshot;
		}

	[Test, Combinatorial]
	public void VolumeUsesTenthsAtTheSameOffsetWithoutChangingTiming (
	 [Values (1, 2, 3)] int mode, [Values ("0.3", "1.4", "6000")] string value)
		{
		decimal volume = decimal.Parse (value, CultureInfo.InvariantCulture);
		string encoded = Encode (mode, volume), plain = Encode (mode, null);
		string word = value == "0.3" ? "0300" : value == "1.4" ? "0e00" : "60ea";
		Assert.That (encoded, Is.EqualTo (plain.Substring (0, 10) + word + plain.Substring (14)));
		var decoded = Decode (encoded).Schedules.Single ();
		Assert.That (decoded.WaterLimitLitres, Is.EqualTo (volume));
		Assert.That (decoded.Enabled, Is.False);
		Assert.That (decoded.Mode, Is.EqualTo ((RainPointScheduleMode)mode));
		Assert.That (decoded.Duration, Is.EqualTo (Decode (plain).Schedules.Single ().Duration));
		}

	[Test, Combinatorial]
	public void InvalidVolumeIsRejectedForEveryMode (
	 [Values (1, 2, 3)] int mode, [Values ("-1", "0", "0.1", "0.2", "1.23", "6000.1", "6553.5")] string value)
		{
		Assert.Throws<ArgumentException> (() => Encode (mode, decimal.Parse (value, CultureInfo.InvariantCulture)));
		}

	[Test, Combinatorial]
	public void ReadsRetainValuesOutsideTheAppWriteRange (
	 [Values (1, 2, 3)] int mode, [Values ("0100", "ffff")] string word)
		{
		string plain = Encode (mode, null);
		var decoded = Decode (plain.Substring (0, 10) + word + plain.Substring (14)).Schedules.Single ();
		Assert.That (decoded.WaterLimitLitres, Is.EqualTo (word == "0100" ? 0.1m : 6553.5m));
		}

	[Test]
	public void VolumeDoesNotBypassCycleRecurrenceDurationGuard ()
		{
		Assert.Throws<ArgumentException> (() => ScheduleEditor.EncodeCycleAndSoak (new ()
			{
			Duration = TimeSpan.FromDays (1),
			CycleWateringTime = TimeSpan.FromHours (12),
			CyclePauseTime = TimeSpan.FromMinutes (1),
			WaterLimitLitres = 0.3m
			}));
		}

	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public void SeasonalValidationKeepsVolumePlansDurationCapUnscaled (int mode)
		{
		string record = mode == 1 ? ScheduleEditor.Encode (new ()
			{
			Duration = TimeSpan.FromHours (12),
			WaterLimitLitres = 1.4m
			})
		 : mode == 2 ? ScheduleEditor.EncodeMisting (new ()
			 {
			 Duration = TimeSpan.FromHours (12),
			 WaterLimitLitres = 1.4m
			 })
		 : ScheduleEditor.EncodeCycleAndSoak (new ()
			 {
			 Duration = TimeSpan.FromHours (12),
			 CycleWateringTime = TimeSpan.FromHours (12),
			 WaterLimitLitres = 1.4m
			 });
		var snapshot = Decode (record);
		string edited = TimerPlanSettings.EditSeason (snapshot, Enumerable.Repeat (200, 12).ToArray ());
		Assert.That (edited.Split ('|')[0].Split (',')[1], Is.EqualTo (record + "/"));
		}
	}
// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Globalization;
using System.Linq;

namespace RainPointClient.Protocol;

internal static class TimerMoistureRule
	{
	internal static void Decode (RainPointScheduleSnapshot snapshot)
		{
		snapshot.MoistureWateringRule = null;
		snapshot.MoistureRuleAvailability = TimerReadingAvailability.NotReported;
		if (string.IsNullOrEmpty (snapshot.Parameter))
			return;
		if (snapshot.PortNumber != 3 || snapshot.Parameter!.Contains ("="))
			{
			snapshot.MoistureRuleAvailability = TimerReadingAvailability.UnsupportedFormat;
			return;
			}
		string[] zones = snapshot.Parameter.Split ('|');
		if (zones.Length != 3 || snapshot.Zone is < 1 or > 3)
			{
			snapshot.MoistureRuleAvailability = TimerReadingAvailability.Malformed;
			return;
			}
		string[] fields = zones[snapshot.Zone - 1].Split (',');
		if (!zones[snapshot.Zone - 1].Contains ("/"))
			{
			snapshot.MoistureRuleAvailability = TimerReadingAvailability.UnsupportedFormat;
			return;
			}
		if (fields.Length < 3 || fields[2].Length == 0)
			return;
		string text = fields[2];
		if (text.Length < 16 || text.Length % 2 != 0)
			{
			snapshot.MoistureRuleAvailability = TimerReadingAvailability.Malformed;
			return;
			}
		byte[] b = new byte[text.Length / 2];
		for (int i = 0; i < b.Length; i++)
			if (!byte.TryParse (text.Substring (2 * i, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out b[i]))
				{
				snapshot.MoistureRuleAvailability = TimerReadingAvailability.Malformed;
				return;
				}
		int packed = b[1] | b[2] << 8 | b[3] << 16;
		int startHour = packed >> 7 & 31, startMinute = packed >> 1 & 63, endHour = packed >> 18 & 31, endMinute = packed >> 12 & 63;
		int seconds = b[4] | b[5] << 8;
		int volume = b[6] | b[7] << 8;
		if ((b[0] & 127) > 99 || startHour > 23 || endHour > 23 || startMinute > 59 || endMinute > 59)
			{
			snapshot.MoistureRuleAvailability = TimerReadingAvailability.Malformed;
			return;
			}
		snapshot.MoistureWateringRule = new RainPointMoistureWateringRule
			{
			Enabled = (b[0] & 128) != 0,
			StartBelowMoisturePercent = b[0] & 127,
			Mode = (packed & 0x800000) == 0 ? RainPointScheduleMode.Irrigation : RainPointScheduleMode.Misting,
			Duration = seconds is 0 or 43260 ? null : TimeSpan.FromSeconds (seconds),
			WaterLimitLitres = volume == 0 ? null : volume / 10m,
			ExcludedFrom = (packed & 1) == 0 ? null : new TimeSpan (startHour, startMinute, 0),
			ExcludedUntil = (packed & 1) == 0 ? null : new TimeSpan (endHour, endMinute, 0)
			};
		snapshot.MoistureRuleAvailability = TimerReadingAvailability.Decoded;
		}
	internal static string Edit (RainPointScheduleSnapshot snapshot, RainPointMoistureWateringRule rule)
		{
		if (snapshot is null)
			throw new ArgumentNullException (nameof (snapshot));
		if (rule is null)
			throw new ArgumentNullException (nameof (rule));
		if (snapshot.Availability != TimerReadingAvailability.Decoded || snapshot.Parameter is null || snapshot.PortNumber != 3 || snapshot.Zone is < 1 or > 3
		 || snapshot.MoistureRuleAvailability is not (TimerReadingAvailability.Decoded or TimerReadingAvailability.NotReported)
		 || !int.TryParse (snapshot.FirmwareVersion, out int version) || version < 120)
			throw new NotSupportedException ("Use a readable modern timer snapshot on firmware 120 or newer.");
		if (rule.StartBelowMoisturePercent is < 1 or > 99 || rule.Mode is not (RainPointScheduleMode.Irrigation or RainPointScheduleMode.Misting))
			throw new ArgumentException ("Use moisture 1..99 and normal or misting mode.", nameof (rule));
		if (rule.Duration.HasValue && (rule.Duration.Value.Ticks % TimeSpan.TicksPerMinute != 0 || rule.Duration.Value.TotalMinutes < 1 || rule.Duration.Value.TotalMinutes > 30))
			throw new ArgumentException ("Duration must be 1..30 whole minutes.", nameof (rule));
		if (rule.WaterLimitLitres.HasValue && (rule.WaterLimitLitres < .3m || rule.WaterLimitLitres > 6000m || rule.WaterLimitLitres * 10 != decimal.Truncate (rule.WaterLimitLitres.Value * 10)))
			throw new ArgumentException ("Use 0.3..6000 litres in 0.1 litre steps.", nameof (rule));
		if (!rule.Duration.HasValue && !rule.WaterLimitLitres.HasValue)
			throw new ArgumentException ("Supply a duration or volume limit.", nameof (rule));
		if (rule.Enabled && (snapshot.SoilSensorAvailability != TimerReadingAvailability.Decoded || snapshot.SoilSensorSettings?.SensorAddress is null))
			throw new InvalidOperationException ("Associate a soil sensor before enabling automatic watering.");
		bool excluded = rule.ExcludedFrom.HasValue;
		if (excluded != rule.ExcludedUntil.HasValue)
			throw new ArgumentException ("Supply both exclusion times.", nameof (rule));
		int packed = rule.Mode == RainPointScheduleMode.Misting ? 0x800000 : 0;
		if (excluded)
			{
			var start = rule.ExcludedFrom!.Value;
			var end = rule.ExcludedUntil!.Value;
			if (start < TimeSpan.Zero || end < TimeSpan.Zero || start >= TimeSpan.FromDays (1) || end >= TimeSpan.FromDays (1) || start.Ticks % TimeSpan.TicksPerMinute != 0 || end.Ticks % TimeSpan.TicksPerMinute != 0 || start == end)
				throw new ArgumentException ("Use distinct whole-minute home-local exclusion times within one day.", nameof (rule));
			int availableMinutes = start > end ? (int)(start - end).TotalMinutes : 1440 - (int)(end - start).TotalMinutes;
			if (rule.Duration.HasValue && rule.Duration.Value.TotalMinutes > availableMinutes)
				throw new ArgumentException ("Duration exceeds the daily watering window.", nameof (rule));
			packed |= 1 | start.Minutes << 1 | start.Hours << 7 | end.Minutes << 12 | end.Hours << 18;
			}
		int seconds = rule.Duration.HasValue ? (int)rule.Duration.Value.TotalSeconds : 43260;
		int volume = (int)((rule.WaterLimitLitres ?? 0) * 10);
		byte[] b = { (byte)(rule.StartBelowMoisturePercent | (rule.Enabled ? 128 : 0)), (byte)packed, (byte)(packed >> 8), (byte)(packed >> 16), (byte)seconds, (byte)(seconds >> 8), (byte)volume, (byte)(volume >> 8) };
		string[] zones = snapshot.Parameter.Split ('|');
		string[] fields = zones[snapshot.Zone - 1].Split (',');
		if (zones.Length != 3 || fields.Length < 4 || !zones[snapshot.Zone - 1].Contains ("/"))
			throw new NotSupportedException ("Only the verified modern settings container can be edited.");
		fields[2] = string.Concat (b.Select (value => value.ToString ("x2", CultureInfo.InvariantCulture))) + (fields[2].Length > 16 ? fields[2].Substring (16) : string.Empty);
		zones[snapshot.Zone - 1] = string.Join (",", fields);
		return string.Join ("|", zones);
		}
	}
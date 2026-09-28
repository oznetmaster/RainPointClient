using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RainPointClient.Protocol;

internal static class TimerPlanSettings
	{
	internal static void Decode (RainPointScheduleSnapshot snapshot)
		{
		snapshot.SeasonalAdjustmentAvailability = TimerReadingAvailability.NotReported;
		snapshot.RainDelayAvailability = TimerReadingAvailability.NotReported;
		if (string.IsNullOrEmpty (snapshot.Parameter))
			return;
		if (snapshot.PortNumber != 3 || snapshot.Parameter!.IndexOf ('=') >= 0)
			{
			snapshot.SeasonalAdjustmentAvailability = snapshot.RainDelayAvailability = TimerReadingAvailability.UnsupportedFormat;
			return;
			}
		string[] ports = snapshot.Parameter.Split ('|');
		if (ports.Length != 3 || snapshot.Zone is < 1 or > 3)
			{
			snapshot.SeasonalAdjustmentAvailability = snapshot.RainDelayAvailability = TimerReadingAvailability.Malformed;
			return;
			}
		string[] fields = ports[snapshot.Zone - 1].Split (',');
		// Only modern containers identify field four as seasonal percentages.
		if (fields.Length >= 4 && ports[snapshot.Zone - 1].IndexOf ('/') >= 0 && fields[3].Length > 0)
			{
			if (TryBytes (fields[3], out byte[] months) && months.Length == 12 && months.All (value => value is >= 10 and <= 200))
				{
				snapshot.SeasonalPercentages = Array.AsReadOnly (months.Select (value => (int)value).ToArray ());
				snapshot.SeasonalAdjustmentAvailability = TimerReadingAvailability.Decoded;
				}
			else
				snapshot.SeasonalAdjustmentAvailability = TimerReadingAvailability.Malformed;
			}
		if (fields[0].Length == 0)
			return;
		if (fields[0].Length < 24 || !TryBytes (fields[0], out byte[] settings))
			{
			snapshot.RainDelayAvailability = TimerReadingAvailability.Malformed;
			return;
			}
		uint packed = (uint)(settings[8] | settings[9] << 8 | settings[10] << 16 | settings[11] << 24);
		if (packed == 0)
			{
			snapshot.RainDelayUntil = null;
			snapshot.RainDelayAvailability = TimerReadingAvailability.Decoded;
			return;
			}
		int year = 2020 + (int)(packed >> 26), month = (int)(packed >> 22 & 15), day = (int)(packed >> 17 & 31);
		int hour = (int)(packed >> 12 & 31), minute = (int)(packed >> 6 & 63), second = (int)(packed & 63);
		if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth (year, month) || hour > 23 || minute > 59 || second > 59)
			{
			snapshot.RainDelayAvailability = TimerReadingAvailability.Malformed;
			return;
			}
		snapshot.RainDelayUntil = new DateTime (year, month, day, hour, minute, second, DateTimeKind.Unspecified);
		snapshot.RainDelayAvailability = TimerReadingAvailability.Decoded;
		}

	internal static string EditSeason (RainPointScheduleSnapshot snapshot, IReadOnlyList<int> percentages)
		{
		string[] ports = WritablePorts (snapshot);
		if (percentages is null)
			throw new ArgumentNullException (nameof (percentages));
		int[] months = percentages.ToArray ();
		if (months.Length != 12 || months.Any (value => value is < 10 or > 200))
			throw new ArgumentException ("Supply twelve monthly percentages, January through December, each from 10 to 200.", nameof (percentages));
		if (snapshot.SeasonalAdjustmentAvailability != TimerReadingAvailability.Decoded)
			throw new NotSupportedException ("A complete existing monthly adjustment is required; missing defaults are not guessed.");
		ValidateDurations (snapshot.Schedules, months.Max ());
		string[] fields = ports[snapshot.Zone - 1].Split (',');
		fields[3] = string.Concat (months.Select (value => value.ToString ("x2", CultureInfo.InvariantCulture)));
		ports[snapshot.Zone - 1] = string.Join (",", fields);
		return string.Join ("|", ports);
		}

	internal static string EditRainDelay (RainPointScheduleSnapshot snapshot, DateTime? endsAtLocal)
		{
		string[] ports = WritablePorts (snapshot);
		if (snapshot.RainDelayAvailability != TimerReadingAvailability.Decoded)
			throw new NotSupportedException ("A readable existing rain-delay field is required.");
		DateTime end = endsAtLocal ?? new DateTime (2020, 1, 1);
		if (end.Kind != DateTimeKind.Unspecified || end.Year is < 2020 or > 2083 || end.Ticks % TimeSpan.TicksPerSecond != 0)
			throw new ArgumentException ("Use home-local time with Unspecified kind, whole seconds, in 2020..2083.", nameof (endsAtLocal));
		uint packed = endsAtLocal.HasValue ? (uint)end.Second | (uint)end.Minute << 6 | (uint)end.Hour << 12 | (uint)end.Day << 17 | (uint)end.Month << 22 | (uint)(end.Year - 2020) << 26 : 0;
		string encoded = string.Concat (Enumerable.Range (0, 4).Select (i => ((packed >> (i * 8)) & 255).ToString ("x2", CultureInfo.InvariantCulture)));
		string[] fields = ports[snapshot.Zone - 1].Split (',');
		// Change only the four date bytes, preserving sensor flags, calibration and unknown suffixes.
		fields[0] = fields[0].Substring (0, 16) + encoded + fields[0].Substring (24);
		ports[snapshot.Zone - 1] = string.Join (",", fields);
		return string.Join ("|", ports);
		}

	private static string[] WritablePorts (RainPointScheduleSnapshot snapshot)
		{
		if (snapshot is null)
			throw new ArgumentNullException (nameof (snapshot));
		if (snapshot.Availability != TimerReadingAvailability.Decoded || snapshot.Parameter is null || snapshot.PortNumber != 3
			 || !int.TryParse (snapshot.FirmwareVersion, NumberStyles.None, CultureInfo.InvariantCulture, out int version) || version < 120)
			throw new NotSupportedException ("Use a decoded three-zone timer snapshot with firmware 120 or newer.");
		string[] ports = snapshot.Parameter.Split ('|');
		if (ports.Length != 3 || snapshot.Zone is < 1 or > 3 || snapshot.Parameter.IndexOf ('=') >= 0)
			throw new NotSupportedException ("Unsupported timer configuration.");
		return ports;
		}

	private static bool TryBytes (string text, out byte[] bytes)
		{
		bytes = new byte[text.Length / 2];
		if (text.Length % 2 != 0)
			return false;
		for (int i = 0; i < bytes.Length; i++)
			if (!byte.TryParse (text.Substring (i * 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out bytes[i]))
				return false;
		return true;
		}

	private static void ValidateDurations (IReadOnlyList<RainPointSchedule> plans, int percentage)
		{
		foreach (RainPointSchedule plan in plans)
			{
			// Match positive Math.round from the app; volume-limited plans retain their configured duration.
			double seconds = Math.Max (60, Math.Floor (plan.Duration.TotalSeconds * (plan.WaterLimitLitres.HasValue ? 1 : percentage / 100d) / 60 + 0.5) * 60);
			if (seconds > (plan.Mode == RainPointScheduleMode.CycleAndSoak ? 86400 : 43200))
				throw new ArgumentException ("The seasonal adjustment would exceed a plan's supported duration.");
			if (plan.Mode != RainPointScheduleMode.CycleAndSoak)
				continue;
			if (plan.CycleWateringTime is not { } watering || watering <= TimeSpan.Zero || plan.CyclePauseTime is not { } pause || pause < TimeSpan.Zero)
				throw new NotSupportedException ("Cycle timing is incomplete; seasonal duration cannot be checked.");
			double elapsed = seconds + (Math.Ceiling (seconds / watering.TotalSeconds) - 1) * pause.TotalSeconds;
			int days = plan.Repeat switch
				{
					RainPointScheduleRepeat.Once => int.MaxValue,
					RainPointScheduleRepeat.EveryDay or RainPointScheduleRepeat.OddDays => 1,
					RainPointScheduleRepeat.EvenDays => 2,
					RainPointScheduleRepeat.IntervalDays => plan.Interval!.Value,
					RainPointScheduleRepeat.Weekdays => WeekdayGap (plan.Weekdays),
					_ => throw new NotSupportedException ("Cannot validate seasonal timing for this repeat mode.")
					};
			if (elapsed > (long)days * 86400)
				throw new ArgumentException ("Adjusted watering and pauses exceed the plan's repeat interval.");
			}
		}

	private static int WeekdayGap (IReadOnlyList<DayOfWeek> weekdays)
		{
		int[] days = weekdays.Select (day => (int)day).OrderBy (day => day).ToArray ();
		if (days.Length == 0)
			throw new NotSupportedException ("A repeating plan has no weekdays.");
		int gap = days[0] + 7 - days[days.Length - 1];
		for (int i = 1; i < days.Length; i++)
			gap = Math.Min (gap, days[i] - days[i - 1]);
		return gap;
		}
	}
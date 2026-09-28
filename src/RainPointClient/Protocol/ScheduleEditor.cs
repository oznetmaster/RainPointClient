using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace RainPointClient.Protocol;

internal enum ScheduleEdit
	{
	Add, Replace, Delete, SetEnabled
	}

internal sealed class TimerParameterRequest
	{
	[JsonPropertyName ("mid")]
	public long HubId
		{
		get; set;
		}
	[JsonPropertyName ("sid")]
	public long DeviceId
		{
		get; set;
		}
	[JsonPropertyName ("param")] public string Parameter { get; set; } = string.Empty;
	}

internal static class ScheduleEditor
	{
	internal static string Encode (RainPointIrrigationSchedule schedule)
		{
		if (schedule is null)
			throw new ArgumentNullException (nameof (schedule));
		if (schedule.Duration < TimeSpan.FromMinutes (1) || schedule.Duration > TimeSpan.FromHours (12)
			 || schedule.Duration.Ticks % TimeSpan.TicksPerSecond != 0)
			throw new ArgumentException ("Normal duration must be 60..43200 whole seconds.", nameof (schedule));
		return EncodeCore (schedule.Enabled, schedule.StartTime, schedule.Repeat, schedule.Weekdays,
			 schedule.Interval, schedule.EffectiveDate, schedule.WaterLimitLitres, 1, (int)schedule.Duration.TotalSeconds, 0, 0);
		}

	internal static string EncodeCycleAndSoak (RainPointCycleAndSoakSchedule schedule)
		{
		if (schedule is null)
			throw new ArgumentNullException (nameof (schedule));
		if (!WholeMinutes (schedule.Duration, 5, 1440)
			 || !WholeMinutes (schedule.CycleWateringTime, 1, 720)
			 || !WholeMinutes (schedule.CyclePauseTime, 1, 720)
			 || schedule.CycleWateringTime > schedule.Duration)
			throw new ArgumentException ("Cycle-and-soak requires 5..1440 whole watering minutes; each cycle and pause must be 1..720 whole minutes, with a cycle no longer than total watering.", nameof (schedule));
		return EncodeCore (schedule.Enabled, schedule.StartTime, schedule.Repeat, schedule.Weekdays,
			 schedule.Interval, schedule.EffectiveDate, schedule.WaterLimitLitres, 3, (int)schedule.Duration.TotalMinutes,
			 (int)schedule.CycleWateringTime.TotalMinutes, (int)schedule.CyclePauseTime.TotalMinutes);
		}

	internal static string EncodeMisting (RainPointMistingSchedule schedule)
		{
		if (schedule is null)
			throw new ArgumentNullException (nameof (schedule));
		if (!WholeMinutes (schedule.Duration, 1, 720)
			 || !MistingSeconds (schedule.CycleWateringTime) || !MistingSeconds (schedule.CyclePauseTime))
			throw new ArgumentException ("Misting requires 1..720 whole duration minutes and 5..3600 whole seconds for each burst and pause.", nameof (schedule));
		return EncodeCore (schedule.Enabled, schedule.StartTime, schedule.Repeat, schedule.Weekdays,
			 schedule.Interval, schedule.EffectiveDate, schedule.WaterLimitLitres, 2, (int)schedule.Duration.TotalSeconds,
			 (int)schedule.CycleWateringTime.TotalSeconds, (int)schedule.CyclePauseTime.TotalSeconds);
		}

	private static bool MistingSeconds (TimeSpan value) =>
		 value >= TimeSpan.FromSeconds (5) && value <= TimeSpan.FromHours (1)
		 && value.Ticks % TimeSpan.TicksPerSecond == 0;

	private static bool WholeMinutes (TimeSpan value, int minimum, int maximum) =>
		 value >= TimeSpan.FromMinutes (minimum) && value <= TimeSpan.FromMinutes (maximum)
		 && value.Ticks % TimeSpan.TicksPerMinute == 0;

	private static string EncodeCore (bool enabled, TimeSpan startTime, RainPointScheduleRepeat repeat,
		 IReadOnlyList<DayOfWeek> weekdays, int? interval, DateTime? effectiveDate, decimal? waterLimit,
		 int mode, int duration, int watering, int pause)
		{
		if (startTime < TimeSpan.Zero || startTime >= TimeSpan.FromDays (1) || startTime.Ticks % TimeSpan.TicksPerMinute != 0)
			throw new ArgumentException ("Start time must be a local whole minute from 00:00 to 23:59.", "schedule");
		if (repeat is < RainPointScheduleRepeat.EveryDay or > RainPointScheduleRepeat.IntervalDays)
			{
			throw new NotSupportedException ("HTV345FRF plan writes support daily, odd/even days, selected weekdays and interval days. Once is readable but cannot be created or enabled.");
			}
		DayOfWeek[] days = weekdays?.ToArray () ?? throw new ArgumentException ("Weekdays cannot be null.", "schedule");
		if (days.Any (day => day < DayOfWeek.Sunday || day > DayOfWeek.Saturday) || days.Distinct ().Count () != days.Length
			 || (repeat == RainPointScheduleRepeat.Weekdays ? days.Length == 0 : days.Length != 0))
			{
			throw new ArgumentException ("Select distinct weekdays only for the weekday repeat mode.", "schedule");
			}
		if (repeat == RainPointScheduleRepeat.IntervalDays ? interval is null or < 1 or > 127 : interval is not null)
			{
			throw new ArgumentException ("IntervalDays requires an interval of 1..127; other repeats must omit it.", "schedule");
			}
		DateTime? date = effectiveDate;
		if (date.HasValue && (date.Value.Kind != DateTimeKind.Unspecified || date.Value.TimeOfDay != TimeSpan.Zero || date.Value.Year is < 2020 or > 2083)
			 || (!date.HasValue && repeat is RainPointScheduleRepeat.Once or RainPointScheduleRepeat.IntervalDays))
			{
			throw new ArgumentException ("Once and interval-day plans require a local date; dates must be midnight with Unspecified kind in 2020..2083.", "schedule");
			}
		decimal water = waterLimit ?? 0;
		if (waterLimit.HasValue && (water < 0.3m || water > 6000m || decimal.Truncate (water * 10) != water * 10))
			{
			throw new ArgumentException ("Water limit must be 0.3..6000 litres, in tenths of a litre.", "schedule");
			}
		if (mode == 3)
			{
			long elapsed = duration + (long)((duration + watering - 1) / watering - 1) * pause;
			int repeatDays = repeat switch
				{
					RainPointScheduleRepeat.Once => int.MaxValue,
					RainPointScheduleRepeat.EvenDays => 2,
					RainPointScheduleRepeat.IntervalDays => interval!.Value,
					RainPointScheduleRepeat.Weekdays => MinimumWeekdayGap (days),
					_ => 1
					};
			if (elapsed > (long)repeatDays * 1440)
				throw new ArgumentException ("Watering plus pauses must fit before the next occurrence at 100% seasonal adjustment.", "schedule");
			}
		int repeatDetail = interval ?? days.Sum (day => 1 << (int)day);
		byte[] bytes = new byte[mode == 1 ? 9 : 13];
		bytes[0] = (byte)(repeatDetail | (enabled ? 128 : 0));
		void Word (int offset, int value)
			{
			bytes[offset] = (byte)value;
			bytes[offset + 1] = (byte)(value >> 8);
			}
		Word (1, startTime.Minutes | startTime.Hours << 6 | (int)repeat << 11 | mode << 14);
		Word (3, duration);
		Word (5, (int)(water * 10));
		Word (7, date.HasValue ? date.Value.Day | date.Value.Month << 5 | (date.Value.Year - 2020) << 9 : 0);
		if (mode != 1)
			{
			Word (9, watering);
			Word (11, pause);
			}
		return BitConverter.ToString (bytes).Replace ("-", "").ToLowerInvariant ();
		}

	private static int MinimumWeekdayGap (DayOfWeek[] days)
		{
		int[] ordered = days.Select (day => (int)day).OrderBy (day => day).ToArray ();
		int minimum = ordered[0] + 7 - ordered[ordered.Length - 1];
		for (int i = 1; i < ordered.Length; i++)
			minimum = Math.Min (minimum, ordered[i] - ordered[i - 1]);
		return minimum;
		}

	internal static string Edit (RainPointScheduleSnapshot expected, ScheduleEdit edit, int index, string? record, bool enabled)
		{
		if (expected.PortNumber != 3 || !int.TryParse (expected.FirmwareVersion, NumberStyles.None, CultureInfo.InvariantCulture, out int version) || version < 120)
			{
			throw new NotSupportedException ("Schedule writes require the three-zone HTV345FRF configuration and firmware 120 or newer.");
			}
		string[] ports = expected.Parameter!.Split ('|');
		if (ports.Length != 3 || expected.Zone is < 1 or > 3 || expected.Parameter.IndexOf ('=') >= 0)
			{
			throw new NotSupportedException ("This schedule container cannot be safely edited.");
			}
		string port = ports[expected.Zone - 1];
		string[] fields = port.Split (',');
		if (fields.Length < 2 || fields[0].Length == 0)
			{
			throw new NotSupportedException ("The zone configuration is incomplete.");
			}
		bool modern = port.IndexOf ('/') >= 0;
		string payload = modern ? fields[1] : port.Substring (port.IndexOf (',') + 1);
		List<string> records = payload.Split (modern ? '/' : ',').Where (item => item.Length > 0).ToList ();
		if (records.Count != expected.Schedules.Count || records.Count > 6)
			{
			throw new NotSupportedException ("The plan list cannot be safely edited.");
			}
		if (edit == ScheduleEdit.Add)
			{
			if (records.Count >= 6)
				{
				throw new ArgumentException ("A zone supports at most six plans.");
				}
			records.Add (record!);
			}
		else
			{
			if (index < 0 || index >= records.Count || expected.Schedules[index].Index != index)
				{
				throw new ArgumentOutOfRangeException (nameof (index));
				}
			switch (edit)
				{
				case ScheduleEdit.Replace:
					records[index] = record!;
					break;
				case ScheduleEdit.Delete:
					records.RemoveAt (index);
					break;
				case ScheduleEdit.SetEnabled:
					if (enabled && expected.Schedules[index].Repeat == RainPointScheduleRepeat.Once)
						throw new NotSupportedException ("Once plans cannot be enabled on the supported HTV345FRF timer; disable or delete the existing record.");
					int flags = int.Parse (records[index].Substring (0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
					records[index] = ((flags & 127) | (enabled ? 128 : 0)).ToString ("x2", CultureInfo.InvariantCulture) + records[index].Substring (2);
					break;
				}
			}
		// Keep untouched records byte-for-byte, including their order and optional fields.
		if (modern)
			{
			fields[1] = records.Count == 0 ? "/" : string.Join ("/", records) + (records.Count == 1 ? "/" : "");
			ports[expected.Zone - 1] = string.Join (",", fields);
			}
		else
			{
			ports[expected.Zone - 1] = fields[0] + "," + string.Join (",", records);
			}
		return string.Join ("|", ports);
		}
	}
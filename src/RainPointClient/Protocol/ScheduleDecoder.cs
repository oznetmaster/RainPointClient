// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace RainPointClient.Protocol;

internal static class ScheduleDecoder
	{
	internal static RainPointScheduleSnapshot Decode (RainPointDevice device, int zone)
		{
		RainPointScheduleSnapshot Result (TimerReadingAvailability availability, RainPointSchedule[]? plans = null)
			 => new (device.Address, zone, availability, Array.AsReadOnly (plans ?? []));

		if (device.Parameter is null || device.Parameter.Length == 0)
			{
			return Result (TimerReadingAvailability.NotReported);
			}
		// This decoder supports the RF container only. DP key/value layouts need their own schema.
		if (device.Parameter.IndexOf ('=') >= 0 || device.PortNumber is not (>= 1 and <= 3))
			{
			return Result (TimerReadingAvailability.UnsupportedFormat);
			}
		string[] ports = device.Parameter.Split ('|');
		if (ports.Length != device.PortNumber || zone < 1 || zone > device.PortNumber)
			{
			return Result (TimerReadingAvailability.Malformed);
			}
		string port = ports[zone - 1];
		int separator = port.IndexOf (',');
		if (separator < 0)
			{
			return Result (TimerReadingAvailability.NotReported);
			}
		bool modern = port.IndexOf ('/') >= 0;
		string payload = modern ? port.Split (',')[1] : port.Substring (separator + 1);
		if (payload.Length == 0 || payload == "/")
			{
			return Result (TimerReadingAvailability.Decoded);
			}
		string[] records = payload.Split (modern ? '/' : ',');
		List<RainPointSchedule> plans = [];
		for (int index = 0; index < records.Length; index++)
			{
			// The modern writer appends a slash to a single record to identify the container format.
			if (modern && records[index].Length == 0 && index == records.Length - 1)
				{
				continue;
				}
			TimerReadingAvailability availability = DecodeRecord (records[index], index, out RainPointSchedule? plan);
			if (availability != TimerReadingAvailability.Decoded)
				{
				return Result (availability);
				}
			plans.Add (plan!);
			}
		return Result (TimerReadingAvailability.Decoded, plans.ToArray ());
		}

	private static TimerReadingAvailability DecodeRecord (string record, int index, out RainPointSchedule? plan)
		{
		plan = null;
		if (record.Length < 10 || record.Length % 2 != 0)
			{
			return TimerReadingAvailability.Malformed;
			}
		if (record.Length is not (10 or 14 or 18 or 26))
			{
			return TimerReadingAvailability.UnsupportedFormat;
			}
		byte[] bytes = new byte[record.Length / 2];
		for (int i = 0; i < bytes.Length; i++)
			{
			if (!byte.TryParse (record.Substring (i * 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out bytes[i]))
				{
				return TimerReadingAvailability.Malformed;
				}
			}
		int Word (int offset) => bytes[offset] | bytes[offset + 1] << 8;
		int packed = Word (1);
		int minute = packed & 63;
		int hour = packed >> 6 & 31;
		int repeat = packed >> 11 & 7;
		int mode = packed >> 14 & 3;
		if (mode == 0 || repeat == 7)
			{
			return TimerReadingAvailability.UnsupportedFormat;
			}
		int detail = bytes[0] & 127;
		if (hour > 23 || minute > 59 || (repeat is 5 or 6 && detail == 0))
			{
			return TimerReadingAvailability.Malformed;
			}
		DateTime? date = null;
		if (bytes.Length >= 9 && Word (7) != 0)
			{
			int encodedDate = Word (7);
			int year = 2020 + (encodedDate >> 9 & 63);
			int month = encodedDate >> 5 & 15;
			int day = encodedDate & 31;
			if ((encodedDate & 32768) != 0 || month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth (year, month))
				{
				return TimerReadingAvailability.Malformed;
				}
			date = new DateTime (year, month, day, 0, 0, 0, DateTimeKind.Unspecified);
			}
		List<DayOfWeek> weekdays = [];
		if (repeat == 4)
			{
			for (int day = 0; day < 7; day++)
				{
				if ((detail & 1 << day) != 0)
					{
					weekdays.Add ((DayOfWeek)day);
					}
				}
			}
		int secondsPerUnit = mode == 3 ? 60 : 1;
		plan = new RainPointSchedule (index, (bytes[0] & 128) != 0, (RainPointScheduleMode)mode,
			 new TimeSpan (hour, minute, 0), TimeSpan.FromSeconds (Word (3) * secondsPerUnit),
			 (RainPointScheduleRepeat)repeat, weekdays.AsReadOnly (), repeat is 5 or 6 ? detail : null,
			 bytes.Length >= 7 && Word (5) > 0 ? Word (5) / 10m : null, date,
			 bytes.Length == 13 ? TimeSpan.FromSeconds (Word (9) * secondsPerUnit) : null,
			 bytes.Length == 13 ? TimeSpan.FromSeconds (Word (11) * secondsPerUnit) : null);
		return TimerReadingAvailability.Decoded;
		}
	}
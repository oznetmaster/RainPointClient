// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Globalization;

namespace RainPointClient.Protocol;

internal static class TimerZoneDefaults
	{
	internal static void Decode (RainPointScheduleSnapshot snapshot)
		{
		snapshot.ZoneDefaults = null;
		snapshot.ZoneDefaultsAvailability = TimerReadingAvailability.NotReported;
		if (string.IsNullOrEmpty (snapshot.Parameter))
			{
			return;
			}
		if (snapshot.PortNumber != 3 || snapshot.Parameter!.IndexOf ('=') >= 0)
			{
			snapshot.ZoneDefaultsAvailability = TimerReadingAvailability.UnsupportedFormat;
			return;
			}
		string[] ports = snapshot.Parameter.Split ('|');
		if (ports.Length != 3 || snapshot.Zone is < 1 or > 3)
			{
			snapshot.ZoneDefaultsAvailability = TimerReadingAvailability.Malformed;
			return;
			}
		string text = ports[snapshot.Zone - 1].Split (',')[0];
		if (text.Length == 0)
			{
			return;
			}
		if (text.Length < 24 || text.Length % 2 != 0)
			{
			snapshot.ZoneDefaultsAvailability = TimerReadingAvailability.Malformed;
			return;
			}
		byte[] bytes = new byte[text.Length / 2];
		for (int i = 0; i < bytes.Length; i++)
			{
			if (!byte.TryParse (text.Substring (i * 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out bytes[i]))
				{
				snapshot.ZoneDefaultsAvailability = TimerReadingAvailability.Malformed;
				return;
				}
			}
		int duration = bytes[0] | bytes[1] << 8, run = bytes[2] | bytes[3] << 8, interval = bytes[4] | bytes[5] << 8;
		// Read whole-second legacy values faithfully; the current app's duration picker writes whole minutes.
		if ((duration != 0 && duration is < 60 or > 43200) || (run != 0 && run is < 5 or > 3600) || (interval != 0 && interval is < 5 or > 3600))
			{
			snapshot.ZoneDefaultsAvailability = TimerReadingAvailability.Malformed;
			return;
			}
		snapshot.ZoneDefaults = new RainPointZoneDefaults (Value (duration), Value (run), Value (interval));
		snapshot.ZoneDefaultsAvailability = TimerReadingAvailability.Decoded;
		}

	internal static string EditDuration (RainPointScheduleSnapshot snapshot, TimeSpan? duration)
		{
		int seconds = Seconds (duration, 60, 43200, TimeSpan.TicksPerMinute, nameof (duration));
		return Edit (snapshot, 0, Encode (seconds));
		}

	internal static string EditMisting (RainPointScheduleSnapshot snapshot, TimeSpan? runTime, TimeSpan? interval)
		{
		int run = Seconds (runTime, 5, 3600, TimeSpan.TicksPerSecond, nameof (runTime));
		int pause = Seconds (interval, 5, 3600, TimeSpan.TicksPerSecond, nameof (interval));
		return Edit (snapshot, 4, Encode (run) + Encode (pause));
		}

	private static string Edit (RainPointScheduleSnapshot snapshot, int start, string replacement)
		{
		if (snapshot is null)
			{
			throw new ArgumentNullException (nameof (snapshot));
			}
		if (snapshot.Availability != TimerReadingAvailability.Decoded || snapshot.ZoneDefaultsAvailability != TimerReadingAvailability.Decoded
		 || snapshot.ZoneDefaults is null || snapshot.Parameter is null || snapshot.PortNumber != 3 || snapshot.Zone is < 1 or > 3
		 || !int.TryParse (snapshot.FirmwareVersion, NumberStyles.None, CultureInfo.InvariantCulture, out int firmware) || firmware < 120)
			{
			throw new NotSupportedException ("Use a decoded three-zone settings snapshot with firmware 120 or newer.");
			}
		string[] ports = snapshot.Parameter.Split ('|');
		if (ports.Length != 3)
			{
			throw new NotSupportedException ("Only the verified three-zone container can be edited.");
			}
		string[] fields = ports[snapshot.Zone - 1].Split (',');
		if (fields.Length < 4 || ports[snapshot.Zone - 1].IndexOf ('/') < 0 || snapshot.Parameter.IndexOf ('=') >= 0)
			{
			throw new NotSupportedException ("Only the verified modern settings container can be edited.");
			}
		fields[0] = fields[0].Substring (0, start) + replacement + fields[0].Substring (start + replacement.Length);
		ports[snapshot.Zone - 1] = string.Join (",", fields);
		return string.Join ("|", ports);
		}

	private static int Seconds (TimeSpan? value, int min, int max, long resolution, string name)
		{
		if (!value.HasValue)
			{
			return 0;
			}
		if (value.Value.Ticks % resolution != 0 || value.Value.TotalSeconds < min || value.Value.TotalSeconds > max)
			{
			throw new ArgumentOutOfRangeException (name, "Use the app's duration range and precision, or null for its default sentinel.");
			}
		return (int)value.Value.TotalSeconds;
		}

	private static TimeSpan? Value (int seconds) => seconds == 0 ? null : TimeSpan.FromSeconds (seconds);
	private static string Encode (int seconds) => (seconds & 255).ToString ("x2", CultureInfo.InvariantCulture) + (seconds >> 8).ToString ("x2", CultureInfo.InvariantCulture);
	}
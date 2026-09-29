// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol/compatibility reference: https://github.com/funkadelic/ha-rainpoint
// Additional reference: https://github.com/rathga/rainpoint-ha
// Independently written C# implementation. See ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace RainPointClient.Protocol;

// Wire framing is based on the attributed upstream references in THIRD-PARTY-NOTICES.md.
// Walk record boundaries; never search value bytes for apparent header markers.
internal static class TimerDecoder
	{
	internal static RainPointTimerStatus Decode (int address, int zoneCount, string? value, DateTimeOffset? changed)
		{
		RainPointTimerStatus Empty (TimerReadingAvailability availability) =>
			 new (address, availability, [], lastDataChange: changed);

		if (string.IsNullOrEmpty (value))
			{
			return Empty (TimerReadingAvailability.NotReported);
			}

		bool compact = zoneCount == 1 && value!.StartsWith ("10#", StringComparison.Ordinal);
		if (!compact && !value!.StartsWith ("11#", StringComparison.Ordinal) && !value.StartsWith ("01#", StringComparison.Ordinal))
			{
			return Empty (TimerReadingAvailability.UnsupportedFormat);
			}

		string hex = value!.Substring (3);
		if (hex.Length == 0 || hex.Length % 2 != 0)
			{
			return Empty (TimerReadingAvailability.Malformed);
			}

		byte[] bytes = new byte[hex.Length / 2];
		for (int i = 0; i < bytes.Length; i++)
			{
			if (!byte.TryParse (hex.Substring (i * 2, 2), NumberStyles.AllowHexSpecifier,
				 CultureInfo.InvariantCulture, out bytes[i]))
				{
				return Empty (TimerReadingAvailability.Malformed);
				}
			}

		Dictionary<int, byte[]> records = [];
		int offset = 0;
		while (offset < bytes.Length)
			{
			int dp = compact ? 0 : bytes[offset++];
			if (!compact && offset == bytes.Length)
				{
				return Empty (TimerReadingAvailability.Malformed);
				}

			byte header = bytes[offset++];
			int field;
			byte[] data;
			if ((header & 0x80) == 0)
				{
				field = (header >> 4) & 7;
				data = [header];
				}
			else
				{
				int index = (header >> 2) & 31;
				int width = (header & 3) + 1;
				if (index == 31)
					{
					if (offset == bytes.Length)
						{
						return Empty (TimerReadingAvailability.Malformed);
						}

					field = bytes[offset++] + 39;
					}
				else
					{
					field = index + 8;
					}

				if (bytes.Length - offset < width)
					{
					return Empty (TimerReadingAvailability.Malformed);
					}

				data = new byte[width];
				Array.Copy (bytes, offset, data, 0, width);
				offset += width;
				}

			records[(dp << 9) | field] = data;
			}

		byte[]? Read (int dp, int field) => records.TryGetValue (((compact ? 0 : dp) << 9) | field, out byte[]? data) ? data : null;
		List<RainPointZoneStatus> zones = [];
		for (int zone = 1; zone <= zoneCount; zone++)
			{
			byte[]? state = Read (0x18 + zone, 30);
			byte[]? duration = Read (0x24 + zone, 19);
			byte[]? usage = Read (0x28 + zone, 15);
			byte[]? eventTime = Read (0x20 + zone, 21);
			byte[]? alarm = Read (0x1C + zone, 2);
			byte? mode = state?.Length == 1 ? (byte)(state[0] & 15) : null;
			zones.Add (new RainPointZoneStatus (zone,
				 mode switch
					 {
						 0 => false,
						 1 or 2 or 3 or 7 => true,
						 _ => null
						 },
				 duration?.Length is 2 or 4 ? ReadUnsigned (duration) : null,
				 usage?.Length == 4 ? ReadUnsigned (usage) : null,
				 DecodeTimestamp (eventTime), alarm?.Length == 1 ? (byte)(alarm[0] & 15) : null, mode));
			}

		byte[]? rssi = Read (0x17, 32);
		byte[]? battery = Read (0x18, 31);
		return new RainPointTimerStatus (address, TimerReadingAvailability.Decoded, zones.AsReadOnly (),
			 rssi?.Length >= 1 && rssi[0] >= 128 ? rssi[0] - 256 : null,
			 battery?.Length == 1 ? battery[0] : null, changed, DecodeTimestamp (Read (0xFE, 54)));
		}

	private static DateTime? DecodeTimestamp (byte[]? value)
		{
		if (value?.Length != 4)
			{
			return null;
			}
		uint packed = ReadUnsigned (value);
		if (packed == 0)
			{
			return null;
			}
		try
			{
			return new DateTime (2020 + (int)(packed >> 26), (int)((packed >> 22) & 15),
				 (int)((packed >> 17) & 31), (int)((packed >> 12) & 31),
				 (int)((packed >> 6) & 63), (int)(packed & 63), DateTimeKind.Unspecified);
			}
		catch (ArgumentOutOfRangeException)
			{
			return null;
			}
		}

	private static uint ReadUnsigned (byte[] bytes)
		{
		uint result = 0;
		for (int i = 0; i < bytes.Length; i++)
			{
			result |= (uint)bytes[i] << (8 * i);
			}

		return result;
		}
	}
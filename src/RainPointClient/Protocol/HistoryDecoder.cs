// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Globalization;
using System.Linq;

namespace RainPointClient.Protocol;

/// <summary>
/// Internal history decoder representation or processing contract for the RainPoint protocol.
/// </summary>
internal static class HistoryDecoder
	{
	/// <summary>
	/// Decodes recognized history details while retaining unknown or malformed-detail availability.
	/// </summary>
	/// <param name="row">The attributed history record to decode.</param>
	/// <returns>The typed event with any independently decoded details retained.</returns>
	internal static RainPointEvent Decode (EventResponse row)
		{
		RainPointEvent result = new ()
			{
			Id = row.Id,
			HubId = row.HubId,
			Address = row.Address,
			Zone = row.Zone,
			Code = row.Code,
			CloudTimestamp = DateTimeOffset.FromUnixTimeMilliseconds (row.Timestamp),
			ReportedTimeZone = row.TimeZone,
			HasUninterpretedDetails = row.Rules is null
			};
		if (DateTime.TryParseExact (row.Time, new[] { "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm:ss" },
		 CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
			{
			result.ReportedLocalTime = local;
			}
		else if (!string.IsNullOrEmpty (row.Time))
			{
			result.HasUninterpretedDetails = true;
			}
		if (row.Rules is null)
			{
			return result;
			}
		foreach (var group in row.Rules.GroupBy (rule => rule?.Type))
			{
			if (group.Count () != 1 || group.Key is null)
				{
				result.HasUninterpretedDetails = true;
				continue;
				}
			string? value = group.Single ()?.Value;
			if (group.Key == "9")
				{
				result.Operator = value;
				result.HasUninterpretedDetails |= value is null;
				continue;
				}
			if (!long.TryParse (value, NumberStyles.None, CultureInfo.InvariantCulture, out long number))
				{
				result.HasUninterpretedDetails = true;
				continue;
				}
			switch (group.Key)
				{
				case "1" when number <= int.MaxValue:
					result.WorkModeCode = (int)number;
					break;
				case "2" when number <= int.MaxValue:
					result.ControlModeCode = (int)number;
					break;
				case "3":
					result.WaterUsedLitres = number / 10m;
					break;
				case "4" when number <= TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond:
					result.Duration = TimeSpan.FromTicks (number * TimeSpan.TicksPerSecond);
					break;
				case "8" when number is 0 or 1:
					result.IsOnline = number == 1;
					break;
				case "11" when number <= int.MaxValue:
					result.ExceptionCode = (int)number;
					break;
				default:
					result.HasUninterpretedDetails = true;
					break;
				}
			}
		return result;
		}
	}
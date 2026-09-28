// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;

namespace RainPointClient.Desktop.Core;

public sealed class UsageHistoryRow
	{
	internal UsageHistoryRow (RainPointWaterUsage value, bool monthly)
		{
		Period = value.PeriodStart.ToString (monthly ? "yyyy-MM" : "yyyy-MM-dd", CultureInfo.InvariantCulture);
		Litres = value.Litres?.ToString ("0.0", CultureInfo.CurrentCulture) ?? "Unknown";
		}
	public string Period
		{
		get;
		}
	public string Litres
		{
		get;
		}
	}

public sealed class EventHistoryRow
	{
	internal EventHistoryRow (RainPointEvent value) => Event = value;
	internal RainPointEvent Event
		{
		get;
		}
	internal string Id => Event.Id;
	public string Scope => Event.Zone == 0 && Event.Kind == RainPointEventKind.SubDevicePowerOn ? "Whole timer" : "Zone " + Event.Zone.ToString (CultureInfo.InvariantCulture);
	public string CloudTime => Event.CloudTimestamp.UtcDateTime.ToString ("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
	public string DeviceTime => Event.ReportedLocalTime?.ToString ("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "Unknown";
	public string Kind => Event.Kind switch
		{
			RainPointEventKind.Watering => "Watering",
			RainPointEventKind.WaterUsage => "Water usage",
			RainPointEventKind.WaterControl => "Water control",
			RainPointEventKind.HubStatus => "Hub status",
			RainPointEventKind.SubDevicePowerOn => "Power on",
			_ => "Unknown (" + Event.Code.ToString (CultureInfo.InvariantCulture) + ")"
			};
	public string Litres => Event.WaterUsedLitres?.ToString ("0.0", CultureInfo.CurrentCulture) ?? "Unknown";
	public string Duration => Event.Duration?.TotalSeconds.ToString ("0", CultureInfo.CurrentCulture) ?? "Unknown";
	public string Details => "Event: " + Kind + " · " + Scope + " · code " + Event.Code.ToString (CultureInfo.InvariantCulture)
	 + "\nCloud time: " + CloudTime + " UTC · Device-local time: " + DeviceTime + " · Reported timezone: " + (Event.ReportedTimeZone ?? "Unknown")
	 + "\nWater: " + Litres + " L · Duration: " + Duration + " seconds"
	 + "\nWork mode code: " + Code (Event.WorkModeCode) + " · Control mode code: " + Code (Event.ControlModeCode) + " · Exception code: " + Code (Event.ExceptionCode)
	 + "\nConnection: " + (Event.IsOnline.HasValue ? Event.IsOnline.Value ? "Online" : "Offline" : "Unknown") + " · Operator: " + (Event.Operator ?? "Unknown")
	 + (Event.HasUninterpretedDetails ? "\nSome event details could not be interpreted." : string.Empty);
	private static string Code (int? value) => value?.ToString (CultureInfo.InvariantCulture) ?? "Unknown";
	}
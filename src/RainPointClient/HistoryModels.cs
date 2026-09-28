using System;
using System.Collections.Generic;

namespace RainPointClient;

public enum RainPointUsagePeriod
	{
	Day, Month
	}

/// <summary>A reported cloud bucket. Omitted buckets are not manufactured as zero readings.</summary>
public sealed class RainPointWaterUsage
	{
	internal RainPointWaterUsage (DateTime date, decimal? litres)
		{
		PeriodStart = date;
		Litres = litres;
		}
	/// <summary>Home-calendar date, with Unspecified kind. Monthly buckets use the first day.</summary>
	public DateTime PeriodStart
		{
		get;
		}
	/// <summary>Litres, or null when the cloud supplied no usable reading.</summary>
	public decimal? Litres
		{
		get;
		}
	}

/// <summary>Optional filters for one bounded event request. Bounds use the cloud timestamp, not the reported local time.</summary>
public sealed class RainPointEventQuery
	{
	public long? HubId
		{
		get; set;
		}
	public int? Address
		{
		get; set;
		}
	public int? Zone
		{
		get; set;
		}
	/// <summary>Vendor event code. Null requests all event types.</summary>
	public int? Code
		{
		get; set;
		}
	public DateTimeOffset? Begin
		{
		get; set;
		}
	public DateTimeOffset? End
		{
		get; set;
		}
	/// <summary>1–50; the app requests 50 records per page.</summary>
	public int Limit { get; set; } = 50;
	}

public enum RainPointEventKind
	{
	Unknown = 0, Watering = 1, WaterUsage = 2, WaterControl = 3, HubStatus = 4, SubDevicePowerOn = 5,
	SocketControl = 6, ValveDetection = 9, AlarmCleared = 10, Rainfall = 14, OperationLog = 22, BugZapperControl = 27,
	WaterLeak = 128, ExcessiveWaterUsage = 129, ExcessiveEnergyUsage = 130, LowTemperature = 131,
	HighTemperature = 132, Freeze = 133, LowHumidity = 134, HighHumidity = 135, WaterShortage = 136,
	ExcessiveCarbonDioxide = 137, ExcessivePower = 139, ExcessiveCost = 140, ValveFailure = 141, LowBattery = 143,
	Blockage = 146, FanAbnormal = 147, LampFailure = 148, LowVoltage = 149, HighVoltage = 150,
	AbnormalProtection = 155, CityFreeze = 158, BatteryFault = 159
	}

/// <summary>A cloud event. Codes with unrecognized meanings remain available without exposing protocol payloads.</summary>
public sealed class RainPointEvent
	{
	internal RainPointEvent ()
		{
		}
	public string Id { get; internal set; } = string.Empty;
	public long HubId
		{
		get; internal set;
		}
	public int Address
		{
		get; internal set;
		}
	public int Zone
		{
		get; internal set;
		}
	public int Code
		{
		get; internal set;
		}
	public RainPointEventKind Kind => Enum.IsDefined (typeof (RainPointEventKind), Code) ? (RainPointEventKind)Code : RainPointEventKind.Unknown;
	/// <summary>The cloud's Unix-millisecond timestamp, also used for history query bounds.</summary>
	public DateTimeOffset CloudTimestamp
		{
		get; internal set;
		}
	/// <summary>Device-reported local time; independent of CloudTimestamp. No machine-local conversion is performed.</summary>
	public DateTime? ReportedLocalTime
		{
		get; internal set;
		}
	public string? ReportedTimeZone
		{
		get; internal set;
		}
	public decimal? WaterUsedLitres
		{
		get; internal set;
		}
	public TimeSpan? Duration
		{
		get; internal set;
		}
	public int? WorkModeCode
		{
		get; internal set;
		}
	public int? ControlModeCode
		{
		get; internal set;
		}
	public int? ExceptionCode
		{
		get; internal set;
		}
	public bool? IsOnline
		{
		get; internal set;
		}
	public string? Operator
		{
		get; internal set;
		}
	/// <summary>True when detail rules are missing, unknown, duplicated or malformed. Known independent details are retained.</summary>
	public bool HasUninterpretedDetails
		{
		get; internal set;
		}
	}

public sealed class RainPointEventPage
	{
	internal RainPointEventPage (IReadOnlyList<RainPointEvent> events, bool limitReached)
		{
		Events = events;
		IsLimitReached = limitReached;
		}
	public IReadOnlyList<RainPointEvent> Events
		{
		get;
		}
	/// <summary>The response filled the requested limit; more events may exist. This is not a total-count or completeness guarantee.</summary>
	public bool IsLimitReached
		{
		get;
		}
	/// <summary>Oldest returned cloud timestamp. Use as End for an older-page request and deduplicate by Id; timestamp ties have no documented cursor guarantee.</summary>
	public DateTimeOffset? OldestTimestamp
		{
		get
			{
			DateTimeOffset? oldest = null;
			foreach (RainPointEvent item in Events)
				{
				if (!oldest.HasValue || item.CloudTimestamp < oldest.Value)
					{
					oldest = item.CloudTimestamp;
					}
				}
			return oldest;
			}
		}
	}
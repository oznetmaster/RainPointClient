// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;

namespace RainPointClient;

/// <summary>
/// Selects daily or monthly aggregation of cloud water-usage totals.
/// </summary>
public enum RainPointUsagePeriod
	{
	/// <summary>Aggregate usage into daily home-calendar buckets.</summary>
	Day,
	/// <summary>Aggregate usage into monthly home-calendar buckets.</summary>
	Month
	}

/// <summary>A reported cloud bucket. Omitted buckets are not manufactured as zero readings.</summary>
public sealed class RainPointWaterUsage
	{
	/// <summary>
	/// Initializes water usage from the supplied typed values.
	/// </summary>
	/// <param name="date">The home-calendar bucket start date; monthly buckets use the first day.</param>
	/// <param name="litres">The cloud volume in litres, or null when unavailable.</param>
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
	/// <summary>
	/// Gets or sets an optional cloud hub-ID filter; null leaves hubs unfiltered.
	/// </summary>
	public long? HubId
		{
		get; set;
		}
	/// <summary>
	/// Gets or sets an optional child RF-address filter.
	/// </summary>
	public int? Address
		{
		get; set;
		}
	/// <summary>
	/// Gets or sets an optional one-based zone filter; the service may also return timer-wide events.
	/// </summary>
	public int? Zone
		{
		get; set;
		}
	/// <summary>Vendor event code. Null requests all event types.</summary>
	public int? Code
		{
		get; set;
		}
	/// <summary>
	/// Gets or sets the inclusive lower cloud-timestamp bound, or null for no lower bound.
	/// </summary>
	public DateTimeOffset? Begin
		{
		get; set;
		}
	/// <summary>
	/// Gets or sets the inclusive upper cloud-timestamp bound, or null for no upper bound.
	/// </summary>
	public DateTimeOffset? End
		{
		get; set;
		}
	/// <summary>1–50; the app requests 50 records per page.</summary>
	public int Limit { get; set; } = 50;
	}

/// <summary>
/// Classifies known vendor history event codes; device-family availability varies.
/// </summary>
public enum RainPointEventKind
	{
	/// <summary>
	/// The vendor reports unknown. Unknown codes retain their original numeric value.
	/// </summary>
	Unknown = 0,
	/// <summary>
	/// The vendor reports watering. This is cloud history, not independent physical confirmation.
	/// </summary>
	Watering = 1,
	/// <summary>
	/// The vendor reports water usage. This is cloud history, not independent physical confirmation.
	/// </summary>
	WaterUsage = 2,
	/// <summary>
	/// The vendor reports water control. This is cloud history, not independent physical confirmation.
	/// </summary>
	WaterControl = 3,
	/// <summary>
	/// The vendor reports hub status. This is cloud history, not independent physical confirmation.
	/// </summary>
	HubStatus = 4,
	/// <summary>
	/// The vendor reports sub device power on. This is cloud history, not independent physical confirmation.
	/// </summary>
	SubDevicePowerOn = 5,
	/// <summary>
	/// The vendor reports socket control. This is cloud history, not independent physical confirmation.
	/// </summary>
	SocketControl = 6,
	/// <summary>
	/// The vendor reports valve detection. This is cloud history, not independent physical confirmation.
	/// </summary>
	ValveDetection = 9,
	/// <summary>
	/// The vendor reports alarm cleared. This is cloud history, not independent physical confirmation.
	/// </summary>
	AlarmCleared = 10,
	/// <summary>
	/// The vendor reports rainfall. This is cloud history, not independent physical confirmation.
	/// </summary>
	Rainfall = 14,
	/// <summary>
	/// The vendor reports operation log. This is cloud history, not independent physical confirmation.
	/// </summary>
	OperationLog = 22,
	/// <summary>
	/// The vendor reports bug zapper control. This is cloud history, not independent physical confirmation.
	/// </summary>
	BugZapperControl = 27,
	/// <summary>
	/// The vendor reports water leak. This is cloud history, not independent physical confirmation.
	/// </summary>
	WaterLeak = 128,
	/// <summary>
	/// The vendor reports excessive water usage. This is cloud history, not independent physical confirmation.
	/// </summary>
	ExcessiveWaterUsage = 129,
	/// <summary>
	/// The vendor reports excessive energy usage. This is cloud history, not independent physical confirmation.
	/// </summary>
	ExcessiveEnergyUsage = 130,
	/// <summary>
	/// The vendor reports low temperature. This is cloud history, not independent physical confirmation.
	/// </summary>
	LowTemperature = 131,
	/// <summary>
	/// The vendor reports high temperature. This is cloud history, not independent physical confirmation.
	/// </summary>
	HighTemperature = 132,
	/// <summary>
	/// The vendor reports freeze. This is cloud history, not independent physical confirmation.
	/// </summary>
	Freeze = 133,
	/// <summary>
	/// The vendor reports low humidity. This is cloud history, not independent physical confirmation.
	/// </summary>
	LowHumidity = 134,
	/// <summary>
	/// The vendor reports high humidity. This is cloud history, not independent physical confirmation.
	/// </summary>
	HighHumidity = 135,
	/// <summary>
	/// The vendor reports water shortage. This is cloud history, not independent physical confirmation.
	/// </summary>
	WaterShortage = 136,
	/// <summary>
	/// The vendor reports excessive carbon dioxide. This is cloud history, not independent physical confirmation.
	/// </summary>
	ExcessiveCarbonDioxide = 137,
	/// <summary>
	/// The vendor reports excessive power. This is cloud history, not independent physical confirmation.
	/// </summary>
	ExcessivePower = 139,
	/// <summary>
	/// The vendor reports excessive cost. This is cloud history, not independent physical confirmation.
	/// </summary>
	ExcessiveCost = 140,
	/// <summary>
	/// The vendor reports valve failure. This is cloud history, not independent physical confirmation.
	/// </summary>
	ValveFailure = 141,
	/// <summary>
	/// The vendor reports low battery. This is cloud history, not independent physical confirmation.
	/// </summary>
	LowBattery = 143,
	/// <summary>
	/// The vendor reports blockage. This is cloud history, not independent physical confirmation.
	/// </summary>
	Blockage = 146,
	/// <summary>
	/// The vendor reports fan abnormal. This is cloud history, not independent physical confirmation.
	/// </summary>
	FanAbnormal = 147,
	/// <summary>
	/// The vendor reports lamp failure. This is cloud history, not independent physical confirmation.
	/// </summary>
	LampFailure = 148,
	/// <summary>
	/// The vendor reports low voltage. This is cloud history, not independent physical confirmation.
	/// </summary>
	LowVoltage = 149,
	/// <summary>
	/// The vendor reports high voltage. This is cloud history, not independent physical confirmation.
	/// </summary>
	HighVoltage = 150,
	/// <summary>
	/// The vendor reports abnormal protection. This is cloud history, not independent physical confirmation.
	/// </summary>
	AbnormalProtection = 155,
	/// <summary>
	/// The vendor reports city freeze. This is cloud history, not independent physical confirmation.
	/// </summary>
	CityFreeze = 158,
	/// <summary>
	/// The vendor reports battery fault. This is cloud history, not independent physical confirmation.
	/// </summary>
	BatteryFault = 159
	}

/// <summary>A cloud event. Codes with unrecognized meanings remain available without exposing protocol payloads.</summary>
public sealed class RainPointEvent
	{
	/// <summary>
	/// Initializes event from the supplied typed values.
	/// </summary>
	internal RainPointEvent ()
		{
		}
	/// <summary>
	/// Gets the cloud event ID used to deduplicate overlapping history pages.
	/// </summary>
	public string Id { get; internal set; } = string.Empty;
	/// <summary>
	/// Gets the cloud hub identifier associated with the event.
	/// </summary>
	public long HubId
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the reported child RF address; hub-wide events may use zero.
	/// </summary>
	public int Address
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the reported zone number; zero denotes a timer-wide event.
	/// </summary>
	public int Zone
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the original vendor event code, including unrecognized codes.
	/// </summary>
	public int Code
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the recognized event kind, or Unknown without discarding the original code.
	/// </summary>
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
	/// <summary>
	/// Gets the device-reported time-zone label, or null when unavailable.
	/// </summary>
	public string? ReportedTimeZone
		{
		get; internal set;
		}
	/// <summary>
	/// Gets decoded usage in litres, or null when that detail is absent or invalid.
	/// </summary>
	public decimal? WaterUsedLitres
		{
		get; internal set;
		}
	/// <summary>
	/// Gets decoded watering duration, or null when that detail is absent or invalid.
	/// </summary>
	public TimeSpan? Duration
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the vendor watering-mode detail, or null when absent or invalid.
	/// </summary>
	public int? WorkModeCode
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the vendor control-source detail, or null when absent or invalid.
	/// </summary>
	public int? ControlModeCode
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the vendor exception detail, or null when absent or invalid.
	/// </summary>
	public int? ExceptionCode
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the event's reported connectivity state, or null when unavailable.
	/// </summary>
	public bool? IsOnline
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the operator description supplied in event details, or null when absent.
	/// </summary>
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

/// <summary>
/// Contains one bounded history response and continuation hints, without automatic paging.
/// </summary>
public sealed class RainPointEventPage
	{
	/// <summary>
	/// Initializes event page from the supplied typed values.
	/// </summary>
	/// <param name="events">The decoded events returned by this page.</param>
	/// <param name="limitReached">Whether the response filled the requested limit; this does not guarantee another page exists.</param>
	internal RainPointEventPage (IReadOnlyList<RainPointEvent> events, bool limitReached)
		{
		Events = events;
		IsLimitReached = limitReached;
		}
	/// <summary>
	/// Gets the events in this response; no automatic paging or deduplication is performed.
	/// </summary>
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
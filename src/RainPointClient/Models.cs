// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RainPointClient;

/// <summary>A home shared with the authenticated account.</summary>
public sealed class RainPointHome
	{
	[JsonPropertyName ("hid"), JsonRequired, JsonInclude]
	public long Id
		{
		get; internal set;
		}

	[JsonPropertyName ("homeName"), JsonInclude]
	public string Name { get; internal set; } = string.Empty;
	}

/// <summary>A cloud gateway and its RF children. Discover again after pairing changes.</summary>
public sealed class RainPointHub
	{
	[JsonPropertyName ("function"), JsonInclude]
	internal string? SceneFunctionParameter
		{
		get; set;
		}
	[JsonPropertyName ("supportSmart"), JsonInclude]
	internal int? AdvertisedSceneFlags
		{
		get; set;
		}
	[JsonPropertyName ("modelCode"), JsonInclude]
	public int? ModelCode
		{
		get; internal set;
		}
	[JsonIgnore] public bool? SupportsSceneExecution => SceneFunction.Supports (SceneFunctionParameter, AdvertisedSceneFlags, 1);

	[JsonIgnore]
	public long HomeId
		{
		get; internal set;
		}

	[JsonPropertyName ("softVer"), JsonInclude]
	public string? FirmwareVersion
		{
		get; internal set;
		}

	[JsonPropertyName ("mac"), JsonInclude]
	public string? MacAddress
		{
		get; internal set;
		}

	[JsonPropertyName ("param"), JsonInclude]
	internal string? Parameter
		{
		get; set;
		}

	[JsonIgnore]
	public bool? AutomaticTimeBroadcastEnabled => Protocol.HubSettings.ReadBroadcast (Parameter);
	[JsonPropertyName ("recich"), JsonInclude]
	internal int? ReportedRfChannel
		{
		get; set;
		}
	internal int RfChannelWriteAttempted;
	/// <summary>Reported RF receive channel. Writes support channels 1..3 for supported hubs.</summary>
	[JsonIgnore] public int? RfChannel => ReportedRfChannel > 0 ? ReportedRfChannel : null;

	[JsonPropertyName ("mid"), JsonRequired, JsonInclude]
	public long Id
		{
		get; internal set;
		}

	[JsonPropertyName ("name"), JsonInclude]
	public string Name { get; internal set; } = string.Empty;

	[JsonPropertyName ("model"), JsonInclude]
	public string Model { get; internal set; } = string.Empty;

	[JsonPropertyName ("deviceName"), JsonRequired, JsonInclude]
	public string DeviceName { get; internal set; } = string.Empty;

	[JsonPropertyName ("productKey"), JsonRequired, JsonInclude]
	public string ProductKey { get; internal set; } = string.Empty;

	[JsonPropertyName ("subDevices"), JsonInclude]
	public IReadOnlyList<RainPointDevice> Devices { get; internal set; } = [];
	}

/// <summary>A paired device. Address is the RF address, not the cloud database ID.</summary>
public sealed class RainPointDevice
	{
	[JsonPropertyName ("function"), JsonInclude]
	internal string? SceneFunctionParameter
		{
		get; set;
		}
	[JsonPropertyName ("supportSmart"), JsonInclude]
	internal int? AdvertisedSceneFlags
		{
		get; set;
		}
	[JsonIgnore] public bool? SupportsSceneActions => SceneFunction.Supports (SceneFunctionParameter, AdvertisedSceneFlags, 2);

	[JsonPropertyName ("style"), JsonInclude]
	internal string? Style
		{
		get; set;
		}
	[JsonPropertyName ("param"), JsonInclude]
	internal string? Parameter
		{
		get; set;
		}

	[JsonPropertyName ("portNumber"), JsonInclude]
	internal int? PortNumber
		{
		get; set;
		}

	/// <summary>Cloud sub-device identifier, distinct from the RF address.</summary>
	[JsonPropertyName ("sid"), JsonInclude]
	public long? Id
		{
		get; internal set;
		}

	[JsonPropertyName ("softVer"), JsonInclude]
	public string? FirmwareVersion
		{
		get; internal set;
		}

	[JsonPropertyName ("addr"), JsonRequired, JsonInclude]
	public int Address
		{
		get; internal set;
		}

	[JsonPropertyName ("name"), JsonInclude]
	public string Name { get; internal set; } = string.Empty;

	[JsonPropertyName ("model"), JsonRequired, JsonInclude]
	public string Model { get; internal set; } = string.Empty;

	[JsonPropertyName ("modelCode"), JsonInclude]
	public int? ModelCode
		{
		get; internal set;
		}

	[JsonPropertyName ("planJson"), JsonInclude]
	internal string? ProfileParameter
		{
		get; set;
		}

	/// <summary>Whether discovery identifies the supported timer with firmware 120 or newer for manual misting/cycle commands.</summary>
	[JsonIgnore]
	public bool SupportsManualCycles => SupportedZoneCount.HasValue
		 && int.TryParse (FirmwareVersion, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
			  out int version) && version >= 120;

	[JsonIgnore]
	public int? SupportedZoneCount => string.Equals (Model, "HTV345FRF", StringComparison.OrdinalIgnoreCase) ? 3 : null;
	}

public enum TimerReadingAvailability
	{
	NotReported,
	Decoded,
	UnsupportedFormat,
	Malformed
	}

/// <summary>A snapshot from the cloud; absent readings are unknown, never assumed closed.</summary>
public sealed class RainPointTimerStatus
	{
	internal RainPointTimerStatus (int address, TimerReadingAvailability availability,
		 IReadOnlyList<RainPointZoneStatus> zones, int? signalStrength = null, byte? batteryFlag = null,
		 DateTimeOffset? lastDataChange = null, DateTime? reportedAtLocal = null)
		{
		Address = address;
		Availability = availability;
		Zones = zones;
		SignalStrengthDbm = signalStrength;
		BatteryConditionCode = batteryFlag;
		LastDataChange = lastDataChange;
		ReportedAtLocal = reportedAtLocal;
		}

	public int Address
		{
		get;
		}
	public TimerReadingAvailability Availability
		{
		get;
		}
	public IReadOnlyList<RainPointZoneStatus> Zones
		{
		get;
		}
	public int? SignalStrengthDbm
		{
		get;
		}

	/// <summary>Protocol condition code; this is not a percentage of remaining charge.</summary>
	public byte? BatteryConditionCode
		{
		get;
		}

	/// <summary>Upstream condition mapping: 1 is normal, 2 is low; other codes are unknown.</summary>
	/// <remarks>Based on related HTV hardware; not a measured charge percentage.</remarks>
	public bool? IsBatteryLow => BatteryConditionCode switch { 1 => false, 2 => true, _ => null };

	/// <summary>Device-reported local wall-clock time, with unspecified timezone; not receipt time.</summary>
	public DateTime? ReportedAtLocal
		{
		get;
		}

	/// <summary>Cloud data-change time, which is not necessarily a last-seen heartbeat.</summary>
	public DateTimeOffset? LastDataChange
		{
		get;
		}
	}

/// <summary>Reported irrigation program state, including the cycle-and-soak pause. This is not independent physical valve feedback.</summary>
public enum RainPointWateringMode
	{
	Idle = 0,
	Normal = 1,
	Misting = 2,
	CycleAndSoak = 3,
	/// <summary>The cycle-and-soak program is still active, but reports its soaking pause.</summary>
	CycleAndSoakPause = 7
	}

public sealed class RainPointZoneStatus
	{
	internal RainPointZoneStatus (int zone, bool? isOpen, uint? durationSeconds, uint? usageCounts,
		 DateTime? eventTimeLocal = null, byte? alarmCode = null, byte? workModeCode = null)
		{
		Zone = zone;
		IsOpen = isOpen;
		ConfiguredRunDuration = durationSeconds.HasValue ? TimeSpan.FromSeconds (durationSeconds.Value) : null;
		LastWaterUsageCounts = usageCounts;
		EventTimeLocal = eventTimeLocal;
		AlarmCode = alarmCode;
		WorkModeCode = workModeCode;
		}

	public int Zone
		{
		get;
		}
	/// <summary>Reported active irrigation mode: true for normal/misting/cycle (including its soaking pause), false for idle, null for unknown.</summary>
	/// <remarks>This does not indicate physical valve position during cyclic pauses and may lag completion. Interpret alongside LastDataChange and WorkMode.</remarks>
	public bool? IsOpen
		{
		get;
		}

	/// <summary>Reported low-nibble work-mode code; unknown values are retained without guessing active state.</summary>
	public byte? WorkModeCode
		{
		get;
		}
	public RainPointWateringMode? WorkMode => WorkModeCode is (>= 0 and <= 3) or 7 ? (RainPointWateringMode)WorkModeCode.Value : null;

	/// <summary>Commanded run length retained by the timer; not a remaining-time countdown.</summary>
	public TimeSpan? ConfiguredRunDuration
		{
		get;
		}

	/// <summary>Underlying last-usage count, retained for protocol diagnostics.</summary>
	public uint? LastWaterUsageCounts
		{
		get;
		}

	/// <summary>Last reported water usage in litres for the supported HTV345FRF timer.</summary>
	/// <remarks>
	/// Uses 0.1 litre per count, supported by an owner app comparison of 14 counts with 1.4 litres.
	/// This is a last-usage reading, not a flow rate or cumulative meter; it may lag or reset during watering.
	/// Missing usage remains unknown. The conversion has not been validated for other timer models.
	/// </remarks>
	public decimal? LastWaterUsageLitres => LastWaterUsageCounts / 10m;

	/// <summary>Device event wall-clock time with unspecified timezone; null when absent, zero or invalid.</summary>
	/// <remarks>Related HTV captures identify it as the run end time. It is not physical stop confirmation.</remarks>
	public DateTime? EventTimeLocal
		{
		get;
		}

	/// <summary>Reported alarm bits. Bits 0..2 mean leak, water shortage and freeze in the vendor app; other bits are retained.</summary>
	public byte? AlarmCode
		{
		get;
		}
	/// <summary>Reported leak flag; null when the alarm reading is absent.</summary>
	public bool? WaterLeakReported => AlarmCode.HasValue ? (AlarmCode.Value & 1) != 0 : null;
	/// <summary>Reported water-shortage flag; not an independently measured flow condition.</summary>
	public bool? WaterShortageReported => AlarmCode.HasValue ? (AlarmCode.Value & 2) != 0 : null;
	/// <summary>Reported freeze flag. An absent alarm is not interpreted as frost protection.</summary>
	public bool? FreezeReported => AlarmCode.HasValue ? (AlarmCode.Value & 4) != 0 : null;
	/// <summary>Uninterpreted bits, or null if no alarm was reported.</summary>
	public byte? UnknownAlarmBits => AlarmCode.HasValue ? (byte)(AlarmCode.Value & ~7) : null;
	}

/// <summary>Cloud acknowledgement only. Polled status may also lag physical valve state.</summary>
public enum RainPointCommandOutcome
	{
	Accepted,
	AlreadyInRequestedStateOrTransitioning
	}
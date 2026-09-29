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
	/// <summary>
	/// Gets the cloud home identifier used for subsequent discovery and history requests.
	/// </summary>
	[JsonPropertyName ("hid"), JsonRequired, JsonInclude]
	public long Id
		{
		get; internal set;
		}

	/// <summary>
	/// Gets the assigned home name; an omitted wire name remains an empty string.
	/// </summary>
	[JsonPropertyName ("homeName"), JsonInclude]
	public string Name { get; internal set; } = string.Empty;
	}

/// <summary>A cloud gateway and its RF children. Discover again after pairing changes.</summary>
public sealed class RainPointHub
	{
	/// <summary>
	/// Stores the encoded scene-capability field for explicit capability decoding.
	/// </summary>
	[JsonPropertyName ("function"), JsonInclude]
	internal string? SceneFunctionParameter
		{
		get; set;
		}
	/// <summary>
	/// Stores optional catalog scene-capability flags.
	/// </summary>
	[JsonPropertyName ("supportSmart"), JsonInclude]
	internal int? AdvertisedSceneFlags
		{
		get; set;
		}
	/// <summary>
	/// Gets the vendor model code, or null when not reported.
	/// </summary>
	[JsonPropertyName ("modelCode"), JsonInclude]
	public int? ModelCode
		{
		get; internal set;
		}
	/// <summary>
	/// Gets advertised scene-execution capability, or null when the capability metadata is unknown.
	/// </summary>
	[JsonIgnore] public bool? SupportsSceneExecution => SceneFunction.Supports (SceneFunctionParameter, AdvertisedSceneFlags, 1);

	/// <summary>
	/// Gets the owning home ID assigned during discovery.
	/// </summary>
	[JsonIgnore]
	public long HomeId
		{
		get; internal set;
		}

	/// <summary>
	/// Gets the reported firmware version string, or null when absent.
	/// </summary>
	[JsonPropertyName ("softVer"), JsonInclude]
	public string? FirmwareVersion
		{
		get; internal set;
		}

	/// <summary>
	/// Gets the reported MAC address, or null when absent.
	/// </summary>
	[JsonPropertyName ("mac"), JsonInclude]
	public string? MacAddress
		{
		get; internal set;
		}

	/// <summary>
	/// Stores the original encoded configuration field for bounded decoding and guarded updates.
	/// </summary>
	[JsonPropertyName ("param"), JsonInclude]
	internal string? Parameter
		{
		get; set;
		}

	/// <summary>
	/// Gets the decoded automatic RF time-broadcast setting, or null when unavailable or unsupported.
	/// </summary>
	[JsonIgnore]
	public bool? AutomaticTimeBroadcastEnabled => Protocol.HubSettings.ReadBroadcast (Parameter);
	/// <summary>
	/// Stores the reported RF receive-channel number before range interpretation.
	/// </summary>
	[JsonPropertyName ("recich"), JsonInclude]
	internal int? ReportedRfChannel
		{
		get; set;
		}
	/// <summary>
	/// Tracks whether this hub observation has already been used for an RF-channel write attempt.
	/// </summary>
	internal int RfChannelWriteAttempted;
	/// <summary>Reported RF receive channel. Writes support channels 1..3 for supported hubs.</summary>
	[JsonIgnore] public int? RfChannel => ReportedRfChannel > 0 ? ReportedRfChannel : null;

	/// <summary>
	/// Gets the cloud hub identifier, distinct from a child's RF address.
	/// </summary>
	[JsonPropertyName ("mid"), JsonRequired, JsonInclude]
	public long Id
		{
		get; internal set;
		}

	/// <summary>
	/// Gets the assigned hub name; an omitted wire name remains an empty string.
	/// </summary>
	[JsonPropertyName ("name"), JsonInclude]
	public string Name { get; internal set; } = string.Empty;

	/// <summary>
	/// Gets the hub model identifier reported by the cloud.
	/// </summary>
	[JsonPropertyName ("model"), JsonInclude]
	public string Model { get; internal set; } = string.Empty;

	/// <summary>
	/// Gets the vendor IoT device identity used to address hub operations.
	/// </summary>
	[JsonPropertyName ("deviceName"), JsonRequired, JsonInclude]
	public string DeviceName { get; internal set; } = string.Empty;

	/// <summary>
	/// Gets the vendor IoT product identity used with the device name.
	/// </summary>
	[JsonPropertyName ("productKey"), JsonRequired, JsonInclude]
	public string ProductKey { get; internal set; } = string.Empty;

	/// <summary>
	/// Gets the paired RF children reported by discovery; refresh discovery after pairing changes.
	/// </summary>
	[JsonPropertyName ("subDevices"), JsonInclude]
	public IReadOnlyList<RainPointDevice> Devices { get; internal set; } = [];
	}

/// <summary>A paired device. Address is the RF address, not the cloud database ID.</summary>
public sealed class RainPointDevice
	{
	/// <summary>
	/// Stores the encoded scene-capability field for explicit capability decoding.
	/// </summary>
	[JsonPropertyName ("function"), JsonInclude]
	internal string? SceneFunctionParameter
		{
		get; set;
		}
	/// <summary>
	/// Stores optional catalog scene-capability flags.
	/// </summary>
	[JsonPropertyName ("supportSmart"), JsonInclude]
	internal int? AdvertisedSceneFlags
		{
		get; set;
		}
	/// <summary>
	/// Gets advertised scene-action capability, or null when capability metadata is unknown.
	/// </summary>
	[JsonIgnore] public bool? SupportsSceneActions => SceneFunction.Supports (SceneFunctionParameter, AdvertisedSceneFlags, 2);

	/// <summary>
	/// Stores the vendor zone-name field used to derive assigned zone names.
	/// </summary>
	[JsonPropertyName ("portDescribe"), JsonInclude]
	internal string? PortDescriptions
		{
		get; set;
		}

	/// <summary>Assigned names in zone order; an empty entry means no assigned name. Unknown timer models return an empty list.</summary>
	[JsonIgnore]
	public IReadOnlyList<string> ZoneNames
		{
		get
			{
			if (SupportedZoneCount is not { } count)
				return Array.Empty<string> ();
			string[] parts = (PortDescriptions ?? string.Empty).Split ('|');
			string[] names = new string[count];
			for (int i = 0; i < count; i++)
				names[i] = i < parts.Length ? parts[i] : string.Empty;
			return Array.AsReadOnly (names);
			}
		}

	/// <summary>
	/// Stores the vendor style field used when preserving device configuration.
	/// </summary>
	[JsonPropertyName ("style"), JsonInclude]
	internal string? Style
		{
		get; set;
		}
	/// <summary>
	/// Stores the original encoded configuration field for bounded decoding and guarded updates.
	/// </summary>
	[JsonPropertyName ("param"), JsonInclude]
	internal string? Parameter
		{
		get; set;
		}

	/// <summary>
	/// Stores the reported port count used to validate configuration section boundaries.
	/// </summary>
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

	/// <summary>
	/// Gets the reported firmware version string, or null when absent.
	/// </summary>
	[JsonPropertyName ("softVer"), JsonInclude]
	public string? FirmwareVersion
		{
		get; internal set;
		}

	/// <summary>
	/// Gets the child device's RF address within its hub, distinct from its cloud ID.
	/// </summary>
	[JsonPropertyName ("addr"), JsonRequired, JsonInclude]
	public int Address
		{
		get; internal set;
		}

	/// <summary>
	/// Gets the child's assigned display name; an omitted wire name remains an empty string.
	/// </summary>
	[JsonPropertyName ("name"), JsonInclude]
	public string Name { get; internal set; } = string.Empty;

	/// <summary>
	/// Gets the reported child-device model identifier.
	/// </summary>
	[JsonPropertyName ("model"), JsonRequired, JsonInclude]
	public string Model { get; internal set; } = string.Empty;

	/// <summary>
	/// Gets the vendor model code, or null when absent.
	/// </summary>
	[JsonPropertyName ("modelCode"), JsonInclude]
	public int? ModelCode
		{
		get; internal set;
		}

	/// <summary>
	/// Stores the encoded recommendation-profile configuration.
	/// </summary>
	[JsonPropertyName ("planJson"), JsonInclude]
	internal string? ProfileParameter
		{
		get; set;
		}

	/// <summary>Whether discovery identifies the supported timer with firmware 120 or newer for manual misting/cycle commands.</summary>
	[JsonIgnore]
	public bool SupportsManualCycles => SupportedZoneCount == 3
		 && int.TryParse (FirmwareVersion, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
			  out int version) && version >= 120;

	/// <summary>
	/// Gets 1, 2 or 3 for HTV145FRF, HTV245FRF or HTV345FRF respectively, or null for unrecognized models.
	/// </summary>
	[JsonIgnore]
	public int? SupportedZoneCount => Model?.ToUpperInvariant () switch
		{
			"HTV145FRF" => 1,
			"HTV245FRF" => 2,
			"HTV345FRF" => 3,
			_ => null
		};
	}

/// <summary>
/// Distinguishes decoded data from absent, malformed and unsupported representations.
/// </summary>
public enum TimerReadingAvailability
	{
	/// <summary>
	/// No usable field was reported; no state or empty configuration is inferred.
	/// </summary>
	NotReported,
	/// <summary>
	/// The recognized representation was decoded successfully.
	/// </summary>
	Decoded,
	/// <summary>
	/// A reported representation is not supported by this decoder.
	/// </summary>
	UnsupportedFormat,
	/// <summary>
	/// The expected representation was present but malformed.
	/// </summary>
	Malformed
	}

/// <summary>A snapshot from the cloud; absent readings are unknown, never assumed closed.</summary>
public sealed class RainPointTimerStatus
	{
	/// <summary>
	/// Initializes timer status from the supplied typed values.
	/// </summary>
	/// <param name="address">The paired child's RF address within its hub, distinct from its cloud database ID.</param>
	/// <param name="availability">The decoding result; unavailable data must not be interpreted as an empty configuration or closed valve.</param>
	/// <param name="zones">The decoded per-zone observations.</param>
	/// <param name="signalStrength">Reported timer RF signal strength in dBm, or null when unavailable.</param>
	/// <param name="batteryFlag">The vendor battery-condition code, not a charge percentage; null means absent.</param>
	/// <param name="lastDataChange">The timer's cloud data-change instant, or null when absent.</param>
	/// <param name="reportedAtLocal">The device-reported wall-clock time with Unspecified kind, or null when unavailable.</param>
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

	/// <summary>
	/// Gets the timer's RF address within the hub.
	/// </summary>
	public int Address
		{
		get;
		}
	/// <summary>
	/// Gets whether the timer status was decoded, absent, unsupported or malformed.
	/// </summary>
	public TimerReadingAvailability Availability
		{
		get;
		}
	/// <summary>
	/// Gets decoded zone states; consult availability before interpreting an empty list.
	/// </summary>
	public IReadOnlyList<RainPointZoneStatus> Zones
		{
		get;
		}
	/// <summary>
	/// Gets reported timer-to-hub RF signal strength in dBm, or null when unavailable.
	/// </summary>
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
	/// <summary>
	/// The reported irrigation program is idle.
	/// </summary>
	Idle = 0,
	/// <summary>
	/// Normal timed irrigation is reported active.
	/// </summary>
	Normal = 1,
	/// <summary>
	/// A misting program is reported active.
	/// </summary>
	Misting = 2,
	/// <summary>
	/// The watering phase of a cycle-and-soak program is reported active.
	/// </summary>
	CycleAndSoak = 3,
	/// <summary>The cycle-and-soak program is still active, but reports its soaking pause.</summary>
	CycleAndSoakPause = 7
	}

/// <summary>
/// Contains reported state for one irrigation zone; missing values remain unknown.
/// </summary>
public sealed class RainPointZoneStatus
	{
	/// <summary>
	/// Initializes zone status from the supplied typed values.
	/// </summary>
	/// <param name="zone">The one-based zone number on the selected timer.</param>
	/// <param name="isOpen">Reported irrigation activity, including active cyclic pauses, or null when unknown.</param>
	/// <param name="durationSeconds">The reported configured run duration in seconds, or null when absent.</param>
	/// <param name="usageCounts">The underlying last-usage counter, or null when absent; recognized timers use 0.1 litre per count.</param>
	/// <param name="eventTimeLocal">The reported device event wall time, or null when unavailable.</param>
	/// <param name="alarmCode">Reported alarm bits, or null when absent; unknown bits are retained.</param>
	/// <param name="workModeCode">The reported work-mode code, or null when absent.</param>
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

	/// <summary>
	/// Gets the one-based irrigation zone number.
	/// </summary>
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
	/// <summary>
	/// Gets a recognized reported watering mode, or null for an absent or unknown mode code.
	/// </summary>
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

	/// <summary>Last reported water usage in litres for recognized RF timers.</summary>
	/// <remarks>
	/// Uses 0.1 litre per count, supported by an owner app comparison of 14 counts with 1.4 litres.
	/// This is a last-usage reading, not a flow rate or cumulative meter; it may lag or reset during watering.
	/// Missing usage remains unknown. The added one- and two-zone models use the reference conversion without project hardware validation.
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
	/// <summary>
	/// The cloud accepted the request; physical valve actuation is not confirmed.
	/// </summary>
	Accepted,
	/// <summary>
	/// The service reports the requested state or a transition already in progress.
	/// </summary>
	AlreadyInRequestedStateOrTransitioning
	}
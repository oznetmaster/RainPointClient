using System;
using System.Collections.Generic;

namespace RainPointClient;

/// <summary>Last cloud-reported hub readings, not a direct LAN reachability check.</summary>
public sealed class RainPointHubStatus
	{
	internal RainPointHubStatus (long hubId, bool? isConnected, int? signal, DateTimeOffset? connectionChanged,
		 IReadOnlyList<RainPointTimerStatus> timers)
		{
		HubId = hubId;
		IsConnected = isConnected;
		WifiSignalStrengthDbm = signal;
		LastConnectionChange = connectionChanged;
		Timers = timers;
		}

	public long HubId
		{
		get;
		}
	public bool? IsConnected
		{
		get;
		}
	public int? WifiSignalStrengthDbm
		{
		get;
		}
	/// <summary>Time the cloud connection record changed; not a heartbeat or status-receipt time.</summary>
	public DateTimeOffset? LastConnectionChange
		{
		get;
		}
	public IReadOnlyList<RainPointTimerStatus> Timers
		{
		get;
		}
	}
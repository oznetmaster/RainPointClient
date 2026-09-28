// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

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
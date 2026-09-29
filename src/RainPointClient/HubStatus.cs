// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;

namespace RainPointClient;

/// <summary>Last cloud-reported hub readings, not a direct LAN reachability check.</summary>
public sealed class RainPointHubStatus
	{
	/// <summary>
	/// Initializes hub status from the supplied typed values.
	/// </summary>
	/// <param name="hubId">The positive cloud hub identifier, distinct from child RF addresses.</param>
	/// <param name="isConnected">Cloud-reported hub connectivity, or null when unavailable.</param>
	/// <param name="signal">Reported hub Wi-Fi signal strength in dBm, or null when unavailable.</param>
	/// <param name="connectionChanged">The cloud-reported connection-change instant, or null when absent.</param>
	/// <param name="timers">The timer observations included in this update.</param>
	internal RainPointHubStatus (long hubId, bool? isConnected, int? signal, DateTimeOffset? connectionChanged,
		 IReadOnlyList<RainPointTimerStatus> timers)
		{
		HubId = hubId;
		IsConnected = isConnected;
		WifiSignalStrengthDbm = signal;
		LastConnectionChange = connectionChanged;
		Timers = timers;
		}

	/// <summary>
	/// Gets the cloud hub ID to which this snapshot belongs.
	/// </summary>
	public long HubId
		{
		get;
		}
	/// <summary>
	/// Gets cloud-reported hub connectivity, or null when unavailable; this is not a LAN probe.
	/// </summary>
	public bool? IsConnected
		{
		get;
		}
	/// <summary>
	/// Gets reported hub Wi-Fi signal strength in dBm, or null when unavailable; this is separate from timer RF strength.
	/// </summary>
	public int? WifiSignalStrengthDbm
		{
		get;
		}
	/// <summary>Time the cloud connection record changed; not a heartbeat or status-receipt time.</summary>
	public DateTimeOffset? LastConnectionChange
		{
		get;
		}
	/// <summary>
	/// Gets the timer status snapshots included in this cloud response.
	/// </summary>
	public IReadOnlyList<RainPointTimerStatus> Timers
		{
		get;
		}
	}
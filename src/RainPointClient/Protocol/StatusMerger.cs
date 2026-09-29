// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Linq;

namespace RainPointClient.Protocol;

/// <summary>
/// Internal status merger representation or processing contract for the RainPoint protocol.
/// </summary>
/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
internal sealed class StatusMerger (RainPointHub hub)
	{
	private readonly Dictionary<int, RainPointTimerObservation> _timers = [];
	private bool? _connected;
	private int? _wifi;
	private DateTimeOffset? _connectionChanged;
	private DateTimeOffset? _lastPoll;
	private long _revision;

	/// <summary>
	/// Merges a REST snapshot without replacing newer accepted timer feedback with older data.
	/// </summary>
	/// <param name="status">The accepted typed hub or timer observation.</param>
	/// <param name="received">The local receipt instant of this accepted observation.</param>
	/// <returns>The merged status update with per-timer source and receipt metadata.</returns>
	/// <exception cref="System.ArgumentException">The reading belongs to another hub.</exception>
	internal RainPointStatusUpdate ApplyPoll (RainPointHubStatus status, DateTimeOffset received)
		{
		if (status.HubId != hub.Id)
			throw new ArgumentException ("The reading belongs to another hub.");
		_lastPoll = received;
		_wifi = status.WifiSignalStrengthDbm;
		ApplyConnection (status.IsConnected, status.LastConnectionChange, received);
		foreach (RainPointTimerStatus timer in status.Timers)
			ApplyTimer (timer, RainPointUpdateSource.Poll, received);
		return Snapshot (RainPointUpdateSource.Poll, received);
		}

	/// <summary>
	/// Merges an accepted push only when its scoped feedback can advance the current observation.
	/// </summary>
	/// <param name="reading">The accepted decoded push update to merge.</param>
	/// <param name="received">The local receipt instant of this accepted observation.</param>
	/// <returns>The resulting merged update, or null when nothing was accepted.</returns>
	internal RainPointStatusUpdate? ApplyPush (PushReading reading, DateTimeOffset received)
		{
		bool changed = ApplyConnection (reading.Connected, reading.ConnectionChanged, received);
		foreach (RainPointTimerStatus timer in reading.Timers)
			changed |= ApplyTimer (timer, RainPointUpdateSource.Push, received);
		return changed ? Snapshot (RainPointUpdateSource.Push, received) : null;
		}

	private bool ApplyConnection (bool? connected, DateTimeOffset? timestamp, DateTimeOffset received)
		{
		if (!connected.HasValue || timestamp > received.AddMinutes (5))
			return false;
		if (_connectionChanged.HasValue && (!timestamp.HasValue || timestamp <= _connectionChanged))
			return false;
		_connected = connected;
		_connectionChanged = timestamp;
		return true;
		}

	private bool ApplyTimer (RainPointTimerStatus timer, RainPointUpdateSource source, DateTimeOffset received)
		{
		if (!hub.Devices.Any (item => item.Address == timer.Address && item.SupportedZoneCount.HasValue)
			 || timer.LastDataChange > received.AddMinutes (5))
			return false;
		if (_timers.TryGetValue (timer.Address, out RainPointTimerObservation? previous))
			{
			// A missing, malformed or older poll must not destroy a known, newer reading.
			if (timer.Availability != TimerReadingAvailability.Decoded && previous.Status.Availability == TimerReadingAvailability.Decoded)
				return false;
			if (previous.Status.LastDataChange.HasValue
				 && (!timer.LastDataChange.HasValue || timer.LastDataChange <= previous.Status.LastDataChange))
				return false;
			}
		_timers[timer.Address] = new RainPointTimerObservation (timer, source, received);
		return true;
		}

	private RainPointStatusUpdate Snapshot (RainPointUpdateSource source, DateTimeOffset received)
		{
		RainPointTimerObservation[] timers = _timers.OrderBy (pair => pair.Key).Select (pair => pair.Value).ToArray ();
		RainPointHubStatus status = new (hub.Id, _connected, _wifi, _connectionChanged, Array.AsReadOnly (timers.Select (item => item.Status).ToArray ()));
		return new RainPointStatusUpdate (status, Array.AsReadOnly (timers), source, received, _lastPoll, ++_revision);
		}
	}
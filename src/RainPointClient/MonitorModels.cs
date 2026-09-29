// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;

namespace RainPointClient;

/// <summary>
/// Identifies whether an observation came from a REST read or MQTT push.
/// </summary>
public enum RainPointUpdateSource
	{
	/// <summary>A snapshot obtained by a REST read.</summary>
	Poll,
	/// <summary>An update received through the MQTT observer.</summary>
	Push
	}
/// <summary>
/// Describes the monitor's connection lifecycle, independently of physical valve state.
/// </summary>
public enum RainPointMonitorState
	{
	/// <summary>The monitor is starting its initial discovery and observation.</summary>
	Starting,
	/// <summary>The monitor is using REST reconciliation without a connected push observer.</summary>
	Polling,
	/// <summary>The MQTT observer is connected; freshness remains a separate status.</summary>
	PushConnected,
	/// <summary>The monitor is recovering its connection.</summary>
	Reconnecting,
	/// <summary>Observation requires the caller to restore an authenticated session.</summary>
	AuthenticationRequired,
	/// <summary>The monitor has stopped.</summary>
	Stopped
	}

/// <summary>
/// Carries a monitor lifecycle-state transition.
/// </summary>
/// <param name="state">The new lifecycle state carried by the event.</param>
public sealed class RainPointMonitorStateChangedEventArgs (RainPointMonitorState state) : EventArgs
	{
	/// <summary>
	/// Gets the lifecycle state carried by this event.
	/// </summary>
	public RainPointMonitorState State { get; } = state;
	}

/// <summary>One accepted timer snapshot. Receipt time does not prove physical freshness.</summary>
public sealed class RainPointTimerObservation
	{
	/// <summary>
	/// Initializes timer observation from the supplied typed values.
	/// </summary>
	/// <param name="status">The accepted typed hub or timer observation.</param>
	/// <param name="source">The transport from which the accepted observation originated.</param>
	/// <param name="receivedAt">The local receipt instant, independent of the device's data-change time.</param>
	internal RainPointTimerObservation (RainPointTimerStatus status, RainPointUpdateSource source, DateTimeOffset receivedAt)
		{
		Status = status;
		Source = source;
		ReceivedAt = receivedAt;
		}
	/// <summary>
	/// Gets the accepted timer snapshot.
	/// </summary>
	public RainPointTimerStatus Status
		{
		get;
		}
	/// <summary>
	/// Gets the transport source of this timer's accepted snapshot.
	/// </summary>
	public RainPointUpdateSource Source
		{
		get;
		}
	/// <summary>
	/// Gets the local receipt instant; it does not establish the physical age of the device reading.
	/// </summary>
	public DateTimeOffset ReceivedAt
		{
		get;
		}
	}

/// <summary>A merged snapshot. Each timer retains the source and receipt time of its accepted reading.</summary>
public sealed class RainPointStatusUpdate : EventArgs
	{
	/// <summary>
	/// Initializes status update from the supplied typed values.
	/// </summary>
	/// <param name="status">The accepted typed hub or timer observation.</param>
	/// <param name="timers">The timer observations included in this update.</param>
	/// <param name="source">The transport from which the accepted observation originated.</param>
	/// <param name="receivedAt">The local receipt instant, independent of the device's data-change time.</param>
	/// <param name="lastPoll">The last successful REST-read instant, or null before the first successful read.</param>
	/// <param name="revision">The monotonically increasing accepted-observation or configuration revision.</param>
	internal RainPointStatusUpdate (RainPointHubStatus status, IReadOnlyList<RainPointTimerObservation> timers,
		 RainPointUpdateSource source, DateTimeOffset receivedAt, DateTimeOffset? lastPoll, long revision)
		{
		Status = status;
		Timers = timers;
		Source = source;
		ReceivedAt = receivedAt;
		LastSuccessfulPollAt = lastPoll;
		Revision = revision;
		}
	/// <summary>
	/// Gets the merged hub status for this update.
	/// </summary>
	public RainPointHubStatus Status
		{
		get;
		}
	/// <summary>
	/// Gets timer observations retaining their individual accepted source and receipt times.
	/// </summary>
	public IReadOnlyList<RainPointTimerObservation> Timers
		{
		get;
		}
	/// <summary>
	/// Gets the transport that triggered this merged update.
	/// </summary>
	public RainPointUpdateSource Source
		{
		get;
		}
	/// <summary>
	/// Gets the local receipt time of this update.
	/// </summary>
	public DateTimeOffset ReceivedAt
		{
		get;
		}
	/// <summary>
	/// Gets the most recent successful REST-read instant, or null before any successful REST read.
	/// </summary>
	public DateTimeOffset? LastSuccessfulPollAt
		{
		get;
		}
	/// <summary>
	/// Gets the monotonically increasing revision of accepted updates within this monitor.
	/// </summary>
	public long Revision
		{
		get;
		}
	}

/// <summary>
/// Controls REST reconciliation and MQTT observation for a hub monitor.
/// </summary>
public sealed class RainPointMonitorOptions
	{
	/// <summary>
	/// Gets or sets the REST reconciliation interval, from five seconds to one hour; defaults to thirty seconds.
	/// </summary>
	public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds (30);
	/// <summary>
	/// Gets or sets whether to connect the MQTT observer; defaults to true.
	/// </summary>
	public bool EnablePush { get; set; } = true;
	/// <summary>When false, automatic REST reads run at startup, after each MQTT connection, and while live updates are unavailable. Manual refresh remains available.</summary>
	/// <remarks>The default preserves periodic reconciliation for existing callers. A quiet connected observer need not emit timer changes.</remarks>
	public bool PollWhilePushConnected { get; set; } = true;
	}
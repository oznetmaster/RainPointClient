// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;

namespace RainPointClient;

public enum RainPointUpdateSource
	{
	Poll, Push
	}
public enum RainPointMonitorState
	{
	Starting, Polling, PushConnected, Reconnecting, AuthenticationRequired, Stopped
	}

public sealed class RainPointMonitorStateChangedEventArgs (RainPointMonitorState state) : EventArgs
	{
	public RainPointMonitorState State { get; } = state;
	}

/// <summary>One accepted timer snapshot. Receipt time does not prove physical freshness.</summary>
public sealed class RainPointTimerObservation
	{
	internal RainPointTimerObservation (RainPointTimerStatus status, RainPointUpdateSource source, DateTimeOffset receivedAt)
		{
		Status = status;
		Source = source;
		ReceivedAt = receivedAt;
		}
	public RainPointTimerStatus Status
		{
		get;
		}
	public RainPointUpdateSource Source
		{
		get;
		}
	public DateTimeOffset ReceivedAt
		{
		get;
		}
	}

/// <summary>A merged snapshot. Each timer retains the source and receipt time of its accepted reading.</summary>
public sealed class RainPointStatusUpdate : EventArgs
	{
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
	public RainPointHubStatus Status
		{
		get;
		}
	public IReadOnlyList<RainPointTimerObservation> Timers
		{
		get;
		}
	public RainPointUpdateSource Source
		{
		get;
		}
	public DateTimeOffset ReceivedAt
		{
		get;
		}
	public DateTimeOffset? LastSuccessfulPollAt
		{
		get;
		}
	public long Revision
		{
		get;
		}
	}

public sealed class RainPointMonitorOptions
	{
	public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds (30);
	public bool EnablePush { get; set; } = true;
	}
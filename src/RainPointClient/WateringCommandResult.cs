using System;

namespace RainPointClient;

/// <summary>Cloud acknowledgement and optional reported status; neither confirms physical water flow.</summary>
public sealed class RainPointWateringCommandResult
	{
	internal RainPointWateringCommandResult (RainPointCommandOutcome outcome, int requestedZone,
		 RainPointTimerStatus status, DateTimeOffset? responseTimestamp)
		{
		Outcome = outcome;
		RequestedZone = requestedZone;
		Status = status;
		ResponseTimestamp = responseTimestamp;
		}

	public RainPointCommandOutcome Outcome
		{
		get;
		}
	public int RequestedZone
		{
		get;
		}

	/// <summary>Only fields actually reported in the response are populated. Check Availability first.</summary>
	/// <remarks>This may be partial or stale. It is not automatically merged into monitored status.</remarks>
	public RainPointTimerStatus Status
		{
		get;
		}

	/// <summary>Optional server response timestamp, separate from receipt time and device data-change time.</summary>
	/// <remarks>Absent, invalid and out-of-range timestamps remain unknown.</remarks>
	public DateTimeOffset? ResponseTimestamp
		{
		get;
		}
	}
// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;

namespace RainPointClient;

/// <summary>Cloud acknowledgement and optional reported status; neither confirms physical water flow.</summary>
public sealed class RainPointWateringCommandResult
	{
	/// <summary>
	/// Initializes watering command result from the supplied typed values.
	/// </summary>
	/// <param name="outcome">The cloud command acknowledgement classification.</param>
	/// <param name="requestedZone">The one-based zone addressed by the command.</param>
	/// <param name="status">The accepted typed hub or timer observation.</param>
	/// <param name="responseTimestamp">The command response's cloud timestamp, or null when absent or invalid.</param>
	internal RainPointWateringCommandResult (RainPointCommandOutcome outcome, int requestedZone,
		 RainPointTimerStatus status, DateTimeOffset? responseTimestamp)
		{
		Outcome = outcome;
		RequestedZone = requestedZone;
		Status = status;
		ResponseTimestamp = responseTimestamp;
		}

	/// <summary>
	/// Gets the cloud acknowledgement classification, not physical confirmation.
	/// </summary>
	public RainPointCommandOutcome Outcome
		{
		get;
		}
	/// <summary>
	/// Gets the one-based zone addressed by the command.
	/// </summary>
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
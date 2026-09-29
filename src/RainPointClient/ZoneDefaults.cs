// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Threading;
using System.Threading.Tasks;

using RainPointClient.Protocol;

namespace RainPointClient;

/// <summary>Saved default values used by the app's manual-watering forms. These do not start watering or alter saved schedules.</summary>
public sealed class RainPointZoneDefaults
	{
	/// <summary>
	/// Initializes zone defaults from the supplied typed values.
	/// </summary>
	/// <param name="duration">The configured watering duration; use the operation's documented range and mode-specific treatment of pauses.</param>
	/// <param name="run">The decoded default burst duration, or null for the app-default sentinel.</param>
	/// <param name="interval">The saved misting pause interval, or null for the app-default sentinel.</param>
	internal RainPointZoneDefaults (TimeSpan? duration, TimeSpan? run, TimeSpan? interval)
		{
		WateringDuration = duration;
		MistingRunTime = run;
		MistingInterval = interval;
		}
	/// <summary>Saved duration; null means the encoded zero sentinel. The inspected app displays ten minutes for that sentinel.</summary>
	public TimeSpan? WateringDuration
		{
		get;
		}
	/// <summary>Saved misting on-time; null means the encoded zero sentinel, displayed by the app as ten seconds.</summary>
	public TimeSpan? MistingRunTime
		{
		get;
		}
	/// <summary>Saved misting off-time; null means the encoded zero sentinel, displayed by the app as thirty seconds.</summary>
	public TimeSpan? MistingInterval
		{
		get;
		}
	}

public sealed partial class RainPointCloudClient
	{
	/// <summary>Sets the saved default watering duration, 1..720 whole minutes. Null restores the app-default sentinel. Sends no valve command.</summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="expected">An unused, current schedule snapshot observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="duration">The configured watering duration; use the operation's documented range and mode-specific treatment of pauses.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task SetTimerDefaultWateringDurationAsync (RainPointHub hub, RainPointScheduleSnapshot expected, TimeSpan? duration,
	 CancellationToken cancellationToken = default) =>
	 WriteTimerParameterAsync (hub, expected, TimerZoneDefaults.EditDuration (expected, duration), cancellationToken);

	/// <summary>Sets saved misting on/off defaults, each 5..3600 whole seconds. A null value restores that field's app-default sentinel. Does not start misting.</summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="expected">An unused, current schedule snapshot observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="runTime">The saved misting burst duration, from 5 through 3600 whole seconds, or null for the app-default sentinel.</param>
	/// <param name="interval">The saved misting pause interval, or null for the app-default sentinel.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task SetTimerMistingDefaultsAsync (RainPointHub hub, RainPointScheduleSnapshot expected, TimeSpan? runTime, TimeSpan? interval,
	 CancellationToken cancellationToken = default) =>
	 WriteTimerParameterAsync (hub, expected, TimerZoneDefaults.EditMisting (expected, runTime, interval), cancellationToken);
	}
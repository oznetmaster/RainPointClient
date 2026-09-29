// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System.Threading;
using System.Threading.Tasks;

using RainPointClient.Protocol;

namespace RainPointClient;

public sealed partial class RainPointCloudClient
	{
	/// <summary>Sets one zone's signed flow-calibration percentage (-20..20), preserving every other configuration byte. Sends no valve command.</summary>
	/// <remarks>Use a fresh snapshot for each write. The client does not apply this correction again to reported litres. Cloud acceptance does not prove physical measurement accuracy.</remarks>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="expected">An unused, current schedule snapshot observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="percentage">The signed flow-calibration correction in percent, from -20 through 20.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task SetTimerFlowCalibrationAsync (RainPointHub hub, RainPointScheduleSnapshot expected, int percentage,
	 CancellationToken cancellationToken = default) =>
	 WriteTimerParameterAsync (hub, expected, TimerFlowCalibration.Edit (expected, percentage), cancellationToken);
	}
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
	public Task SetTimerFlowCalibrationAsync (RainPointHub hub, RainPointScheduleSnapshot expected, int percentage,
	 CancellationToken cancellationToken = default) =>
	 WriteTimerParameterAsync (hub, expected, TimerFlowCalibration.Edit (expected, percentage), cancellationToken);
	}
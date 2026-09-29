// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace RainPointClient;

/// <summary>Automatic watering below a soil-moisture threshold. Enabling can cause future watering without a manual start command.</summary>
public sealed class RainPointMoistureWateringRule
	{
	/// <summary>
	/// Gets or sets whether the automatic low-moisture rule is enabled; enabling can authorize future watering.
	/// </summary>
	public bool Enabled
		{
		get; set;
		}
	/// <summary>
	/// Gets or sets the soil-moisture threshold from 1 to 99 percent below which the associated sensor can trigger watering.
	/// </summary>
	public int StartBelowMoisturePercent { get; set; } = 30;
	/// <summary>Normal irrigation or misting only. Misting uses the zone's saved burst/pause defaults.</summary>
	public RainPointScheduleMode Mode { get; set; } = RainPointScheduleMode.Irrigation;
	/// <summary>1..30 whole minutes, or null for volume-only operation.</summary>
	public TimeSpan? Duration { get; set; } = TimeSpan.FromMinutes (10);
	/// <summary>Optional 0.3..6000 litres in 0.1 litre steps. Duration or volume must be supplied.</summary>
	public decimal? WaterLimitLitres
		{
		get; set;
		}
	/// <summary>Optional home-local daily exclusion period; start and end must both be supplied, to whole minutes.</summary>
	public TimeSpan? ExcludedFrom
		{
		get; set;
		}
	/// <summary>
	/// Gets or sets the daily home-local exclusion end time to whole minutes. Supply it together with ExcludedFrom; null disables the exclusion window.
	/// </summary>
	public TimeSpan? ExcludedUntil
		{
		get; set;
		}
	}

public sealed partial class RainPointCloudClient
	{
	/// <summary>Replaces one zone's automatic low-moisture rule. Enabling requires an associated sensor and may cause future watering.</summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="expected">An unused, current schedule snapshot observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="rule">The complete low-moisture watering rule for the selected zone.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task SetTimerMoistureWateringRuleAsync (RainPointHub hub, RainPointScheduleSnapshot expected, RainPointMoistureWateringRule rule, CancellationToken cancellationToken = default) =>
		WriteTimerParameterAsync (hub, expected, Protocol.TimerMoistureRule.Edit (expected, rule), cancellationToken);
	}
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RainPointClient;

/// <summary>Automatic watering below a soil-moisture threshold. Enabling can cause future watering without a manual start command.</summary>
public sealed class RainPointMoistureWateringRule
	{
	public bool Enabled
		{
		get; set;
		}
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
	public TimeSpan? ExcludedUntil
		{
		get; set;
		}
	}

public sealed partial class RainPointCloudClient
	{
	/// <summary>Replaces one zone's automatic low-moisture rule. Enabling requires an associated sensor and may cause future watering.</summary>
	public Task SetTimerMoistureWateringRuleAsync (RainPointHub hub, RainPointScheduleSnapshot expected, RainPointMoistureWateringRule rule, CancellationToken cancellationToken = default) =>
		WriteTimerParameterAsync (hub, expected, Protocol.TimerMoistureRule.Edit (expected, rule), cancellationToken);
	}
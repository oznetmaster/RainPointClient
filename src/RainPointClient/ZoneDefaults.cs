using System;
using System.Threading;
using System.Threading.Tasks;

using RainPointClient.Protocol;

namespace RainPointClient;

/// <summary>Saved default values used by the app's manual-watering forms. These do not start watering or alter saved schedules.</summary>
public sealed class RainPointZoneDefaults
	{
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
	public Task SetTimerDefaultWateringDurationAsync (RainPointHub hub, RainPointScheduleSnapshot expected, TimeSpan? duration,
	 CancellationToken cancellationToken = default) =>
	 WriteTimerParameterAsync (hub, expected, TimerZoneDefaults.EditDuration (expected, duration), cancellationToken);

	/// <summary>Sets saved misting on/off defaults, each 5..3600 whole seconds. A null value restores that field's app-default sentinel. Does not start misting.</summary>
	public Task SetTimerMistingDefaultsAsync (RainPointHub hub, RainPointScheduleSnapshot expected, TimeSpan? runTime, TimeSpan? interval,
	 CancellationToken cancellationToken = default) =>
	 WriteTimerParameterAsync (hub, expected, TimerZoneDefaults.EditMisting (expected, runTime, interval), cancellationToken);
	}
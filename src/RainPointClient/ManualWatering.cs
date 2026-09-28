using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace RainPointClient;

public sealed partial class RainPointCloudClient
	{
	/// <summary>Starts manual misting: 1..720 whole duration minutes, with 5..3600 whole seconds per burst and pause.</summary>
	/// <remarks>Requires HTV345FRF firmware 120 or newer. Physical treatment of pauses remains unverified.</remarks>
	public Task<RainPointWateringCommandResult> StartMistingAsync (RainPointHub hub, int address, int zone,
		 TimeSpan duration, TimeSpan wateringTime, TimeSpan pauseTime, CancellationToken cancellationToken = default)
		{
		ValidateManualTime (duration, 1, 720, TimeSpan.TicksPerMinute, nameof (duration));
		ValidateManualTime (wateringTime, 5, 3600, TimeSpan.TicksPerSecond, nameof (wateringTime));
		ValidateManualTime (pauseTime, 5, 3600, TimeSpan.TicksPerSecond, nameof (pauseTime));
		return ControlAsync (hub, address, zone, 2, (int)duration.TotalSeconds, cancellationToken,
			 EncodeManualIntervals ((int)wateringTime.TotalSeconds, (int)pauseTime.TotalSeconds));
		}

	/// <summary>Starts manual cycle-and-soak: 5..1440 whole watering minutes, with 1..720 whole minutes per burst and pause.</summary>
	/// <remarks>Requires HTV345FRF firmware 120 or newer. Burst cannot exceed total watering duration; pauses extend elapsed time.</remarks>
	public Task<RainPointWateringCommandResult> StartCycleAndSoakAsync (RainPointHub hub, int address, int zone,
		 TimeSpan duration, TimeSpan wateringTime, TimeSpan pauseTime, CancellationToken cancellationToken = default)
		{
		ValidateManualTime (duration, 5, 1440, TimeSpan.TicksPerMinute, nameof (duration));
		ValidateManualTime (wateringTime, 1, 720, TimeSpan.TicksPerMinute, nameof (wateringTime));
		ValidateManualTime (pauseTime, 1, 720, TimeSpan.TicksPerMinute, nameof (pauseTime));
		if (wateringTime > duration)
			throw new ArgumentOutOfRangeException (nameof (wateringTime), "A burst cannot exceed the total watering duration.");
		return ControlAsync (hub, address, zone, 3, (int)duration.TotalMinutes, cancellationToken,
			 EncodeManualIntervals ((int)wateringTime.TotalMinutes, (int)pauseTime.TotalMinutes));
		}

	private static void ValidateManualTime (TimeSpan value, int minimum, int maximum, long unit, string name)
		{
		if (value.Ticks % unit != 0 || value.Ticks < minimum * unit || value.Ticks > maximum * unit)
			throw new ArgumentOutOfRangeException (name, "The duration is outside the supported whole-unit range.");
		}

	// App manual form: two little-endian unsigned 16-bit intervals, in mode-specific units.
	private static string EncodeManualIntervals (int watering, int pause) =>
		 (watering & 255).ToString ("X2", CultureInfo.InvariantCulture)
		 + (watering >> 8).ToString ("X2", CultureInfo.InvariantCulture)
		 + (pause & 255).ToString ("X2", CultureInfo.InvariantCulture)
		 + (pause >> 8).ToString ("X2", CultureInfo.InvariantCulture);
	}
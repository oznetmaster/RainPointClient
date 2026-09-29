// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace RainPointClient;

public sealed partial class RainPointCloudClient
	{
	/// <summary>Starts manual misting: 1..720 whole duration minutes, with 5..3600 whole seconds per burst and pause.</summary>
	/// <remarks>Requires HTV345FRF firmware 120 or newer. Physical treatment of pauses remains unverified.</remarks>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="address">The paired child's RF address within its hub, distinct from its cloud database ID.</param>
	/// <param name="zone">The one-based zone number on the selected timer.</param>
	/// <param name="duration">The configured watering duration; use the operation's documented range and mode-specific treatment of pauses.</param>
	/// <param name="wateringTime">The watering burst length for a cyclic operation, in the mode-specific range documented above.</param>
	/// <param name="pauseTime">The pause between bursts, in the mode-specific range documented above.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing cloud acceptance and any separately reported watering feedback.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
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
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="address">The paired child's RF address within its hub, distinct from its cloud database ID.</param>
	/// <param name="zone">The one-based zone number on the selected timer.</param>
	/// <param name="duration">The configured watering duration; use the operation's documented range and mode-specific treatment of pauses.</param>
	/// <param name="wateringTime">The watering burst length for a cyclic operation, in the mode-specific range documented above.</param>
	/// <param name="pauseTime">The pause between bursts, in the mode-specific range documented above.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing cloud acceptance and any separately reported watering feedback.</returns>
	/// <exception cref="System.ArgumentOutOfRangeException">A burst cannot exceed the total watering duration.</exception>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
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
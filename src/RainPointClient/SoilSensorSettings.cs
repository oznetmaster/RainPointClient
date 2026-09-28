// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using RainPointClient.Protocol;

namespace RainPointClient;

/// <summary>Saved sensor association and moisture stop threshold, not current soil readings.</summary>
public sealed class RainPointSoilSensorSettings
	{
	internal RainPointSoilSensorSettings (int address, int threshold)
		{
		SensorAddress = address == 0 ? null : address;
		StopAboveMoisturePercent = threshold == 0 ? null : threshold;
		}
	/// <summary>Paired RF address. Null means no association.</summary>
	public int? SensorAddress
		{
		get;
		}
	/// <summary>Stop when moisture exceeds this percentage. Null is the disabled zero sentinel.</summary>
	public int? StopAboveMoisturePercent
		{
		get;
		}
	}

public sealed partial class RainPointCloudClient
	{
	/// <summary>Reads compatible, currently unassigned HCS005FRF/HCS021FRF sensors on this hub. Does not pair devices.</summary>
	public async Task<IReadOnlyList<RainPointDevice>> GetAvailableSoilSensorsAsync (RainPointHub hub, RainPointScheduleSnapshot expected, CancellationToken cancellationToken = default)
		{
		ValidateScheduleSnapshot (hub, expected);
		var matches = (await GetHubsAsync (hub.HomeId, cancellationToken).ConfigureAwait (false)).Where (h => h.Id == hub.Id).ToArray ();
		if (matches.Length != 1 || matches[0].DeviceName != hub.DeviceName || matches[0].ProductKey != hub.ProductKey)
			throw new RainPointException ("Rediscover the hub before selecting a sensor.");
		var current = matches[0];
		var timer = GetTimer (current, expected.Address);
		if (timer.Id != expected.DeviceId || timer.Parameter != expected.Parameter || timer.FirmwareVersion != expected.FirmwareVersion || timer.PortNumber != expected.PortNumber)
			throw new RainPointException ("Timer settings changed. Reload before selecting a sensor.");
		HashSet<int> used = new ();
		foreach (var device in current.Devices)
			{
			if (device.SupportedZoneCount == 3)
				{
				for (int zone = 1; zone <= 3; zone++)
					{
					if (device.Id == expected.DeviceId && zone == expected.Zone)
						continue;
					var probe = new RainPointScheduleSnapshot (device.Address, zone, TimerReadingAvailability.NotReported, Array.Empty<RainPointSchedule> ()) { Parameter = device.Parameter, PortNumber = device.PortNumber };
					TimerSoilSettings.Decode (probe);
					if (probe.SoilSensorAvailability != TimerReadingAvailability.Decoded)
						throw new NotSupportedException ("Another zone's sensor assignment is unreadable.");
					if (probe.SoilSensorSettings!.SensorAddress is int address)
						used.Add (address);
					}
				}
			else if (device.Model.StartsWith ("HTV", StringComparison.OrdinalIgnoreCase) || device.Model.StartsWith ("HTP", StringComparison.OrdinalIgnoreCase) || device.Model.StartsWith ("HIC", StringComparison.OrdinalIgnoreCase))
				throw new NotSupportedException ("Cannot verify sensor assignments on another unsupported watering device.");
			}
		var sensors = current.Devices.Where (d => d.Model is "HCS005FRF" or "HCS021FRF" && d.Address is > 0 and <= 255 && d.Id > 0 && !used.Contains (d.Address)).ToArray ();
		if (sensors.Any (s => current.Devices.Count (d => d.Address == s.Address) != 1))
			throw new RainPointException ("Ambiguous sensor address. Rediscover paired devices.");
		return Array.AsReadOnly (sensors);
		}
	/// <summary>Associates an already paired compatible soil sensor, or clears the association with null. Preserves all other settings.</summary>
	public async Task SetTimerSoilSensorAsync (RainPointHub hub, RainPointScheduleSnapshot expected, int? sensorAddress, CancellationToken cancellationToken = default)
		{
		if (sensorAddress is < 1 or > 255)
			throw new ArgumentOutOfRangeException (nameof (sensorAddress));
		string updated = TimerSoilSettings.Edit (expected, sensorAddress, null, true);
		ValidateScheduleSnapshot (hub, expected);
		if (sensorAddress.HasValue)
			{
			var available = await GetAvailableSoilSensorsAsync (hub, expected, cancellationToken).ConfigureAwait (false);
			if (!available.Any (s => s.Address == sensorAddress))
				throw new ArgumentException ("Choose an available paired soil sensor on this hub.", nameof (sensorAddress));
			}
		await WriteTimerParameterAsync (hub, expected, updated, cancellationToken).ConfigureAwait (false);
		}
	/// <summary>Sets the saved moisture stop threshold (1..100%), or disables it with null. Requires an associated sensor to enable.</summary>
	public Task SetTimerMoistureStopAsync (RainPointHub hub, RainPointScheduleSnapshot expected, int? percent, CancellationToken cancellationToken = default) =>
		WriteTimerParameterAsync (hub, expected, TimerSoilSettings.Edit (expected, null, percent, false), cancellationToken);
	}
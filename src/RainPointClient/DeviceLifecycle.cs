// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using RainPointClient.Protocol;
namespace RainPointClient;

public sealed partial class RainPointCloudClient
	{
	/// <summary>Starts the hub's RF discovery window for one supported catalog model. This can pair a device in pairing mode. The returned duration is the cloud's search window, not proof of pairing.</summary>
	public async Task<TimeSpan> StartDevicePairingAsync (RainPointHomeDetails home, RainPointHub expected, RainPointProductModel model, CancellationToken cancellationToken = default)
		{
		if (model is null)
			throw new ArgumentNullException (nameof (model));
		if (!SupportedLifecycleChild (model.Model) || model.ModelCode <= 0 || model.IsHub == true)
			throw new NotSupportedException ("Pairing supports the HTV345FRF timer and HCS005FRF/HCS021FRF soil sensors.");
		var current = await CurrentLifecycleHubAsync (home, expected, cancellationToken).ConfigureAwait (false);
		if (current.ModelCode is not > 0)
			throw new NotSupportedException ("The discovered hub must provide its model code.");
		var catalog = await GetProductCatalogAsync (cancellationToken).ConfigureAwait (false);
		if (!catalog.Models.Any (m => m.ModelCode == model.ModelCode && m.Model == model.Model && m.IsHub != true))
			throw new InvalidOperationException ("The selected model is no longer in the catalog.");
		var session = await ClaimHomeWriteAsync (home, cancellationToken).ConfigureAwait (false);
		var response = await SendAsync<PairingRequest, ApiResult<PairingWindowWire>> (HttpMethod.Post, "app/device/sub/search", new ()
			{
			HubId = current.Id.ToString (CultureInfo.InvariantCulture),
			DeviceName = current.DeviceName,
			ProductKey = current.ProductKey,
			ParentModelCode = current.ModelCode.Value,
			ModelCodes = new[] { model.ModelCode },
			DeviceId = ""
			}, session, cancellationToken).ConfigureAwait (false);
		CheckResult (response, session);
		var window = RequireData (response);
		if (window.Seconds is < 0 or > 86400)
			throw new RainPointException ("The pairing response contains an invalid search duration.");
		return TimeSpan.FromSeconds (window.Seconds);
		}
	/// <summary>Cancels RF discovery. Does not undo devices already paired; rediscover the hub afterwards.</summary>
	public async Task CancelDevicePairingAsync (RainPointHub hub, CancellationToken cancellationToken = default)
		{
		ValidateHub (hub);
		var session = GetSession ();
		var response = await SendAsync<PairingCancelWire, ApiResult> (HttpMethod.Post, "app/device/sub/cancelSearch", new ()
			{
			DeviceName = hub.DeviceName,
			ProductKey = hub.ProductKey
			}, session, cancellationToken).ConfigureAwait (false);
		CheckResult (response, session);
		}
	/// <summary>Removes the hub and its children from the home. Requires a fresh home/hub observation; never retries.</summary>
	public async Task RemoveHubAsync (RainPointHomeDetails home, RainPointHub expected, CancellationToken cancellationToken = default)
		{
		await CurrentLifecycleHubAsync (home, expected, cancellationToken).ConfigureAwait (false);
		await WriteHomeAsync (home, "app/device/main/delete", new RemoveHubWire { HubId = expected.Id.ToString (CultureInfo.InvariantCulture) }, cancellationToken).ConfigureAwait (false);
		}
	/// <summary>Removes a supported paired child, clearing matching soil-sensor associations on every supported timer zone in the same request. Unknown sibling families are rejected because their relationships cannot be safely updated.</summary>
	public async Task RemoveDeviceAsync (RainPointHomeDetails home, RainPointHub expected, int address, CancellationToken cancellationToken = default)
		{
		var current = await CurrentLifecycleHubAsync (home, expected, cancellationToken).ConfigureAwait (false);
		if (current.Devices.Any (d => !SupportedLifecycleChild (d.Model) || d.Id is not > 0))
			throw new NotSupportedException ("Removal requires known timer/sensor siblings with complete cloud identities.");
		var removed = current.Devices.SingleOrDefault (d => d.Address == address) ?? throw new ArgumentException ("Choose a discovered child address.", nameof (address));
		List<RemoveRelationWire> relations = new ();
		foreach (var timer in current.Devices.Where (d => d.Address != address && d.SupportedZoneCount == 3))
			{
			string? parameter = timer.Parameter;
			for (int zone = 1; zone <= 3; zone++)
				{
				var snapshot = new RainPointScheduleSnapshot (timer.Address, zone, ScheduleDecoder.Decode (timer, zone).Availability, Array.Empty<RainPointSchedule> ()) { Parameter = parameter, PortNumber = timer.PortNumber, FirmwareVersion = timer.FirmwareVersion };
				TimerSoilSettings.Decode (snapshot);
				if (snapshot.SoilSensorAvailability != TimerReadingAvailability.Decoded)
					throw new NotSupportedException ("A remaining zone's sensor relationship cannot be read.");
				if (snapshot.SoilSensorSettings!.SensorAddress == address)
					parameter = TimerSoilSettings.Edit (snapshot, null, null, true);
				}
			if (parameter != timer.Parameter)
				relations.Add (new ()
					{
					DeviceId = timer.Id!.Value.ToString (CultureInfo.InvariantCulture),
					Parameter = parameter!,
					Style = timer.Style ?? ""
					});
			}
		await WriteHomeAsync (home, "app/device/sub/deleteSubDevice", new RemoveChildWire { HubId = current.Id.ToString (CultureInfo.InvariantCulture), DeviceId = removed.Id!.Value.ToString (CultureInfo.InvariantCulture), Relations = relations }, cancellationToken).ConfigureAwait (false);
		}
	private static bool SupportedLifecycleChild (string model) => model is "HTV345FRF" or "HCS005FRF" or "HCS021FRF";
	private async Task<RainPointHub> CurrentLifecycleHubAsync (RainPointHomeDetails home, RainPointHub expected, CancellationToken token)
		{
		if (home is null)
			throw new ArgumentNullException (nameof (home));
		ValidateHub (expected);
		if (home.Id != expected.HomeId || !ReferenceEquals (home.Session, GetSession ()) || Volatile.Read (ref home.Attempted) != 0)
			throw new InvalidOperationException ("Read the home and hub again before changing pairing.");
		var current = (await GetHubsAsync (home.Id, token).ConfigureAwait (false)).SingleOrDefault (h => h.Id == expected.Id);
		if (current is null || JsonSerializer.Serialize (current, _json) != JsonSerializer.Serialize (expected, _json))
			throw new InvalidOperationException ("The hub or its children changed. Reload before changing pairing.");
		return current;
		}
	private sealed class PairingRequest
		{
		[JsonPropertyName ("mid")] public string HubId { get; set; } = "";
		[JsonPropertyName ("deviceName")] public string DeviceName { get; set; } = "";
		[JsonPropertyName ("productKey")] public string ProductKey { get; set; } = "";
		[JsonPropertyName ("parentModelCode")]
		public int ParentModelCode
			{
			get; set;
			}
		[JsonPropertyName ("modelCode")] public int[] ModelCodes { get; set; } = Array.Empty<int> ();
		[JsonPropertyName ("did")] public string DeviceId { get; set; } = "";
		}
	private sealed class PairingWindowWire
		{
		[JsonPropertyName ("time"), JsonRequired]
		public int Seconds
			{
			get; set;
			}
		}
	private sealed class PairingCancelWire
		{
		[JsonPropertyName ("deviceName")] public string DeviceName { get; set; } = ""; [JsonPropertyName ("productKey")] public string ProductKey { get; set; } = "";
		}
	private sealed class RemoveHubWire
		{
		[JsonPropertyName ("mid")] public string HubId { get; set; } = "";
		}
	private sealed class RemoveChildWire
		{
		[JsonPropertyName ("mid")] public string HubId { get; set; } = "";
		[JsonPropertyName ("sid")] public string DeviceId { get; set; } = "";
		[JsonPropertyName ("updateList")] public List<RemoveRelationWire> Relations { get; set; } = new ();
		}
	private sealed class RemoveRelationWire
		{
		[JsonPropertyName ("sid")] public string DeviceId { get; set; } = "";
		[JsonPropertyName ("param")] public string Parameter { get; set; } = "";
		[JsonPropertyName ("style")] public string Style { get; set; } = "";
		}
	}
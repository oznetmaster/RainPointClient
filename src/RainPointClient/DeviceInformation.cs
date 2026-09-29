// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using RainPointClient.Protocol;

namespace RainPointClient;

public sealed partial class RainPointCloudClient
	{
	/// <summary>
	/// Reads installed and offered hub firmware metadata without installing an update.
	/// </summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the typed firmware status result.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task<RainPointFirmwareStatus> GetHubFirmwareAsync (RainPointHub hub, CancellationToken cancellationToken = default)
		{
		ValidateHub (hub);
		return GetFirmwareAsync ("app/device/firmware/upgrade/info/v2?mid=" + hub.Id.ToString (CultureInfo.InvariantCulture), cancellationToken);
		}

	/// <summary>
	/// Reads firmware metadata for a paired timer without installing an update.
	/// </summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="address">The paired child's RF address within its hub, distinct from its cloud database ID.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the typed firmware status result.</returns>
	/// <exception cref="System.ArgumentException">The discovered timer has no usable cloud sub-device identifier.</exception>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task<RainPointFirmwareStatus> GetTimerFirmwareAsync (RainPointHub hub, int address, CancellationToken cancellationToken = default)
		{
		RainPointDevice device = GetTimer (hub, address);
		return device.Id is not > 0
			? throw new ArgumentException ("The discovered timer has no usable cloud sub-device identifier.", nameof (address))
			: GetFirmwareAsync ("app/device/sub/firmware/upgrade/info?sid=" + device.Id.Value.ToString (CultureInfo.InvariantCulture), cancellationToken);
		}

	private async Task<RainPointFirmwareStatus> GetFirmwareAsync (string path, CancellationToken cancellationToken)
		{
		RainPointFirmwareStatus result = await GetAsync<RainPointFirmwareStatus> (path, cancellationToken).ConfigureAwait (false);
		if (string.IsNullOrWhiteSpace (result.InstalledVersion)
			 || (result.AvailableUpdate is not null && string.IsNullOrWhiteSpace (result.AvailableUpdate.Version)))
			{
			throw new RainPointException ("The firmware response omitted a usable version.");
			}
		return result;
		}

	/// <summary>Changes the hub's automatic time-broadcast setting, preserving unrelated settings.</summary>
	/// <remarks>Reads fresh settings before writing. The result is a cloud acknowledgement, not RF delivery confirmation.</remarks>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="enabled">Whether the selected feature or saved plan should be enabled.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="System.ArgumentException">Use a discovered hub with its home identifier.</exception>
	/// <exception cref="RainPointException">The hub's current settings could not be uniquely located. The hub's time-broadcast setting is unreadable. The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task SetAutomaticTimeBroadcastAsync (RainPointHub hub, bool enabled, CancellationToken cancellationToken = default)
		{
		ValidateHub (hub);
		if (hub.HomeId <= 0)
			{
			throw new ArgumentException ("Use a discovered hub with its home identifier.", nameof (hub));
			}
		RainPointHub[] fresh = (await GetHubsAsync (hub.HomeId, cancellationToken).ConfigureAwait (false)).Where (item => item.Id == hub.Id).ToArray ();
		if (fresh.Length != 1)
			{
			throw new RainPointException ("The hub's current settings could not be uniquely located.");
			}
		string parameter = HubSettings.SetBroadcast (fresh[0].Parameter, enabled)
			 ?? throw new RainPointException ("The hub's time-broadcast setting is unreadable.");
		Session session = GetSession ();
		ApiResult response = await SendAsync<HubParameterRequest, ApiResult> (HttpMethod.Post, "app/device/main/update",
			 new HubParameterRequest { Id = hub.Id, Parameter = parameter }, session, cancellationToken).ConfigureAwait (false);
		CheckResult (response, session);
		}

	/// <summary>Requests a one-shot hub time broadcast; it does not actuate a valve.</summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the typed command outcome result.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task<RainPointCommandOutcome> BroadcastTimeAsync (RainPointHub hub, CancellationToken cancellationToken = default)
		{
		ValidateHub (hub);
		Session session = GetSession ();
		ApiResult response = await SendAsync<ValveCommand, ApiResult> (HttpMethod.Post, "app/device/controlWorkMode",
			 new ValveCommand { HubId = hub.Id, DeviceName = hub.DeviceName, ProductKey = hub.ProductKey, Address = 0, Zone = 1, Mode = 0 },
			 session, cancellationToken).ConfigureAwait (false);
		if (response.Code == 4)
			{
			return RainPointCommandOutcome.AlreadyInRequestedStateOrTransitioning;
			}
		CheckResult (response, session);
		return RainPointCommandOutcome.Accepted;
		}

	private static void ValidateHub (RainPointHub hub)
		{
		if (hub is null)
			{
			throw new ArgumentNullException (nameof (hub));
			}
		if (hub.Id <= 0 || string.IsNullOrWhiteSpace (hub.DeviceName) || string.IsNullOrWhiteSpace (hub.ProductKey))
			{
			throw new ArgumentException ("Use a discovered hub with complete addressing.", nameof (hub));
			}
		if (!string.Equals (hub.Model, "HWG023WBRF", StringComparison.OrdinalIgnoreCase)
			 && !string.Equals (hub.Model, "HWG023WBRF-V2", StringComparison.OrdinalIgnoreCase))
			{
			throw new NotSupportedException ("Hub operations currently support HWG023WBRF and HWG023WBRF-V2.");
			}
		}
	}
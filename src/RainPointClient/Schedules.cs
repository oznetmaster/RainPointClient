// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using RainPointClient.Protocol;

namespace RainPointClient;

public sealed partial class RainPointCloudClient
	{
	/// <summary>Reads fresh saved schedules for a supported HTV145FRF, HTV245FRF or HTV345FRF zone. Sends no configuration or valve commands.</summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="address">The paired child's RF address within its hub, distinct from its cloud database ID.</param>
	/// <param name="zone">The one-based zone number on the selected timer.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the typed schedule snapshot result.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task<RainPointScheduleSnapshot> GetTimerSchedulesAsync (RainPointHub hub, int address, int zone,
		 CancellationToken cancellationToken = default)
		{
		RainPointDevice fresh = await GetScheduleDeviceAsync (hub, address, zone, cancellationToken).ConfigureAwait (false);
		RainPointScheduleSnapshot result = ScheduleDecoder.Decode (fresh, zone);
		result.HomeId = hub.HomeId;
		result.HubId = hub.Id;
		result.DeviceId = fresh.Id;
		result.Parameter = fresh.Parameter;
		result.FirmwareVersion = fresh.FirmwareVersion;
		result.PortNumber = fresh.PortNumber;
		TimerPlanSettings.Decode (result);
		TimerZoneDefaults.Decode (result);
		TimerFlowCalibration.Decode (result);
		TimerSoilSettings.Decode (result);
		TimerMoistureRule.Decode (result);
		return result;
		}

	/// <summary>Adds a normal-irrigation plan, preserving other plans and settings. Read a fresh snapshot before each edit.</summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="expected">An unused, current schedule snapshot observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="schedule">The complete typed plan to validate and encode.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task AddTimerScheduleAsync (RainPointHub hub, RainPointScheduleSnapshot expected, RainPointIrrigationSchedule schedule,
		 CancellationToken cancellationToken = default) =>
		 EditTimerScheduleAsync (hub, expected, ScheduleEdit.Add, -1, ScheduleEditor.Encode (schedule), false, cancellationToken);

	/// <summary>Replaces one plan with a normal-irrigation plan. Index is from the supplied snapshot.</summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="expected">An unused, current schedule snapshot observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="index">The zero-based plan position in this snapshot, not a durable plan identifier.</param>
	/// <param name="schedule">The complete typed plan to validate and encode.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task UpdateTimerScheduleAsync (RainPointHub hub, RainPointScheduleSnapshot expected, int index, RainPointIrrigationSchedule schedule,
		 CancellationToken cancellationToken = default) =>
		 EditTimerScheduleAsync (hub, expected, ScheduleEdit.Replace, index, ScheduleEditor.Encode (schedule), false, cancellationToken);

	/// <summary>Adds a cycle-and-soak plan. Read a fresh snapshot before each edit.</summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="expected">An unused, current schedule snapshot observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="schedule">The complete typed plan to validate and encode.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task AddTimerScheduleAsync (RainPointHub hub, RainPointScheduleSnapshot expected, RainPointCycleAndSoakSchedule schedule,
		 CancellationToken cancellationToken = default) =>
		 EditTimerScheduleAsync (hub, expected, ScheduleEdit.Add, -1, ScheduleEditor.EncodeCycleAndSoak (schedule), false, cancellationToken);

	/// <summary>Replaces one plan with a cycle-and-soak plan. Index is from the supplied snapshot.</summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="expected">An unused, current schedule snapshot observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="index">The zero-based plan position in this snapshot, not a durable plan identifier.</param>
	/// <param name="schedule">The complete typed plan to validate and encode.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task UpdateTimerScheduleAsync (RainPointHub hub, RainPointScheduleSnapshot expected, int index, RainPointCycleAndSoakSchedule schedule,
		 CancellationToken cancellationToken = default) =>
		 EditTimerScheduleAsync (hub, expected, ScheduleEdit.Replace, index, ScheduleEditor.EncodeCycleAndSoak (schedule), false, cancellationToken);

	/// <summary>Adds a misting plan. Read a fresh snapshot before each edit.</summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="expected">An unused, current schedule snapshot observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="schedule">The complete typed plan to validate and encode.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task AddTimerScheduleAsync (RainPointHub hub, RainPointScheduleSnapshot expected, RainPointMistingSchedule schedule,
		 CancellationToken cancellationToken = default) =>
		 EditTimerScheduleAsync (hub, expected, ScheduleEdit.Add, -1, ScheduleEditor.EncodeMisting (schedule), false, cancellationToken);

	/// <summary>Replaces one plan with a misting plan. Index is from the supplied snapshot.</summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="expected">An unused, current schedule snapshot observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="index">The zero-based plan position in this snapshot, not a durable plan identifier.</param>
	/// <param name="schedule">The complete typed plan to validate and encode.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task UpdateTimerScheduleAsync (RainPointHub hub, RainPointScheduleSnapshot expected, int index, RainPointMistingSchedule schedule,
		 CancellationToken cancellationToken = default) =>
		 EditTimerScheduleAsync (hub, expected, ScheduleEdit.Replace, index, ScheduleEditor.EncodeMisting (schedule), false, cancellationToken);

	/// <summary>Deletes one saved plan, preserving other plans and settings.</summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="expected">An unused, current schedule snapshot observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="index">The zero-based plan position in this snapshot, not a durable plan identifier.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task DeleteTimerScheduleAsync (RainPointHub hub, RainPointScheduleSnapshot expected, int index,
		 CancellationToken cancellationToken = default) =>
		 EditTimerScheduleAsync (hub, expected, ScheduleEdit.Delete, index, null, false, cancellationToken);

	/// <summary>Enables or disables one existing plan without rewriting its timing fields.</summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="expected">An unused, current schedule snapshot observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="index">The zero-based plan position in this snapshot, not a durable plan identifier.</param>
	/// <param name="enabled">Whether the selected feature or saved plan should be enabled.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task SetTimerScheduleEnabledAsync (RainPointHub hub, RainPointScheduleSnapshot expected, int index, bool enabled,
		 CancellationToken cancellationToken = default) =>
		 EditTimerScheduleAsync (hub, expected, ScheduleEdit.SetEnabled, index, null, enabled, cancellationToken);

	/// <summary>Replaces January..December percentages (10..200) for one zone. Use a fresh snapshot before each write.</summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="expected">An unused, current schedule snapshot observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="percentages">Twelve seasonal duration percentages in January-to-December order, validated by the operation.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task SetTimerSeasonalAdjustmentAsync (RainPointHub hub, RainPointScheduleSnapshot expected, IReadOnlyList<int> percentages,
		 CancellationToken cancellationToken = default) =>
		 WriteTimerParameterAsync (hub, expected, TimerPlanSettings.EditSeason (expected, percentages), cancellationToken);

	/// <summary>Sets one zone's rain-delay end time in the home's local calendar, to whole seconds. Null clears the field to zero. Does not send a valve stop command.</summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="expected">An unused, current schedule snapshot observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="endsAtLocal">The rain-delay end in home-local wall time with Unspecified kind, or null to clear the delay.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task SetTimerRainDelayAsync (RainPointHub hub, RainPointScheduleSnapshot expected, DateTime? endsAtLocal,
		 CancellationToken cancellationToken = default) =>
		 WriteTimerParameterAsync (hub, expected, TimerPlanSettings.EditRainDelay (expected, endsAtLocal), cancellationToken);

	private Task EditTimerScheduleAsync (RainPointHub hub, RainPointScheduleSnapshot expected, ScheduleEdit edit,
		 int index, string? record, bool enabled, CancellationToken cancellationToken)
		{
		ValidateScheduleSnapshot (hub, expected);
		return WriteTimerParameterAsync (hub, expected, ScheduleEditor.Edit (expected, edit, index, record, enabled), cancellationToken);
		}

	private static void ValidateScheduleSnapshot (RainPointHub hub, RainPointScheduleSnapshot expected)
		{
		ValidateHub (hub);
		if (expected is null)
			{
			throw new ArgumentNullException (nameof (expected));
			}
		if (expected.HomeId != hub.HomeId || expected.HubId != hub.Id || expected.DeviceId is not > 0
			 || expected.Availability != TimerReadingAvailability.Decoded || expected.Parameter is null)
			{
			throw new ArgumentException ("Use a decoded schedule snapshot obtained for this hub.", nameof (expected));
			}
		}

	private async Task WriteTimerParameterAsync (RainPointHub hub, RainPointScheduleSnapshot expected, string updated, CancellationToken cancellationToken)
		{
		ValidateScheduleSnapshot (hub, expected);
		RainPointDevice fresh = await GetScheduleDeviceAsync (hub, expected.Address, expected.Zone, cancellationToken).ConfigureAwait (false);
		if (fresh.Id != expected.DeviceId || fresh.Parameter != expected.Parameter
			 || fresh.FirmwareVersion != expected.FirmwareVersion || fresh.PortNumber != expected.PortNumber)
			{
			throw new RainPointException ("The timer configuration changed. Read schedules again before editing.");
			}
		cancellationToken.ThrowIfCancellationRequested ();
		if (Interlocked.CompareExchange (ref expected.WriteAttempted, 1, 0) != 0)
			{
			throw new InvalidOperationException ("This snapshot has already been used for a write. Read schedules again; do not replay an uncertain write.");
			}
		if (updated == expected.Parameter)
			{
			return;
			}
		Session session = GetSession ();
		ApiResult response = await SendAsync<TimerParameterRequest, ApiResult> (HttpMethod.Post, "app/device/sub/update",
			 new TimerParameterRequest { HubId = hub.Id, DeviceId = fresh.Id!.Value, Parameter = updated }, session, cancellationToken).ConfigureAwait (false);
		CheckResult (response, session);
		}

	private async Task<RainPointDevice> GetScheduleDeviceAsync (RainPointHub hub, int address, int zone, CancellationToken cancellationToken)
		{
		ValidateHub (hub);
		RainPointDevice timer = GetTimer (hub, address);
		if (zone < 1 || zone > timer.SupportedZoneCount)
			{
			throw new ArgumentOutOfRangeException (nameof (zone));
			}
		if (hub.HomeId <= 0 || timer.Id is not > 0)
			{
			throw new ArgumentException ("Use a discovered hub and timer with their cloud identifiers.", nameof (hub));
			}
		RainPointHub[] matches = (await GetHubsAsync (hub.HomeId, cancellationToken).ConfigureAwait (false))
			 .Where (item => item.Id == hub.Id).ToArray ();
		if (matches.Length != 1 || matches[0].DeviceName != hub.DeviceName || matches[0].ProductKey != hub.ProductKey)
			{
			throw new RainPointException ("The hub's current configuration could not be uniquely located.");
			}
		RainPointDevice fresh = GetTimer (matches[0], address);
		if (fresh.Id != timer.Id || fresh.Model != timer.Model || fresh.SupportedZoneCount != timer.SupportedZoneCount)
			{
			throw new RainPointException ("The paired timer changed; discover devices again before reading schedules.");
			}
		return fresh;
		}
	}
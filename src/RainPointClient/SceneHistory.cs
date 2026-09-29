// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace RainPointClient;

/// <summary>Reported scene-action outcome from the inspected vendor app. This is not independent physical confirmation.</summary>
public enum RainPointSceneActionOutcome
	{
	/// <summary>
	/// The cloud reports the scene action outcome as unknown.
	/// </summary>
	Unknown,
	/// <summary>
	/// The cloud reports the scene action outcome as succeeded.
	/// </summary>
	Succeeded,
	/// <summary>
	/// The cloud reports the scene action outcome as failed.
	/// </summary>
	Failed,
	/// <summary>
	/// The cloud reports the scene action outcome as invalid.
	/// </summary>
	Invalid,
	/// <summary>
	/// The cloud reports the scene action outcome as missing device.
	/// </summary>
	MissingDevice,
	/// <summary>
	/// The cloud reports the scene action outcome as data point error.
	/// </summary>
	DataPointError,
	/// <summary>
	/// The cloud reports the scene action outcome as plan conflict.
	/// </summary>
	PlanConflict,
	/// <summary>
	/// The cloud reports the scene action outcome as low power.
	/// </summary>
	LowPower,
	/// <summary>
	/// The cloud reports the scene action outcome as too frequent.
	/// </summary>
	TooFrequent,
	/// <summary>
	/// The cloud reports the scene action outcome as timeout.
	/// </summary>
	Timeout,
	/// <summary>
	/// The cloud reports the scene action outcome as vendor error.
	/// </summary>
	VendorError
	}

/// <summary>The cloud's result for one scene action. A result code is not independent confirmation of physical device behavior.</summary>
public sealed class RainPointSceneActionResult
	{
	/// <summary>
	/// Initializes scene action result from the supplied typed values.
	/// </summary>
	/// <param name="wire">The attributed protocol response from which to create the typed observation.</param>
	internal RainPointSceneActionResult (SceneActionResultWire wire)
		{
		ActionId = wire.ActionId;
		HubId = wire.HubId;
		DeviceAddress = wire.Address;
		ResultCode = wire.Result;
		}
	/// <summary>
	/// Gets the cloud identifier of the scene action.
	/// </summary>
	public long ActionId
		{
		get;
		}
	/// <summary>
	/// Gets the cloud hub identifier associated with this record.
	/// </summary>
	public long HubId
		{
		get;
		}
	/// <summary>Reported device address, or null when omitted (as observed for notification actions).</summary>
	public int? DeviceAddress
		{
		get;
		}
	/// <summary>Original vendor result code, retained even when no specific meaning is known.</summary>
	public int ResultCode
		{
		get;
		}
	/// <summary>App-defined meaning where established; unknown codes remain Unknown. A successful result is only the cloud report.</summary>
	public RainPointSceneActionOutcome Outcome => ResultCode switch
		{
			0 => RainPointSceneActionOutcome.Succeeded,
			1 => RainPointSceneActionOutcome.Failed,
			100 => RainPointSceneActionOutcome.Invalid,
			101 => RainPointSceneActionOutcome.MissingDevice,
			103 => RainPointSceneActionOutcome.DataPointError,
			104 or 105 or 106 => RainPointSceneActionOutcome.PlanConflict,
			107 => RainPointSceneActionOutcome.LowPower,
			108 => RainPointSceneActionOutcome.TooFrequent,
			200 => RainPointSceneActionOutcome.Timeout,
			>= 1000 and <= 9999 => RainPointSceneActionOutcome.VendorError,
			_ => RainPointSceneActionOutcome.Unknown
			};

	}
/// <summary>
/// Represents one cloud-reported scene execution and any supplied action results.
/// </summary>
public sealed class RainPointSceneLogEntry
	{
	/// <summary>
	/// Initializes scene log entry from the supplied typed values.
	/// </summary>
	/// <param name="wire">The attributed protocol response from which to create the typed observation.</param>
	/// <param name="actions">Decoded action results, or null when the cloud supplied no results.</param>
	internal RainPointSceneLogEntry (SceneLogWire wire, IReadOnlyList<RainPointSceneActionResult>? actions)
		{
		Id = wire.Id;
		HomeId = wire.HomeId;
		SceneId = wire.SceneId;
		TriggeredAt = DateTimeOffset.FromUnixTimeMilliseconds (wire.TriggeredAt);
		Actions = actions;
		}
	/// <summary>
	/// Gets the cloud execution-log identifier.
	/// </summary>
	public string Id
		{
		get;
		}
	/// <summary>
	/// Gets the home to which the execution record belongs.
	/// </summary>
	public long HomeId
		{
		get;
		}
	/// <summary>
	/// Gets the scene identifier associated with the execution.
	/// </summary>
	public long SceneId
		{
		get;
		}
	/// <summary>
	/// Gets the trigger instant decoded from the cloud's Unix-millisecond timestamp.
	/// </summary>
	public DateTimeOffset TriggeredAt
		{
		get;
		}
	/// <summary>Null means no result was supplied; an empty list means a supplied empty result.</summary>
	public IReadOnlyList<RainPointSceneActionResult>? Actions
		{
		get;
		}
	}
/// <summary>
/// Contains one zero-based page of scene execution history and the cloud's total count.
/// </summary>
public sealed class RainPointSceneLogPage
	{
	/// <summary>
	/// Initializes scene log page from the supplied typed values.
	/// </summary>
	/// <param name="total">The total matching-record count reported by the service.</param>
	/// <param name="page">The zero-based page index.</param>
	/// <param name="pageSize">The requested maximum records per page, from 1 through 100 for scene history.</param>
	/// <param name="entries">The execution records returned on this page.</param>
	internal RainPointSceneLogPage (long total, int page, int pageSize, IReadOnlyList<RainPointSceneLogEntry> entries)
		{
		Total = total;
		Page = page;
		PageSize = pageSize;
		Entries = entries;
		}
	/// <summary>
	/// Gets the total record count reported by the service for this query.
	/// </summary>
	public long Total
		{
		get;
		}
	/// <summary>
	/// Gets the zero-based page index requested by the caller.
	/// </summary>
	public int Page
		{
		get;
		}
	/// <summary>
	/// Gets the requested page size.
	/// </summary>
	public int PageSize
		{
		get;
		}
	/// <summary>
	/// Gets the execution records returned on this page.
	/// </summary>
	public IReadOnlyList<RainPointSceneLogEntry> Entries
		{
		get;
		}
	/// <summary>
	/// Gets whether a nonempty page and the reported total suggest another page exists; it is not a stable-snapshot guarantee.
	/// </summary>
	public bool HasMore => Entries.Count > 0 && (long)Page * PageSize + Entries.Count < Total;
	}
public sealed partial class RainPointCloudClient
	{
	/// <summary>Reads one bounded zero-based page of scene history using UTC instants. History retention after scene deletion is controlled by the service and is not guaranteed.</summary>
	/// <param name="homeId">The positive cloud home identifier.</param>
	/// <param name="from">The inclusive lower cloud-timestamp bound, after the Unix epoch.</param>
	/// <param name="through">The inclusive upper cloud-timestamp bound, after the Unix epoch.</param>
	/// <param name="page">The zero-based page index.</param>
	/// <param name="pageSize">The requested maximum records per page, from 1 through 100 for scene history.</param>
	/// <param name="sceneId">The cloud scene identifier; null leaves scene history unfiltered where permitted.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the typed scene log page result.</returns>
	/// <exception cref="System.ArgumentOutOfRangeException">An argument is outside the supported range described above.</exception>
	/// <exception cref="System.ArgumentException">Use an ordered date range after the Unix epoch.</exception>
	/// <exception cref="RainPointException">Scene history has incomplete, duplicate or out-of-scope records. Scene action results did not match the expected model. Scene action results are incomplete. The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task<RainPointSceneLogPage> GetSceneHistoryAsync (long homeId, DateTimeOffset from, DateTimeOffset through, int page = 0, int pageSize = 50, long? sceneId = null, CancellationToken cancellationToken = default)
		{
		if (homeId <= 0 || sceneId <= 0)
			throw new ArgumentOutOfRangeException (nameof (homeId));
		if (page is < 0 or int.MaxValue || pageSize is < 1 or > 100)
			throw new ArgumentOutOfRangeException (nameof (page));
		if (from.ToUnixTimeMilliseconds () < 0 || from > through)
			throw new ArgumentException ("Use an ordered date range after the Unix epoch.");
		string query = "app/scene/2.0.5/logPage?appCode=2&hid=" + IdText (homeId)
		 + "&begin=" + from.ToUnixTimeMilliseconds ().ToString (CultureInfo.InvariantCulture)
		 + "&end=" + through.ToUnixTimeMilliseconds ().ToString (CultureInfo.InvariantCulture)
		 + "&pageNum=" + (page + 1).ToString (CultureInfo.InvariantCulture) + "&pageSize=" + pageSize.ToString (CultureInfo.InvariantCulture);
		if (sceneId.HasValue)
			query += "&sceneMainId=" + IdText (sceneId.Value);
		var data = await GetAsync<SceneLogPageWire> (query, cancellationToken).ConfigureAwait (false);
		if (data.Total < 0 || data.Records is null || data.Records.Count > pageSize
		 || data.Records.Any (e => e is null || string.IsNullOrWhiteSpace (e.Id) || e.HomeId != homeId || e.SceneId <= 0 || sceneId.HasValue && e.SceneId != sceneId || e.TriggeredAt is < 0 or > 253402300799999)
		 || data.Records.Select (e => e.Id).Distinct (StringComparer.Ordinal).Count () != data.Records.Count)
			throw new RainPointException ("Scene history has incomplete, duplicate or out-of-scope records.");
		var entries = new List<RainPointSceneLogEntry> ();
		foreach (var entry in data.Records)
			{
			IReadOnlyList<RainPointSceneActionResult>? results = null;
			if (!string.IsNullOrWhiteSpace (entry.Result))
				{
				List<SceneActionResultWire>? decoded;
				try
					{
					decoded = JsonSerializer.Deserialize<List<SceneActionResultWire>> (entry.Result!, _json);
					}
				catch (JsonException) { throw new RainPointException ("Scene action results did not match the expected model."); }
				if (decoded is null || decoded.Any (a => a is null || a.ActionId < 0 || a.HubId < 0 || a.Address < 0))
					throw new RainPointException ("Scene action results are incomplete.");
				results = Array.AsReadOnly (decoded.Select (a => new RainPointSceneActionResult (a)).ToArray ());
				}
			entries.Add (new (entry, results));
			}
		return new (data.Total, page, pageSize, entries.AsReadOnly ());
		}
	}
/// <summary>
/// Internal scene log page wire representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class SceneLogPageWire
	{
	/// <summary>
	/// Stores the total protocol field for scene log page wire.
	/// </summary>
	[JsonPropertyName ("total"), JsonRequired]
	public long Total
		{
		get; set;
		}
	/// <summary>
	/// Stores the records protocol field for scene log page wire.
	/// </summary>
	[JsonPropertyName ("records"), JsonRequired]
	public List<SceneLogWire>? Records
		{
		get; set;
		}
	}
/// <summary>
/// Internal scene log wire representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class SceneLogWire
	{
	/// <summary>
	/// Stores the id protocol field for scene log wire.
	/// </summary>
	[JsonPropertyName ("id"), JsonRequired] public string Id { get; set; } = string.Empty;
	/// <summary>
	/// Stores the hid protocol field for scene log wire.
	/// </summary>
	[JsonPropertyName ("hid"), JsonRequired]
	public long HomeId
		{
		get; set;
		}
	/// <summary>
	/// Stores the sceneMainId protocol field for scene log wire.
	/// </summary>
	[JsonPropertyName ("sceneMainId"), JsonRequired]
	public long SceneId
		{
		get; set;
		}
	/// <summary>
	/// Stores the triggerTime protocol field for scene log wire.
	/// </summary>
	[JsonPropertyName ("triggerTime"), JsonRequired]
	public long TriggeredAt
		{
		get; set;
		}
	/// <summary>
	/// Stores the result protocol field for scene log wire.
	/// </summary>
	[JsonPropertyName ("result")]
	public string? Result
		{
		get; set;
		}
	}
/// <summary>
/// Internal scene action result wire representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class SceneActionResultWire
	{
	/// <summary>
	/// Stores the aid protocol field for scene action result wire.
	/// </summary>
	[JsonPropertyName ("aid"), JsonRequired]
	public long ActionId
		{
		get; set;
		}
	/// <summary>
	/// Stores the mid protocol field for scene action result wire.
	/// </summary>
	[JsonPropertyName ("mid"), JsonRequired]
	public long HubId
		{
		get; set;
		}
	/// <summary>
	/// Stores the addr protocol field for scene action result wire.
	/// </summary>
	[JsonPropertyName ("addr")]
	public int? Address
		{
		get; set;
		}
	/// <summary>
	/// Stores the rt protocol field for scene action result wire.
	/// </summary>
	[JsonPropertyName ("rt"), JsonRequired]
	public int Result
		{
		get; set;
		}
	}
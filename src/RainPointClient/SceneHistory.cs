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
	Unknown,
	Succeeded,
	Failed,
	Invalid,
	MissingDevice,
	DataPointError,
	PlanConflict,
	LowPower,
	TooFrequent,
	Timeout,
	VendorError
	}

/// <summary>The cloud's result for one scene action. A result code is not independent confirmation of physical device behavior.</summary>
public sealed class RainPointSceneActionResult
	{
	internal RainPointSceneActionResult (SceneActionResultWire wire)
		{
		ActionId = wire.ActionId;
		HubId = wire.HubId;
		DeviceAddress = wire.Address;
		ResultCode = wire.Result;
		}
	public long ActionId
		{
		get;
		}
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
public sealed class RainPointSceneLogEntry
	{
	internal RainPointSceneLogEntry (SceneLogWire wire, IReadOnlyList<RainPointSceneActionResult>? actions)
		{
		Id = wire.Id;
		HomeId = wire.HomeId;
		SceneId = wire.SceneId;
		TriggeredAt = DateTimeOffset.FromUnixTimeMilliseconds (wire.TriggeredAt);
		Actions = actions;
		}
	public string Id
		{
		get;
		}
	public long HomeId
		{
		get;
		}
	public long SceneId
		{
		get;
		}
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
public sealed class RainPointSceneLogPage
	{
	internal RainPointSceneLogPage (long total, int page, int pageSize, IReadOnlyList<RainPointSceneLogEntry> entries)
		{
		Total = total;
		Page = page;
		PageSize = pageSize;
		Entries = entries;
		}
	public long Total
		{
		get;
		}
	public int Page
		{
		get;
		}
	public int PageSize
		{
		get;
		}
	public IReadOnlyList<RainPointSceneLogEntry> Entries
		{
		get;
		}
	public bool HasMore => Entries.Count > 0 && (long)Page * PageSize + Entries.Count < Total;
	}
public sealed partial class RainPointCloudClient
	{
	/// <summary>Reads one bounded zero-based page of scene history using UTC instants. History retention after scene deletion is controlled by the service and is not guaranteed.</summary>
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
internal sealed class SceneLogPageWire
	{
	[JsonPropertyName ("total"), JsonRequired]
	public long Total
		{
		get; set;
		}
	[JsonPropertyName ("records"), JsonRequired]
	public List<SceneLogWire>? Records
		{
		get; set;
		}
	}
internal sealed class SceneLogWire
	{
	[JsonPropertyName ("id"), JsonRequired] public string Id { get; set; } = string.Empty;
	[JsonPropertyName ("hid"), JsonRequired]
	public long HomeId
		{
		get; set;
		}
	[JsonPropertyName ("sceneMainId"), JsonRequired]
	public long SceneId
		{
		get; set;
		}
	[JsonPropertyName ("triggerTime"), JsonRequired]
	public long TriggeredAt
		{
		get; set;
		}
	[JsonPropertyName ("result")]
	public string? Result
		{
		get; set;
		}
	}
internal sealed class SceneActionResultWire
	{
	[JsonPropertyName ("aid"), JsonRequired]
	public long ActionId
		{
		get; set;
		}
	[JsonPropertyName ("mid"), JsonRequired]
	public long HubId
		{
		get; set;
		}
	[JsonPropertyName ("addr")]
	public int? Address
		{
		get; set;
		}
	[JsonPropertyName ("rt"), JsonRequired]
	public int Result
		{
		get; set;
		}
	}
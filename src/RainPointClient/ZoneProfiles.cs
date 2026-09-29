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

/// <summary>A vendor profile category and its available options. Labels can be localization keys.</summary>
public sealed class RainPointProfileCategory
	{
	/// <summary>
	/// Gets the cloud or catalog identifier for this record.
	/// </summary>
	[JsonPropertyName ("id"), JsonRequired, JsonInclude]
	public int Id
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the vendor display label, which may be a localization key.
	/// </summary>
	[JsonPropertyName ("langField"), JsonRequired, JsonInclude] public string Label { get; internal set; } = string.Empty;
	/// <summary>
	/// Gets the vendor-defined display ordering value.
	/// </summary>
	[JsonPropertyName ("sort"), JsonInclude]
	public int SortOrder
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the selectable options in this profile category.
	/// </summary>
	[JsonPropertyName ("subType"), JsonRequired, JsonInclude] public IReadOnlyList<RainPointProfileOption> Options { get; internal set; } = Array.Empty<RainPointProfileOption> ();
	}
/// <summary>
/// Describes one selectable vendor recommendation-profile option.
/// </summary>
public sealed class RainPointProfileOption
	{
	/// <summary>
	/// Gets the cloud or catalog identifier for this record.
	/// </summary>
	[JsonPropertyName ("id"), JsonRequired, JsonInclude]
	public int Id
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the vendor display label, which may be a localization key.
	/// </summary>
	[JsonPropertyName ("langField"), JsonRequired, JsonInclude] public string Label { get; internal set; } = string.Empty;
	/// <summary>
	/// Gets the vendor-defined display ordering value.
	/// </summary>
	[JsonPropertyName ("sort"), JsonInclude]
	public int SortOrder
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the vendor default-selection indicator.
	/// </summary>
	[JsonPropertyName ("defaultFlag"), JsonInclude]
	internal int DefaultFlag
		{
		get; set;
		}
	/// <summary>
	/// Gets whether the vendor marks this profile option as the default.
	/// </summary>
	[JsonIgnore] public bool IsDefault => DefaultFlag == 1;
	}
/// <summary>Saved recommendation preferences for one zone. These are not watering schedules.</summary>
public sealed class RainPointZoneProfileSnapshot
	{
	/// <summary>
	/// Stores the home id and hub id and device id for rainpoint zone profile snapshot.
	/// </summary>
	internal long HomeId, HubId, DeviceId;
	/// <summary>
	/// Stores the original encoded profile field so unrelated zone preferences can be preserved.
	/// </summary>
	internal string? Payload;
	/// <summary>
	/// Tracks whether this configuration snapshot has already been used for a write attempt.
	/// </summary>
	internal int WriteAttempted;
	/// <summary>
	/// Initializes zone profile snapshot from the supplied typed values.
	/// </summary>
	/// <param name="address">The paired child's RF address within its hub, distinct from its cloud database ID.</param>
	/// <param name="zone">The one-based zone number on the selected timer.</param>
	/// <param name="availability">The decoding result; unavailable data must not be interpreted as an empty configuration or closed valve.</param>
	/// <param name="configured">Whether a usable profile configuration was present in the response.</param>
	/// <param name="enabled">Whether the selected feature or saved plan should be enabled.</param>
	/// <param name="selection">The decoded selected profile-option IDs.</param>
	internal RainPointZoneProfileSnapshot (int address, int zone, TimerReadingAvailability availability, bool configured, bool? enabled, int[] selection)
		{
		Address = address;
		Zone = zone;
		Availability = availability;
		IsConfigured = configured;
		RecommendationsEnabled = enabled;
		SelectedOptionIds = Array.AsReadOnly (selection);
		}
	/// <summary>
	/// Gets the timer's RF address within the hub.
	/// </summary>
	public int Address
		{
		get;
		}
	/// <summary>
	/// Gets the one-based zone for these recommendation preferences.
	/// </summary>
	public int Zone
		{
		get;
		}
	/// <summary>
	/// Gets the decoding state of the recommendation-profile payload.
	/// </summary>
	public TimerReadingAvailability Availability
		{
		get;
		}
	/// <summary>False when the app's unconfigured defaults are used for an absent profile.</summary>
	public bool IsConfigured
		{
		get;
		}
	/// <summary>
	/// Gets whether cloud recommendations are enabled for the zone; this is not a saved-plan switch.
	/// </summary>
	public bool? RecommendationsEnabled
		{
		get;
		}
	/// <summary>
	/// Gets the selected vendor option IDs in the profile.
	/// </summary>
	public IReadOnlyList<int> SelectedOptionIds
		{
		get;
		}
	}
/// <summary>Cloud recommendation values, not an enabled plan or guaranteed plant requirement.</summary>
public sealed class RainPointWateringRecommendation
	{
	/// <summary>
	/// Gets the recommended interval in days; this does not create or enable a watering schedule.
	/// </summary>
	[JsonPropertyName ("day"), JsonInclude]
	public decimal? IntervalDays
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the recommended watering duration in seconds.
	/// </summary>
	[JsonPropertyName ("second"), JsonInclude]
	internal decimal? Seconds
		{
		get; set;
		}
	/// <summary>
	/// Gets the recommended watering duration as a time span.
	/// </summary>
	[JsonIgnore] public TimeSpan? Duration => Seconds.HasValue ? TimeSpan.FromSeconds ((double)Seconds.Value) : null;
	}
public sealed partial class RainPointCloudClient
	{
	private static readonly JsonSerializerOptions ProfileJson = new () { AllowDuplicateProperties = false, MaxDepth = 8 };
	[JsonUnmappedMemberHandling (JsonUnmappedMemberHandling.Disallow)]
	private sealed class ProfileRecord
		{
		/// <summary>
		/// Stores the open protocol field for profile record.
		/// </summary>
		[JsonPropertyName ("open")]
		public int Open
			{
			get; set;
			}
		/// <summary>
		/// Stores the target protocol field for profile record.
		/// </summary>
		[JsonPropertyName ("target")] public int[] Target { get; set; } = Array.Empty<int> ();
		}
	private sealed class ProfileRequest
		{
		/// <summary>
		/// Stores the mid protocol field for profile request.
		/// </summary>
		[JsonPropertyName ("mid")]
		public long HubId
			{
			get; set;
			}
		/// <summary>
		/// Stores the sid protocol field for profile request.
		/// </summary>
		[JsonPropertyName ("sid")]
		public long DeviceId
			{
			get; set;
			}
		/// <summary>
		/// Stores the planJson protocol field for profile request.
		/// </summary>
		[JsonPropertyName ("planJson")] public string Profile { get; set; } = string.Empty;
		}
	/// <summary>
	/// Lists the vendor's recommendation-profile categories and options.
	/// </summary>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the requested profile category records.</returns>
	/// <exception cref="RainPointException">Invalid profile catalog. Duplicate profile option identifier. The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task<IReadOnlyList<RainPointProfileCategory>> GetZoneProfileCatalogAsync (CancellationToken cancellationToken = default)
		{
		var categories = await GetAsync<RainPointProfileCategory[]> ("app/device/sub/getPlanType", cancellationToken).ConfigureAwait (false);
		if (categories.Length > 32 || categories.Any (c => c is null || c.Id <= 0 || string.IsNullOrWhiteSpace (c.Label) || c.Options is null || c.Options.Count > 128
		 || c.Options.Any (o => o is null || o.Id <= 0 || string.IsNullOrWhiteSpace (o.Label) || o.DefaultFlag is < 0 or > 1)
		 || c.Options.Count (o => o.IsDefault) > 1) || categories.Select (c => c.Id).Distinct ().Count () != categories.Length)
			throw new RainPointException ("Invalid profile catalog.");
		var all = categories.SelectMany (c => c.Options).ToArray ();
		if (all.Select (o => o.Id).Distinct ().Count () != all.Length)
			throw new RainPointException ("Duplicate profile option identifier.");
		foreach (var category in categories)
			category.Options = Array.AsReadOnly (category.Options.OrderBy (o => o.SortOrder).ThenBy (o => o.Id).ToArray ());
		return Array.AsReadOnly (categories.OrderBy (c => c.SortOrder).ThenBy (c => c.Id).ToArray ());
		}
	private static ProfileRecord[] ReadProfiles (string? payload)
		{
		if (payload?.Length > 16384)
			throw new JsonException ("Profile exceeds supported size.");
		ProfileRecord[]? records = string.IsNullOrEmpty (payload) ? null : JsonSerializer.Deserialize<ProfileRecord[]> (payload!, ProfileJson);
		if (records is null)
			return new[] { new ProfileRecord (), new ProfileRecord (), new ProfileRecord () };
		if (records.Length != 3 || records.Any (p => p is null || p.Open is < 0 or > 1 || p.Target is null || p.Target.Length > 32 || p.Target.Any (id => id <= 0) || p.Target.Distinct ().Count () != p.Target.Length))
			throw new JsonException ("Invalid three-zone profile.");
		return records;
		}
	/// <summary>
	/// Reads one zone's saved recommendation preferences without creating a watering plan.
	/// </summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="address">The paired child's RF address within its hub, distinct from its cloud database ID.</param>
	/// <param name="zone">The one-based zone number on the selected timer.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the typed zone profile snapshot result.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task<RainPointZoneProfileSnapshot> GetZoneProfileAsync (RainPointHub hub, int address, int zone, CancellationToken cancellationToken = default)
		{
		var device = await GetScheduleDeviceAsync (hub, address, zone, cancellationToken).ConfigureAwait (false);
		RainPointZoneProfileSnapshot result;
		try
			{
			var records = ReadProfiles (device.ProfileParameter);
			var profile = records[zone - 1];
			result = new (address, zone, TimerReadingAvailability.Decoded, !string.IsNullOrEmpty (device.ProfileParameter) && device.ProfileParameter!.Trim () != "null", profile.Open == 1, profile.Target);
			}
		catch (JsonException) { result = new (address, zone, TimerReadingAvailability.Malformed, true, null, Array.Empty<int> ()); }
		result.HomeId = hub.HomeId;
		result.HubId = hub.Id;
		result.DeviceId = device.Id!.Value;
		result.Payload = device.ProfileParameter;
		return result;
		}
	/// <summary>Updates recommendation preferences for one zone, preserving other zones. Does not create or enable a saved plan.</summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="expected">An unused, current zone profile snapshot observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="recommendationsEnabled">Whether the zone's cloud recommendation preferences should be enabled; this does not enable a plan.</param>
	/// <param name="selectedOptionIds">The selected profile-option IDs from the vendor catalog.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="System.ArgumentNullException">A required argument is null.</exception>
	/// <exception cref="System.ArgumentException">Use a readable profile snapshot for this hub. Select distinct valid profile options. Choose at most one current option from each profile category.</exception>
	/// <exception cref="System.InvalidOperationException">Read the profile again before another write. Profile changed. Read again before writing. Read again; an earlier write was attempted.</exception>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task SetZoneProfileAsync (RainPointHub hub, RainPointZoneProfileSnapshot expected, bool recommendationsEnabled, IReadOnlyList<int> selectedOptionIds, CancellationToken cancellationToken = default)
		{
		ValidateHub (hub);
		if (expected is null)
			throw new ArgumentNullException (nameof (expected));
		if (selectedOptionIds is null)
			throw new ArgumentNullException (nameof (selectedOptionIds));
		if (expected.HomeId != hub.HomeId || expected.HubId != hub.Id || expected.DeviceId <= 0 || expected.Availability != TimerReadingAvailability.Decoded)
			throw new ArgumentException ("Use a readable profile snapshot for this hub.", nameof (expected));
		if (selectedOptionIds.Count > 32 || selectedOptionIds.Any (id => id <= 0) || selectedOptionIds.Distinct ().Count () != selectedOptionIds.Count)
			throw new ArgumentException ("Select distinct valid profile options.", nameof (selectedOptionIds));
		int[] selection = selectedOptionIds.ToArray ();
		if (Volatile.Read (ref expected.WriteAttempted) != 0)
			throw new InvalidOperationException ("Read the profile again before another write.");
		var catalog = await GetZoneProfileCatalogAsync (cancellationToken).ConfigureAwait (false);
		if (selection.Any (id => !catalog.Any (c => c.Options.Any (o => o.Id == id))) || catalog.Any (c => c.Options.Count (o => selection.Contains (o.Id)) > 1))
			throw new ArgumentException ("Choose at most one current option from each profile category.", nameof (selectedOptionIds));
		var fresh = await GetScheduleDeviceAsync (hub, expected.Address, expected.Zone, cancellationToken).ConfigureAwait (false);
		if (fresh.Id != expected.DeviceId || fresh.PortNumber != 3 || fresh.ProfileParameter != expected.Payload)
			throw new InvalidOperationException ("Profile changed. Read again before writing.");
		var records = ReadProfiles (expected.Payload);
		records[expected.Zone - 1] = new ProfileRecord { Open = recommendationsEnabled ? 1 : 0, Target = selection };
		cancellationToken.ThrowIfCancellationRequested ();
		if (Interlocked.CompareExchange (ref expected.WriteAttempted, 1, 0) != 0)
			throw new InvalidOperationException ("Read again; an earlier write was attempted.");
		Session session = GetSession ();
		ApiResult response = await SendAsync<ProfileRequest, ApiResult> (HttpMethod.Post, "app/device/sub/update",
		 new ProfileRequest { HubId = hub.Id, DeviceId = fresh.Id!.Value, Profile = JsonSerializer.Serialize (records, ProfileJson) }, session, cancellationToken).ConfigureAwait (false);
		CheckResult (response, session);
		}
	/// <summary>
	/// Reads cloud watering recommendations for a zone without applying them or enabling watering.
	/// </summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="address">The paired child's RF address within its hub, distinct from its cloud database ID.</param>
	/// <param name="zone">The one-based zone number on the selected timer.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the requested watering recommendation records.</returns>
	/// <exception cref="RainPointException">Invalid watering recommendation. The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task<IReadOnlyList<RainPointWateringRecommendation>> GetZoneRecommendationsAsync (RainPointHub hub, int address, int zone, CancellationToken cancellationToken = default)
		{
		var device = await GetScheduleDeviceAsync (hub, address, zone, cancellationToken).ConfigureAwait (false);
		string query = "app/device/getPlanValueByDevice?mid=" + hub.Id.ToString (CultureInfo.InvariantCulture) + "&sid=" + device.Id!.Value.ToString (CultureInfo.InvariantCulture) + "&port=" + zone.ToString (CultureInfo.InvariantCulture);
		var recommendations = await GetAsync<RainPointWateringRecommendation[]> (query, cancellationToken).ConfigureAwait (false);
		if (recommendations.Length > 32 || recommendations.Any (r => r is null || r.IntervalDays < 0 || r.IntervalDays > 366 || r.Seconds < 0 || r.Seconds > 86400 * 366m))
			throw new RainPointException ("Invalid watering recommendation.");
		return Array.AsReadOnly (recommendations);
		}
	}
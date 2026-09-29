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

/// <summary>
/// Selects an optional daylight or nighttime execution window for a smart scene.
/// </summary>
public enum RainPointSceneSolarPeriod
	{
	/// <summary>Do not restrict execution by the daylight window.</summary>
	None,
	/// <summary>Restrict execution to the vendor-defined daytime window.</summary>
	Daytime,
	/// <summary>Restrict execution to the vendor-defined nighttime window.</summary>
	Nighttime
	}

/// <summary>A complete scene definition. Saving can activate future automation immediately; there is no disabled-draft guarantee.</summary>
public sealed class RainPointSceneDraft
	{
	/// <summary>
	/// Gets or sets the required scene display name.
	/// </summary>
	public string Name { get; set; } = string.Empty;
	/// <summary>
	/// Gets or sets an optional daylight or nighttime effective window, mutually exclusive with clock boundaries.
	/// </summary>
	public RainPointSceneSolarPeriod SolarPeriod
		{
		get; set;
		}
	/// <summary>
	/// Gets or sets the local-date recurrence of the scene's effective period.
	/// </summary>
	public RainPointSceneRepeat EffectiveRepeat
		{
		get; set;
		}
	/// <summary>
	/// Gets or sets the weekday mask, used only with a weekday effective recurrence.
	/// </summary>
	public RainPointSceneWeekdays EffectiveWeekdays
		{
		get; set;
		}
	/// <summary>
	/// Gets or sets whether every condition must match; false permits any matching condition.
	/// </summary>
	public bool MatchAll
		{
		get; set;
		}
	/// <summary>
	/// Gets or sets the vendor daily execution-frequency value, from 0 through 30; defaults to 1.
	/// </summary>
	public int MaximumRunsPerDay { get; set; } = 1;
	/// <summary>
	/// Gets or sets the minimum interval between executions, from 1 through 1440 minutes; defaults to 120.
	/// </summary>
	public int MinimumIntervalMinutes { get; set; } = 120;
	/// <summary>
	/// Gets or sets one to five supported, enabled conditions to save.
	/// </summary>
	public IReadOnlyList<RainPointSceneCondition> Conditions { get; set; } = Array.Empty<RainPointSceneCondition> ();
	/// <summary>
	/// Gets or sets one to five supported, enabled actions to save.
	/// </summary>
	public IReadOnlyList<RainPointSceneAction> Actions { get; set; } = Array.Empty<RainPointSceneAction> ();
	/// <summary>Optional inclusive home-local date, at midnight with Unspecified kind.</summary>
	public DateTime? StartsOn
		{
		get; set;
		}
	/// <summary>
	/// Gets or sets the optional inclusive final home-local date, at midnight with Unspecified kind.
	/// </summary>
	public DateTime? EndsOn
		{
		get; set;
		}
	/// <summary>Optional daily clock window. Both boundaries are required; time-triggered conditions use their own time instead.</summary>
	public TimeSpan? WindowStartsAt
		{
		get; set;
		}
	/// <summary>
	/// Gets or sets the final clock boundary of the effective window; both boundaries are required.
	/// </summary>
	public TimeSpan? WindowEndsAt
		{
		get; set;
		}
	/// <summary>
	/// Validates the complete scene definition and creates fresh wire copies without retained child IDs.
	/// </summary>
	/// <param name="hubId">The positive cloud hub identifier, distinct from child RF addresses.</param>
	/// <returns>Fresh attributed wire models representing the validated complete scene definition.</returns>
	/// <exception cref="System.ArgumentException">A scene name is required. Use one to five conditions and actions. Only supported, enabled conditions and actions can be saved. Solar periods cannot also contain clock boundaries. An all-conditions scene cannot require multiple time triggers. Effective windows need both boundaries and cannot accompany a time trigger. The effective date range is reversed. Duplicate rain-delay actions target the same zone.</exception>
	/// <exception cref="System.ArgumentOutOfRangeException">An argument is outside the supported range described above.</exception>
	/// <exception cref="System.ArgumentNullException">A required argument is null.</exception>
	internal SceneWire Encode (long hubId)
		{
		if (string.IsNullOrWhiteSpace (Name))
			throw new ArgumentException ("A scene name is required.");
		if (MaximumRunsPerDay is < 0 or > 30 || MinimumIntervalMinutes is < 1 or > 1440)
			throw new ArgumentOutOfRangeException (nameof (MaximumRunsPerDay));
		var conditions = Conditions?.ToArray () ?? throw new ArgumentNullException (nameof (Conditions));
		var actions = Actions?.ToArray () ?? throw new ArgumentNullException (nameof (Actions));
		if (conditions.Length is < 1 or > 5 || actions.Length is < 1 or > 5)
			throw new ArgumentException ("Use one to five conditions and actions.");
		if (conditions.Any (c => c is null || c.Kind == RainPointSceneConditionKind.Unsupported || c.Enabled != true) || actions.Any (a => a is null || a.Kind == RainPointSceneActionKind.Unsupported || a.Enabled != true))
			throw new ArgumentException ("Only supported, enabled conditions and actions can be saved.");
		if (!Enum.IsDefined (typeof (RainPointSceneSolarPeriod), SolarPeriod))
			throw new ArgumentOutOfRangeException (nameof (SolarPeriod));
		if (SolarPeriod != RainPointSceneSolarPeriod.None && (WindowStartsAt.HasValue || WindowEndsAt.HasValue))
			throw new ArgumentException ("Solar periods cannot also contain clock boundaries.");
		int timeCount = conditions.Count (c => c.Wire.Type == 2);
		if (MatchAll && timeCount > 1)
			throw new ArgumentException ("An all-conditions scene cannot require multiple time triggers.");
		if (WindowStartsAt.HasValue != WindowEndsAt.HasValue || timeCount > 0 && (WindowStartsAt.HasValue || SolarPeriod != RainPointSceneSolarPeriod.None))
			throw new ArgumentException ("Effective windows need both boundaries and cannot accompany a time trigger.");
		if (StartsOn > EndsOn)
			throw new ArgumentException ("The effective date range is reversed.");
		if (actions.Where (a => a.Kind == RainPointSceneActionKind.RainDelay).GroupBy (a => (a.Wire.HubId, a.Wire.Address, a.Wire.Value)).Any (g => g.Count () > 1))
			throw new ArgumentException ("Duplicate rain-delay actions target the same zone.");
		// Fresh attributed copies exclude existing child IDs; a replacement explicitly replaces the complete definition.
		var request = new SceneWire
			{
			Name = Name,
			Executant = hubId,
			Flags = conditions.Length | actions.Length << 3 | (MatchAll ? 128 : 0),
			Frequency = MaximumRunsPerDay,
			Interval = MinimumIntervalMinutes,
			StartDate = EncodeDate (StartsOn),
			EndDate = EncodeDate (EndsOn),
			StartTime = SolarPeriod == RainPointSceneSolarPeriod.Daytime ? 0x4000 : SolarPeriod == RainPointSceneSolarPeriod.Nighttime ? 0x4001 : EncodeTime (WindowStartsAt),
			EndTime = SolarPeriod == RainPointSceneSolarPeriod.Daytime ? 0x4001 : SolarPeriod == RainPointSceneSolarPeriod.Nighttime ? 0x4000 : EncodeTime (WindowEndsAt),
			DateRepeat = SceneEncoding.Repeat (EffectiveRepeat, EffectiveWeekdays),
			Conditions = conditions.Select (c => JsonSerializer.Deserialize<SceneConditionWire> (JsonSerializer.Serialize (c.Wire))!).ToList (),
			Actions = actions.Select (a => JsonSerializer.Deserialize<SceneActionWire> (JsonSerializer.Serialize (a.Wire))!).ToList ()
			};
		foreach (var c in request.Conditions)
			c.Id = null;
		foreach (var a in request.Actions)
			a.Id = null;
		return request;
		}
	private static int EncodeDate (DateTime? value)
		{
		if (!value.HasValue)
			return 65535;
		DateTime date = value.Value;
		if (date.Kind != DateTimeKind.Unspecified || date.TimeOfDay != TimeSpan.Zero || date.Year is < 2020 or > 2083)
			throw new ArgumentException ("Use a home-local midnight date in 2020..2083.");
		return (date.Year - 2020) << 9 | date.Month << 5 | date.Day;
		}
	private static int EncodeTime (TimeSpan? value)
		{
		if (!value.HasValue)
			return 65535;
		if (value.Value < TimeSpan.Zero || value.Value >= TimeSpan.FromDays (1) || value.Value.Ticks % TimeSpan.TicksPerMinute != 0)
			throw new ArgumentException ("Use a whole-minute time within a day.");
		return value.Value.Hours << 6 | value.Value.Minutes;
		}
	}
public sealed partial class RainPointCloudClient
	{
	/// <summary>Reads devices and their product-catalog scene capabilities. Plain discovery does not supply the catalog flags.</summary>
	/// <param name="homeId">The positive cloud home identifier.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the requested hub records.</returns>
	/// <exception cref="System.InvalidOperationException">Session changed during capability discovery.</exception>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task<IReadOnlyList<RainPointHub>> GetSceneDevicesAsync (long homeId, CancellationToken cancellationToken = default)
		{
		Session session = GetSession ();
		var hubs = await GetHubsAsync (homeId, cancellationToken).ConfigureAwait (false);
		var catalog = await GetProductCatalogAsync (cancellationToken).ConfigureAwait (false);
		foreach (var hub in hubs)
			{
			var models = catalog.Models.Where (m => m.ModelCode == hub.ModelCode && m.Model == hub.Model).ToArray ();
			hub.AdvertisedSceneFlags = models.Length == 1 ? models[0].SceneSupport : null;
			foreach (var device in hub.Devices)
				{
				models = catalog.Models.Where (m => m.ModelCode == device.ModelCode && m.Model == device.Model).ToArray ();
				device.AdvertisedSceneFlags = models.Length == 1 ? models[0].SceneSupport : null;
				}
			}
		if (!ReferenceEquals (session, GetSession ()))
			throw new InvalidOperationException ("Session changed during capability discovery.");
		return hubs;
		}

	/// <summary>Saves a new scene which may be active immediately. This can cause future watering, delays or notifications. Discover again after any attempt; never retry an uncertain creation blindly.</summary>
	/// <param name="executingHub">A discovered hub with reported scene-execution capability.</param>
	/// <param name="draft">The complete scene definition to save; saving can activate future automation immediately.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="System.ArgumentNullException">A required argument is null.</exception>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task CreateSceneAsync (RainPointHub executingHub, RainPointSceneDraft draft, CancellationToken cancellationToken = default)
		{
		if (executingHub is null)
			throw new ArgumentNullException (nameof (executingHub));
		if (draft is null)
			throw new ArgumentNullException (nameof (draft));
		var request = draft.Encode (executingHub.Id);
		Session session = GetSession ();
		await ValidateSceneTargetsAsync (executingHub, request, session, cancellationToken).ConfigureAwait (false);
		var result = await SendAsync<SceneWire, ApiResult> (HttpMethod.Post, "app/scene/2.0.5/save", request, session, cancellationToken, executingHub.HomeId).ConfigureAwait (false);
		CheckResult (result, session);
		}
	/// <summary>Replaces the entire scene definition, including effective dates and conditions. Saving can activate automation; no automatic replay occurs.</summary>
	/// <param name="expected">An unused, current scene observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="executingHub">A discovered hub with reported scene-execution capability.</param>
	/// <param name="replacement">The complete supported replacement definition, including all intended conditions and actions.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="System.ArgumentNullException">A required argument is null.</exception>
	/// <exception cref="System.ArgumentException">The scene and executing hub must belong to the same home.</exception>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task ReplaceSceneAsync (RainPointScene expected, RainPointHub executingHub, RainPointSceneDraft replacement, CancellationToken cancellationToken = default)
		{
		if (expected is null || executingHub is null || replacement is null)
			throw new ArgumentNullException (nameof (expected));
		if (expected.HomeId != executingHub.HomeId)
			throw new ArgumentException ("The scene and executing hub must belong to the same home.");
		var request = replacement.Encode (executingHub.Id);
		request.Id = expected.Id;
		await ValidateSceneTargetsAsync (executingHub, request, GetSession (), cancellationToken).ConfigureAwait (false);
		await WriteSceneAsync (expected, "save", request, cancellationToken).ConfigureAwait (false);
		}
	private async Task ValidateSceneTargetsAsync (RainPointHub expectedHub, SceneWire request, Session session, CancellationToken token)
		{
		var hubs = await GetSceneDevicesAsync (expectedHub.HomeId, token).ConfigureAwait (false);
		var hub = hubs.SingleOrDefault (h => h.Id == expectedHub.Id);
		if (hub is null || hub.DeviceName != expectedHub.DeviceName || hub.ProductKey != expectedHub.ProductKey || hub.SupportsSceneExecution != true)
			throw new InvalidOperationException ("The executing hub changed or does not advertise scene execution support.");
		var members = request.Actions!.Any (a => a.Type == 1) ? await GetMembersAsync (hub.HomeId, token).ConfigureAwait (false) : Array.Empty<RainPointMember> ();
		foreach (var action in request.Actions!)
			{
			if (action.Type == 1)
				{
				foreach (string recipient in action.Parameter!.Split ('|'))
					if (long.TryParse (recipient, NumberStyles.None, CultureInfo.InvariantCulture, out long id) && !members.Any (m => m.Id == id))
						throw new InvalidOperationException ("A notification recipient is no longer a home member.");
				}
			else
				{
				var targetHub = hubs.SingleOrDefault (h => h.Id == action.HubId);
				var device = targetHub?.Devices.SingleOrDefault (d => d.Address == action.Address);
				if (device?.SupportedZoneCount is not int count || device.ModelCode != action.ModelCode || device.SupportsSceneActions != true || !int.TryParse (action.Value, out int zone) || zone < 1 || zone > count)
					throw new InvalidOperationException ("A scene action target changed or does not advertise this capability.");
				}
			}
		if (request.Conditions!.Any (c => c.Type == 1 && c.Code == 3))
			{
			var options = await GetHomeOptionsAsync (token).ConfigureAwait (false);
			foreach (var condition in request.Conditions!.Where (c => c.Type == 1 && c.Code == 3))
				if (new RainPointSceneCondition (condition).WeatherTypeCodes.Any (code => !options.WeatherTypes.Any (w => w.Code == code)))
					throw new InvalidOperationException ("A weather type is no longer in the vendor catalog.");
			}
		if (request.StartTime is 0x4000 or 0x4001 || request.Conditions!.Any (c => c.Type == 1 || c.Type == 2 && c.Code == 2 && new RainPointSceneCondition (c).Time is RainPointSceneTime.Sunrise or RainPointSceneTime.Sunset))
			{
			var home = await GetHomeAsync (hub.HomeId, token).ConfigureAwait (false);
			if (!home.Latitude.HasValue || !home.Longitude.HasValue || home.Latitude == 0 && home.Longitude == 0)
				throw new InvalidOperationException ("Weather and solar conditions require a configured home location.");
			}
		if (!ReferenceEquals (session, GetSession ()))
			throw new InvalidOperationException ("Session changed before the scene write.");
		}
	}
/// <summary>
/// Internal scene function representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class SceneFunction
	{
	/// <summary>
	/// Stores the original bit field, including unknown bits.
	/// </summary>
	[JsonPropertyName ("SM")]
	public int? Flags
		{
		get; set;
		}
	/// <summary>
	/// Reads one scene-capability bit, preferring decoded function data and retaining unknown capability state.
	/// </summary>
	/// <param name="function">The reported encoded scene-capability field, or null when absent.</param>
	/// <param name="advertised">Catalog scene-capability flags, or null when absent.</param>
	/// <param name="bit">The capability bit being queried.</param>
	/// <returns>True or false for a recognized capability bit, or null when capability data is unknown.</returns>
	internal static bool? Supports (string? function, int? advertised, int bit)
		{
		if (advertised.HasValue && (advertised.Value & (1 << bit)) == 0)
			return false;
		if (string.IsNullOrEmpty (function) || !advertised.HasValue)
			return null;
		try
			{
			int? flags = JsonSerializer.Deserialize<SceneFunction> (function!, new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowReadingFromString })?.Flags;
			return flags.HasValue ? (flags.Value & advertised.Value & (1 << bit)) != 0 : null;
			}
		catch (JsonException) { return null; }
		}
	}
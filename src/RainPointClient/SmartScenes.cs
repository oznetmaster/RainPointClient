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

[Flags]
public enum RainPointSceneWeekdays
	{
	None = 0, Sunday = 1, Monday = 2, Tuesday = 4, Wednesday = 8, Thursday = 16, Friday = 32, Saturday = 64, EveryDay = 127
	}
public enum RainPointSceneComparison
	{
	LessThan = 0, Equal = 1, GreaterThan = 2
	}
public enum RainPointSceneWeatherMetric
	{
	RainProbabilityPercent = 4, TemperatureCelsius = 5, HumidityPercent = 6, WindSpeedKilometresPerHour = 7
	}
public enum RainPointSceneConditionKind
	{
	Unsupported, WeatherThreshold, Once, RepeatingTime, WeatherTypes
	}
public enum RainPointSceneActionKind
	{
	Unsupported, Notification, RainDelay
	}
public enum RainPointSceneRepeat
	{
	Daily, OddDates, EvenDates, Weekdays
	}
public enum RainPointSceneTime
	{
	Clock, Sunrise, Sunset
	}

/// <summary>An immutable condition. Unsupported vendor conditions are identified without exposing their encoded payload.</summary>
public sealed class RainPointSceneCondition
	{
	internal SceneConditionWire Wire
		{
		get;
		}
	internal RainPointSceneCondition (SceneConditionWire wire)
		{
		Wire = wire;
		if (wire.Enable is not (0 or 1))
			return;
		if (wire.Type == 1 && wire.Code == 3 && wire.Contrast == 1 && wire.DataType == 3 && SceneEncoding.TryNumber (wire.Value1, 4, out uint mask) && mask != 0)
			{
			WeatherTypeCodes = Array.AsReadOnly (Enumerable.Range (0, 32).Where (i => (mask & (1u << i)) != 0).ToArray ());
			Kind = RainPointSceneConditionKind.WeatherTypes;
			}
		else if (wire.Type == 1 && wire.Code is >= 4 and <= 7 && wire.DataType == (wire.Code == 5 ? 4 : 0) && wire.Contrast is >= 0 and <= 2 && SceneEncoding.TryNumber (wire.Value1, 4, out uint raw))
			{
			Metric = (RainPointSceneWeatherMetric)wire.Code;
			Threshold = wire.Code == 5 ? (unchecked((int)raw) / 10m - 32m) * 5m / 9m : wire.Code == 7 ? raw / 10m : raw;
			Comparison = (RainPointSceneComparison)wire.Contrast;
			Kind = RainPointSceneConditionKind.WeatherThreshold;
			}
		else if (wire.Type == 2 && wire.DataType == 0 && wire.Code == 1 && SceneEncoding.TryNumber (wire.Value1, 4, out raw))
			{
			try
				{
				AtLocal = new DateTime (2020 + (int)(raw >> 26), (int)(raw >> 22 & 15), (int)(raw >> 17 & 31), (int)(raw >> 12 & 31), (int)(raw >> 6 & 63), (int)(raw & 63), DateTimeKind.Unspecified);
				Kind = RainPointSceneConditionKind.Once;
				}
			catch (ArgumentOutOfRangeException) { }
			}
		else if (wire.Type == 2 && wire.DataType == 0 && wire.Code == 2 && SceneEncoding.TryNumber (wire.Value1, 4, out raw) && (raw >> 24) == 0)
			{
			int repeat = (int)(raw & 255), time = (int)(raw >> 8 & 65535);
			if (repeat is 0 or 1 or 2 || repeat is >= 129 and <= 255)
				{
				Repeat = repeat >= 128 ? RainPointSceneRepeat.Weekdays : (RainPointSceneRepeat)repeat;
				Weekdays = repeat >= 128 ? (RainPointSceneWeekdays)(repeat & 127) : RainPointSceneWeekdays.None;
				Time = time == 0x4000 ? RainPointSceneTime.Sunrise : time == 0x4001 ? RainPointSceneTime.Sunset : RainPointSceneTime.Clock;
				if (Time != RainPointSceneTime.Clock || time >> 6 < 24 && (time & 63) < 60)
					{
					if (Time == RainPointSceneTime.Clock)
						ClockTime = new TimeSpan (time >> 6, time & 63, 0);
					Kind = RainPointSceneConditionKind.RepeatingTime;
					}
				}
			}
		}
	public RainPointSceneConditionKind Kind
		{
		get;
		}
	public bool? Enabled => Wire.Enable is 0 or 1 ? Wire.Enable == 1 : null;
	public long? HubId => Wire.HubId > 0 ? Wire.HubId : null;
	public int? DeviceAddress => Wire.Type == 0 ? Wire.Address : null;
	public RainPointSceneWeatherMetric? Metric
		{
		get;
		}
	public RainPointSceneComparison? Comparison
		{
		get;
		}
	public decimal? Threshold
		{
		get;
		}
	/// <summary>Home-local wall time; no conversion using the computer's time zone is made.</summary>
	public DateTime? AtLocal
		{
		get;
		}
	public RainPointSceneRepeat? Repeat
		{
		get;
		}
	public RainPointSceneWeekdays Weekdays
		{
		get;
		}
	public RainPointSceneTime? Time
		{
		get;
		}
	public TimeSpan? ClockTime
		{
		get;
		}
	public IReadOnlyList<int> WeatherTypeCodes { get; } = Array.Empty<int> ();
	public string Description => Kind switch
		{
			RainPointSceneConditionKind.WeatherTypes => "Weather types: " + string.Join (", ", WeatherTypeCodes),
			RainPointSceneConditionKind.WeatherThreshold => $"{Metric} {Comparison} {Threshold:0.##}",
			RainPointSceneConditionKind.Once => $"Once: {AtLocal:yyyy-MM-dd HH:mm} (home time)",
			RainPointSceneConditionKind.RepeatingTime => $"{Repeat} {Weekdays}: {(Time == RainPointSceneTime.Clock ? ClockTime.ToString () : Time.ToString ())}",
			_ => "Unsupported condition; use the vendor app to inspect it."
			};
	public static RainPointSceneCondition WeatherTypes (IReadOnlyList<RainPointWeatherType> types)
		{
		if (types is null || types.Count == 0 || types.Any (t => t is null || t.Code is < 0 or > 31) || types.Select (t => t.Code).Distinct ().Count () != types.Count)
			throw new ArgumentException ("Select distinct types from the weather catalog.", nameof (types));
		uint value = 0;
		foreach (var type in types)
			value |= 1u << type.Code;
		return new (new ()
			{
			Enable = 1,
			Type = 1,
			Code = 3,
			Contrast = 1,
			DataType = 3,
			Value1 = SceneEncoding.Hex (value, 4)
			});
		}
	public static RainPointSceneCondition Weather (RainPointSceneWeatherMetric metric, RainPointSceneComparison comparison, decimal threshold)
		{
		if (!Enum.IsDefined (typeof (RainPointSceneWeatherMetric), metric))
			throw new ArgumentOutOfRangeException (nameof (metric));
		if (!Enum.IsDefined (typeof (RainPointSceneComparison), comparison))
			throw new ArgumentOutOfRangeException (nameof (comparison));
		decimal min = metric == RainPointSceneWeatherMetric.TemperatureCelsius ? -40 : 0;
		decimal max = metric == RainPointSceneWeatherMetric.TemperatureCelsius ? 60 : metric == RainPointSceneWeatherMetric.WindSpeedKilometresPerHour ? 150 : 100;
		decimal step = metric == RainPointSceneWeatherMetric.TemperatureCelsius ? 0.1m : 1m;
		if (threshold < min || threshold > max || threshold % step != 0)
			throw new ArgumentOutOfRangeException (nameof (threshold));
		int value = metric == RainPointSceneWeatherMetric.TemperatureCelsius ? (int)Math.Floor ((threshold * 1.8m + 32) * 10 + 0.5m) : metric == RainPointSceneWeatherMetric.WindSpeedKilometresPerHour ? (int)(threshold * 10) : (int)threshold;
		return new (new ()
			{
			Enable = 1,
			Type = 1,
			Code = (int)metric,
			Contrast = (int)comparison,
			Value1 = SceneEncoding.Hex (unchecked((uint)value), 4),
			DataType = metric == RainPointSceneWeatherMetric.TemperatureCelsius ? 4 : 0
			});
		}
	public static RainPointSceneCondition Once (DateTime homeLocal)
		{
		if (homeLocal.Kind != DateTimeKind.Unspecified || homeLocal.Year is < 2020 or > 2083 || homeLocal.Second != 0 || homeLocal.Ticks % TimeSpan.TicksPerSecond != 0)
			throw new ArgumentException ("Use a whole minute in home-local time, 2020..2083.", nameof (homeLocal));
		uint value = (uint)((homeLocal.Year - 2020) << 26 | homeLocal.Month << 22 | homeLocal.Day << 17 | homeLocal.Hour << 12 | homeLocal.Minute << 6);
		return new (new ()
			{
			Enable = 1,
			Type = 2,
			Code = 1,
			Contrast = 1,
			DataType = 0,
			Value1 = SceneEncoding.Hex (value, 4)
			});
		}
	public static RainPointSceneCondition Repeating (RainPointSceneRepeat repeat, RainPointSceneTime time, TimeSpan? clockTime = null, RainPointSceneWeekdays weekdays = RainPointSceneWeekdays.None)
		{
		if (!Enum.IsDefined (typeof (RainPointSceneRepeat), repeat) || !Enum.IsDefined (typeof (RainPointSceneTime), time))
			throw new ArgumentOutOfRangeException (nameof (repeat));
		if ((repeat == RainPointSceneRepeat.Weekdays && ((int)weekdays < 1 || (int)weekdays > 127)) || (repeat != RainPointSceneRepeat.Weekdays && weekdays != RainPointSceneWeekdays.None))
			throw new ArgumentException ("Specify weekdays only for a weekday recurrence.");
		if (time == RainPointSceneTime.Clock ? !clockTime.HasValue || clockTime.Value < TimeSpan.Zero || clockTime.Value >= TimeSpan.FromDays (1) || clockTime.Value.Ticks % TimeSpan.TicksPerMinute != 0 : clockTime.HasValue)
			throw new ArgumentException ("Specify a whole-minute clock time only for a clock condition.");
		int recurrence = repeat == RainPointSceneRepeat.Weekdays ? 128 | (int)weekdays : (int)repeat;
		int encodedTime = time == RainPointSceneTime.Sunrise ? 0x4000 : time == RainPointSceneTime.Sunset ? 0x4001 : clockTime!.Value.Hours << 6 | clockTime.Value.Minutes;
		return new (new ()
			{
			Enable = 1,
			Type = 2,
			Code = 2,
			DataType = 0,
			Value1 = SceneEncoding.Hex ((uint)(recurrence | encodedTime << 8), 4)
			});
		}
	}
/// <summary>An immutable action. Creating a scene containing notifications can send messages when it executes.</summary>
public sealed class RainPointSceneAction
	{
	internal SceneActionWire Wire
		{
		get;
		}
	internal RainPointSceneAction (SceneActionWire wire)
		{
		Wire = wire;
		if (wire.Enable is not (0 or 1))
			return;
		if (wire.Type == 1 && wire.Code == 1)
			{
			var entries = (wire.Parameter ?? string.Empty).Split ('|');
			if (entries.All (e => !string.IsNullOrWhiteSpace (e)))
				{
				Recipients = Array.AsReadOnly (entries);
				Message = wire.Value;
				Kind = RainPointSceneActionKind.Notification;
				}
			}
		else if (wire.Type == 2 && wire.Code == 255 && int.TryParse (wire.Parameter, NumberStyles.None, CultureInfo.InvariantCulture, out int hours) && hours is >= 24 and <= 720 && hours % 24 == 0)
			{
			var zones = (wire.Value ?? string.Empty).Split (',');
			var parsed = new List<int> ();
			foreach (string zone in zones)
				{
				if (!int.TryParse (zone, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n is < 1 or > 16 || parsed.Contains (n))
					return;
				parsed.Add (n);
				}
			Zones = Array.AsReadOnly (parsed.ToArray ());
			RainDelayDays = hours / 24;
			Kind = RainPointSceneActionKind.RainDelay;
			}
		}
	public RainPointSceneActionKind Kind
		{
		get;
		}
	public bool? Enabled => Wire.Enable is 0 or 1 ? Wire.Enable == 1 : null;
	public long? HubId => Wire.HubId > 0 ? Wire.HubId : null;
	public int? DeviceAddress => Wire.Type is 0 or 2 ? Wire.Address : null;
	public string? Message
		{
		get;
		}
	/// <summary>Member identifiers or email addresses, never a protocol payload.</summary>
	public IReadOnlyList<string> Recipients { get; } = Array.Empty<string> ();
	public IReadOnlyList<int> Zones { get; } = Array.Empty<int> ();
	public int? RainDelayDays
		{
		get;
		}
	public string Description => Kind switch { RainPointSceneActionKind.Notification => $"Notify {Recipients.Count} recipient(s): {Message}", RainPointSceneActionKind.RainDelay => $"Rain delay {RainDelayDays} days; zones {string.Join (", ", Zones)}", _ => "Unsupported device action; use the vendor app to inspect it." };
	public static RainPointSceneAction Notify (string message, IReadOnlyList<long> memberIds, IReadOnlyList<string> emailAddresses)
		{
		if (string.IsNullOrWhiteSpace (message) || message.Length > 100)
			throw new ArgumentException ("Use a message of 1..100 characters.", nameof (message));
		if (memberIds is null || emailAddresses is null)
			throw new ArgumentNullException (nameof (memberIds));
		long[] ids = memberIds.ToArray ();
		string[] emails = emailAddresses.ToArray ();
		if (ids.Any (n => n <= 0) || ids.Distinct ().Count () != ids.Length || emails.Length > 5 || emails.Distinct (StringComparer.OrdinalIgnoreCase).Count () != emails.Length)
			throw new ArgumentException ("Recipients must be distinct; at most five email addresses are supported.");
		foreach (string email in emails)
			if (string.IsNullOrWhiteSpace (email) || email.IndexOf ('|') >= 0 || new System.Net.Mail.MailAddress (email).Address != email)
				throw new ArgumentException ("Use plain email addresses.");
		string recipients = string.Join ("|", ids.Select (n => n.ToString (CultureInfo.InvariantCulture)).Concat (emails));
		if (recipients.Length is < 1 or > 200)
			throw new ArgumentException ("At least one recipient is required; the combined recipient list is limited to 200 characters.");
		return new (new ()
			{
			Enable = 1,
			Type = 1,
			Code = 1,
			Value = message,
			Parameter = recipients
			});
		}
	public static RainPointSceneAction RainDelay (RainPointHub hub, int address, int zone, int days)
		{
		if (hub is null)
			throw new ArgumentNullException (nameof (hub));
		var device = hub.Devices.SingleOrDefault (d => d.Address == address);
		if (device?.SupportedZoneCount is not int count || !device.ModelCode.HasValue)
			throw new ArgumentException ("Select a supported discovered timer.");
		if (zone < 1 || zone > count)
			throw new ArgumentOutOfRangeException (nameof (zone));
		if (days is < 1 or > 30)
			throw new ArgumentOutOfRangeException (nameof (days));
		return new (new ()
			{
			Enable = 1,
			Type = 2,
			Code = 255,
			HubId = hub.Id,
			Address = address,
			ModelCode = device.ModelCode.Value,
			Value = zone.ToString (CultureInfo.InvariantCulture),
			Parameter = (days * 24).ToString (CultureInfo.InvariantCulture)
			});
		}
	}
public sealed class RainPointSceneSummary
	{
	internal RainPointSceneSummary (SceneWire wire)
		{
		Id = wire.Id ?? 0;
		Name = wire.Name ?? string.Empty;
		Enabled = wire.Open is 0 or 1 ? wire.Open == 1 : null;
		Available = wire.Enable is 0 or 1 ? wire.Enable == 1 : null;
		}
	public long Id
		{
		get;
		}
	public string Name
		{
		get;
		}
	public bool? Enabled
		{
		get;
		}
	public bool? Available
		{
		get;
		}
	}
/// <summary>A scene observation. Enabling can start future automation; disabling does not stop an already running valve.</summary>
public sealed class RainPointScene
	{
	internal RainPointScene (long homeId, SceneWire wire, object session)
		{
		HomeId = homeId;
		Wire = wire;
		Session = session;
		Conditions = Array.AsReadOnly ((wire.Conditions ?? []).Select (c => new RainPointSceneCondition (c)).ToArray ());
		Actions = Array.AsReadOnly ((wire.Actions ?? []).Select (a => new RainPointSceneAction (a)).ToArray ());
		}
	internal SceneWire Wire
		{
		get;
		}
	internal object Session
		{
		get;
		}
	internal int Attempted;
	public long HomeId
		{
		get;
		}
	public long Id => Wire.Id!.Value;
	public string Name => Wire.Name ?? string.Empty;
	public long? ExecutingHubId => Wire.Executant;
	public bool? Enabled => Wire.Open is 0 or 1 ? Wire.Open == 1 : null;
	public bool? Available => Wire.Enable is 0 or 1 ? Wire.Enable == 1 : null;
	public bool? MatchAll => Wire.Flags.HasValue ? (Wire.Flags.Value & 128) != 0 : null;
	/// <summary>Zero means unlimited.</summary>
	public int? MaximumRunsPerDay => Wire.Frequency;
	public int? MinimumIntervalMinutes => Wire.Interval;
	/// <summary>Copies a fully understood scene to an editable definition. Unsupported effective periods or components are never silently discarded.</summary>
	public RainPointSceneDraft CreateDraft ()
		{
		if (!SceneEncoding.IsRepeat (Wire.DateRepeat) || !MatchAll.HasValue || !MaximumRunsPerDay.HasValue || !MinimumIntervalMinutes.HasValue || Conditions.Any (c => c.Kind == RainPointSceneConditionKind.Unsupported || c.Enabled != true) || Actions.Any (a => a.Kind == RainPointSceneActionKind.Unsupported || a.Enabled != true))
			throw new NotSupportedException ("This scene contains settings which cannot yet be edited without losing information.");
		var solar = Wire.StartTime == 0x4000 && Wire.EndTime == 0x4001 ? RainPointSceneSolarPeriod.Daytime : Wire.StartTime == 0x4001 && Wire.EndTime == 0x4000 ? RainPointSceneSolarPeriod.Nighttime : RainPointSceneSolarPeriod.None;
		var draft = new RainPointSceneDraft { EffectiveRepeat = Wire.DateRepeat >= 128 ? RainPointSceneRepeat.Weekdays : (RainPointSceneRepeat)Wire.DateRepeat!.Value, EffectiveWeekdays = Wire.DateRepeat >= 128 ? (RainPointSceneWeekdays)(Wire.DateRepeat.Value & 127) : RainPointSceneWeekdays.None, Name = Name, MatchAll = MatchAll.Value, MaximumRunsPerDay = MaximumRunsPerDay.Value, MinimumIntervalMinutes = MinimumIntervalMinutes.Value, Conditions = Conditions, Actions = Actions, StartsOn = SceneEncoding.Date (Wire.StartDate), EndsOn = SceneEncoding.Date (Wire.EndDate), SolarPeriod = solar, WindowStartsAt = solar == RainPointSceneSolarPeriod.None ? SceneEncoding.Time (Wire.StartTime) : null, WindowEndsAt = solar == RainPointSceneSolarPeriod.None ? SceneEncoding.Time (Wire.EndTime) : null };
		draft.Encode (ExecutingHubId ?? throw new NotSupportedException ("Missing executing hub."));
		return draft;
		}
	public IReadOnlyList<RainPointSceneCondition> Conditions
		{
		get;
		}
	public IReadOnlyList<RainPointSceneAction> Actions
		{
		get;
		}
	}
internal static class SceneEncoding
	{
	internal static bool IsRepeat (int? value) => value is 0 or 1 or 2 or >= 129 and <= 255;
	internal static int Repeat (RainPointSceneRepeat repeat, RainPointSceneWeekdays weekdays)
		{
		if (!Enum.IsDefined (typeof (RainPointSceneRepeat), repeat) || (repeat == RainPointSceneRepeat.Weekdays ? (int)weekdays is < 1 or > 127 : weekdays != RainPointSceneWeekdays.None))
			throw new ArgumentException ("Select valid effective weekdays only for a weekday recurrence.");
		return repeat == RainPointSceneRepeat.Weekdays ? 128 | (int)weekdays : (int)repeat;
		}

	internal static DateTime? Date (int? value)
		{
		if (value == 65535)
			return null;
		if (!value.HasValue || value < 0 || value > 32767)
			throw new NotSupportedException ("Unsupported scene effective date.");
		try
			{
			return new DateTime (2020 + (value.Value >> 9 & 63), value.Value >> 5 & 15, value.Value & 31, 0, 0, 0, DateTimeKind.Unspecified);
			}
		catch (ArgumentOutOfRangeException) { throw new NotSupportedException ("Malformed scene effective date."); }
		}
	internal static TimeSpan? Time (int? value)
		{
		if (value == 65535)
			return null;
		if (!value.HasValue || value < 0 || value >> 6 >= 24 || (value & 63) >= 60)
			throw new NotSupportedException ("Unsupported scene clock window.");
		return new TimeSpan (value.Value >> 6, value.Value & 63, 0);
		}

	internal static string Hex (uint value, int bytes) => string.Concat (Enumerable.Range (0, bytes).Select (i => ((value >> (i * 8)) & 255).ToString ("X2", CultureInfo.InvariantCulture)));
	internal static bool TryNumber (string? hex, int bytes, out uint value)
		{
		value = 0;
		if (hex is null || hex.Length != bytes * 2)
			return false;
		for (int i = 0; i < bytes; i++)
			{
			if (!byte.TryParse (hex.Substring (i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
				return false;
			value |= (uint)b << (i * 8);
			}
		return true;
		}
	}
public sealed partial class RainPointCloudClient
	{
	public async Task<IReadOnlyList<RainPointSceneSummary>> GetScenesAsync (long homeId, CancellationToken cancellationToken = default)
		{
		Session session = GetSession ();
		var result = await SendAsync<LoginRequest, ApiResult<List<SceneWire>>> (HttpMethod.Get, "app/scene/2.0.5/list?hid=" + IdText (homeId), null, session, cancellationToken, homeId).ConfigureAwait (false);
		CheckResult (result, session);
		var list = RequireData (result);
		if (list.Any (s => s is null || !s.Id.HasValue || s.Id <= 0) || list.Select (s => s.Id).Distinct ().Count () != list.Count)
			throw new RainPointException ("The scene list contains invalid or duplicate identities.");
		return Array.AsReadOnly (list.Select (s => new RainPointSceneSummary (s)).ToArray ());
		}
	public async Task<RainPointScene> GetSceneAsync (long homeId, long sceneId, CancellationToken cancellationToken = default)
		{
		Session session = GetSession ();
		if (!(await GetScenesAsync (homeId, cancellationToken).ConfigureAwait (false)).Any (s => s.Id == sceneId))
			throw new ArgumentException ("Select a scene belonging to this home.", nameof (sceneId));
		var result = await SendAsync<LoginRequest, ApiResult<SceneWire>> (HttpMethod.Get, "app/scene/2.0.5/detail?id=" + IdText (sceneId), null, session, cancellationToken, homeId).ConfigureAwait (false);
		CheckResult (result, session);
		var wire = RequireData (result);
		if (wire.Id != sceneId || wire.Conditions is null || wire.Actions is null || wire.Conditions.Any (c => c is null) || wire.Actions.Any (a => a is null))
			throw new RainPointException ("Scene details are incomplete or have a different identity.");
		if (!ReferenceEquals (session, GetSession ()))
			throw new InvalidOperationException ("Session changed during the scene read.");
		return new (homeId, wire, session);
		}
	/// <summary>Changes the scene's switch. Enabling authorizes future actions; disabling does not stop an active valve.</summary>
	public Task SetSceneEnabledAsync (RainPointScene expected, bool enabled, CancellationToken cancellationToken = default) => WriteSceneAsync (expected, "open", new SceneSwitch { Id = expected?.Id ?? 0, Open = enabled ? 1 : 0 }, cancellationToken);
	public Task DeleteSceneAsync (RainPointScene expected, CancellationToken cancellationToken = default) => WriteSceneAsync (expected, "delete", new SceneSwitch { Id = expected?.Id ?? 0 }, cancellationToken);
	private async Task WriteSceneAsync<T> (RainPointScene expected, string operation, T request, CancellationToken token) where T : class
		{
		if (expected is null)
			throw new ArgumentNullException (nameof (expected));
		Session session = GetSession ();
		if (!ReferenceEquals (session, expected.Session) || Volatile.Read (ref expected.Attempted) != 0)
			throw new InvalidOperationException ("Read the scene in the current session before another attempt.");
		var current = await GetSceneAsync (expected.HomeId, expected.Id, token).ConfigureAwait (false);
		if (!ReferenceEquals (session, GetSession ()) || JsonSerializer.Serialize (current.Wire, _json) != JsonSerializer.Serialize (expected.Wire, _json))
			throw new InvalidOperationException ("Scene or session changed. Reload before writing.");
		token.ThrowIfCancellationRequested ();
		if (Interlocked.CompareExchange (ref expected.Attempted, 1, 0) != 0)
			throw new InvalidOperationException ("Scene has already been used for a write attempt.");
		var result = await SendAsync<T, ApiResult> (HttpMethod.Post, "app/scene/2.0.5/" + operation, request, session, token, expected.HomeId).ConfigureAwait (false);
		CheckResult (result, session);
		}
	private sealed class SceneSwitch
		{
		[JsonPropertyName ("id")]
		public long Id
			{
			get; set;
			}
		[JsonPropertyName ("open"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public int? Open
			{
			get; set;
			}
		}
	}
internal sealed class SceneWire
	{
	[JsonPropertyName ("id"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public long? Id
		{
		get; set;
		}
	[JsonPropertyName ("sceneName")]
	public string? Name
		{
		get; set;
		}
	[JsonPropertyName ("sceneExecutant")]
	public long? Executant
		{
		get; set;
		}
	[JsonPropertyName ("sceneFlags")]
	public int? Flags
		{
		get; set;
		}
	[JsonPropertyName ("sceneFreq")]
	public int? Frequency
		{
		get; set;
		}
	[JsonPropertyName ("sceneInterval")]
	public int? Interval
		{
		get; set;
		}
	[JsonPropertyName ("startDate")]
	public int? StartDate
		{
		get; set;
		}
	[JsonPropertyName ("endDate")]
	public int? EndDate
		{
		get; set;
		}
	[JsonPropertyName ("startTime")]
	public int? StartTime
		{
		get; set;
		}
	[JsonPropertyName ("endTime")]
	public int? EndTime
		{
		get; set;
		}
	[JsonPropertyName ("dateRepeat")]
	public int? DateRepeat
		{
		get; set;
		}
	[JsonPropertyName ("enable"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Enable
		{
		get; set;
		}
	[JsonPropertyName ("open"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Open
		{
		get; set;
		}
	[JsonPropertyName ("conditions")]
	public List<SceneConditionWire>? Conditions
		{
		get; set;
		}
	[JsonPropertyName ("actions")]
	public List<SceneActionWire>? Actions
		{
		get; set;
		}
	}
internal sealed class SceneConditionWire
	{
	[JsonPropertyName ("id")]
	public long? Id
		{
		get; set;
		}
	[JsonPropertyName ("type")] public int Type { get; set; } = -1;
	[JsonPropertyName ("code")] public int Code { get; set; } = -1;
	[JsonPropertyName ("enable")] public int Enable { get; set; } = -1;
	[JsonPropertyName ("contrast")]
	public int Contrast
		{
		get; set;
		}
	[JsonPropertyName ("mid")]
	public long HubId
		{
		get; set;
		}
	[JsonPropertyName ("addr")]
	public int Address
		{
		get; set;
		}
	[JsonPropertyName ("modelCode")]
	public int ModelCode
		{
		get; set;
		}
	[JsonPropertyName ("value1")] public string? Value1 { get; set; } = string.Empty;
	[JsonPropertyName ("value2")] public string? Value2 { get; set; } = string.Empty;
	[JsonPropertyName ("param")] public string? Parameter { get; set; } = string.Empty;
	[JsonPropertyName ("dataType")]
	public int DataType
		{
		get; set;
		}
	}
internal sealed class SceneActionWire
	{
	[JsonPropertyName ("id")]
	public long? Id
		{
		get; set;
		}
	[JsonPropertyName ("type")] public int Type { get; set; } = -1;
	[JsonPropertyName ("code")] public int Code { get; set; } = -1;
	[JsonPropertyName ("enable")] public int Enable { get; set; } = -1;
	[JsonPropertyName ("mid")]
	public long HubId
		{
		get; set;
		}
	[JsonPropertyName ("addr")]
	public int Address
		{
		get; set;
		}
	[JsonPropertyName ("modelCode")]
	public int ModelCode
		{
		get; set;
		}
	[JsonPropertyName ("value")] public string? Value { get; set; } = string.Empty;
	[JsonPropertyName ("param")] public string? Parameter { get; set; } = string.Empty;
	}
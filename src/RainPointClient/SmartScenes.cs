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
/// Combines the days on which a scene's weekly recurrence applies.
/// </summary>
[Flags]
public enum RainPointSceneWeekdays
	{
	/// <summary>No weekdays selected.</summary>
	None = 0,
	/// <summary>Include Sunday in the weekly recurrence.</summary>
	Sunday = 1,
	/// <summary>Include Monday in the weekly recurrence.</summary>
	Monday = 2,
	/// <summary>Include Tuesday in the weekly recurrence.</summary>
	Tuesday = 4,
	/// <summary>Include Wednesday in the weekly recurrence.</summary>
	Wednesday = 8,
	/// <summary>Include Thursday in the weekly recurrence.</summary>
	Thursday = 16,
	/// <summary>Include Friday in the weekly recurrence.</summary>
	Friday = 32,
	/// <summary>Include Saturday in the weekly recurrence.</summary>
	Saturday = 64,
	/// <summary>Include all seven days of the week.</summary>
	EveryDay = 127
	}
/// <summary>
/// Selects the comparison used by a weather-threshold scene condition.
/// </summary>
public enum RainPointSceneComparison
	{
	/// <summary>The reported weather value must be below the threshold.</summary>
	LessThan = 0,
	/// <summary>The reported weather value must equal the threshold.</summary>
	Equal = 1,
	/// <summary>The reported weather value must exceed the threshold.</summary>
	GreaterThan = 2
	}
/// <summary>
/// Identifies a weather metric together with the units used by its threshold.
/// </summary>
public enum RainPointSceneWeatherMetric
	{
	/// <summary>
	/// Precipitation probability, measured in percent.
	/// </summary>
	RainProbabilityPercent = 4,
	/// <summary>
	/// Air temperature, measured in degrees Celsius.
	/// </summary>
	TemperatureCelsius = 5,
	/// <summary>
	/// Relative humidity, measured in percent.
	/// </summary>
	HumidityPercent = 6,
	/// <summary>
	/// Wind speed, measured in kilometres per hour.
	/// </summary>
	WindSpeedKilometresPerHour = 7
	}
/// <summary>
/// Identifies the supported interpretation of a scene condition.
/// </summary>
public enum RainPointSceneConditionKind
	{
	/// <summary>The condition cannot be represented by a supported typed contract.</summary>
	Unsupported,
	/// <summary>A comparison against a weather measurement.</summary>
	WeatherThreshold,
	/// <summary>A one-time home-local date and time.</summary>
	Once,
	/// <summary>A recurring home-local clock or solar time.</summary>
	RepeatingTime,
	/// <summary>A match against selected vendor weather conditions.</summary>
	WeatherTypes
	}
/// <summary>
/// Identifies the supported interpretation of a scene action.
/// </summary>
public enum RainPointSceneActionKind
	{
	/// <summary>The action cannot be represented by a supported typed contract.</summary>
	Unsupported,
	/// <summary>Send a notification to the configured recipients.</summary>
	Notification,
	/// <summary>Apply a rain delay to a selected timer zone.</summary>
	RainDelay
	}
/// <summary>
/// Selects the local-date recurrence of a scheduled scene condition.
/// </summary>
public enum RainPointSceneRepeat
	{
	/// <summary>Repeat on every home-local calendar date.</summary>
	Daily,
	/// <summary>Repeat on odd-numbered home-local dates.</summary>
	OddDates,
	/// <summary>Repeat on even-numbered home-local dates.</summary>
	EvenDates,
	/// <summary>Repeat on the selected days of the week.</summary>
	Weekdays
	}
/// <summary>
/// Selects a fixed local clock time, sunrise or sunset for a scene condition.
/// </summary>
public enum RainPointSceneTime
	{
	/// <summary>Use an explicit home-local clock time.</summary>
	Clock,
	/// <summary>Use the vendor-computed sunrise time.</summary>
	Sunrise,
	/// <summary>Use the vendor-computed sunset time.</summary>
	Sunset
	}

/// <summary>An immutable condition. Unsupported vendor conditions are identified without exposing their encoded payload.</summary>
public sealed class RainPointSceneCondition
	{
	/// <summary>
	/// Gets the original attributed response retained for guarded edits and unknown-field preservation.
	/// </summary>
	internal SceneConditionWire Wire
		{
		get;
		}
	/// <summary>
	/// Initializes scene condition from the supplied typed values.
	/// </summary>
	/// <param name="wire">The attributed protocol response from which to create the typed observation.</param>
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
	/// <summary>
	/// Gets the recognized condition kind, or Unsupported without discarding its wire representation.
	/// </summary>
	public RainPointSceneConditionKind Kind
		{
		get;
		}
	/// <summary>
	/// Gets the recognized enabled flag, or null for an unknown value.
	/// </summary>
	public bool? Enabled => Wire.Enable is 0 or 1 ? Wire.Enable == 1 : null;
	/// <summary>
	/// Gets the associated positive hub ID, or null when absent.
	/// </summary>
	public long? HubId => Wire.HubId > 0 ? Wire.HubId : null;
	/// <summary>
	/// Gets the associated RF address for a device condition, or null for other condition families.
	/// </summary>
	public int? DeviceAddress => Wire.Type == 0 ? Wire.Address : null;
	/// <summary>
	/// Gets the weather-threshold metric, or null when this is not a decoded weather-threshold condition.
	/// </summary>
	public RainPointSceneWeatherMetric? Metric
		{
		get;
		}
	/// <summary>
	/// Gets the decoded threshold comparison, or null when unavailable.
	/// </summary>
	public RainPointSceneComparison? Comparison
		{
		get;
		}
	/// <summary>
	/// Gets the threshold in the selected metric's units, or null when unavailable.
	/// </summary>
	public decimal? Threshold
		{
		get;
		}
	/// <summary>Home-local wall time; no conversion using the computer's time zone is made.</summary>
	public DateTime? AtLocal
		{
		get;
		}
	/// <summary>
	/// Gets the time condition's recurrence pattern, or null when unavailable.
	/// </summary>
	public RainPointSceneRepeat? Repeat
		{
		get;
		}
	/// <summary>
	/// Gets the selected weekday mask for weekly recurrence; None denotes no selected weekdays.
	/// </summary>
	public RainPointSceneWeekdays Weekdays
		{
		get;
		}
	/// <summary>
	/// Gets the fixed-clock or solar-time selection, or null when unavailable.
	/// </summary>
	public RainPointSceneTime? Time
		{
		get;
		}
	/// <summary>
	/// Gets the home-local time of day for a fixed-clock condition, or null for other conditions.
	/// </summary>
	public TimeSpan? ClockTime
		{
		get;
		}
	/// <summary>
	/// Gets the matched vendor weather codes for a weather-types condition, otherwise an empty list.
	/// </summary>
	public IReadOnlyList<int> WeatherTypeCodes { get; } = Array.Empty<int> ();
	/// <summary>
	/// Gets a readable decoded description or an explicit unsupported-condition message.
	/// </summary>
	public string Description => Kind switch
		{
			RainPointSceneConditionKind.WeatherTypes => "Weather types: " + string.Join (", ", WeatherTypeCodes),
			RainPointSceneConditionKind.WeatherThreshold => $"{Metric} {Comparison} {Threshold:0.##}",
			RainPointSceneConditionKind.Once => $"Once: {AtLocal:yyyy-MM-dd HH:mm} (home time)",
			RainPointSceneConditionKind.RepeatingTime => $"{Repeat} {Weekdays}: {(Time == RainPointSceneTime.Clock ? ClockTime.ToString () : Time.ToString ())}",
			_ => "Unsupported condition; use the vendor app to inspect it."
			};
	/// <summary>
	/// Creates a condition matching the selected vendor weather-condition codes.
	/// </summary>
	/// <param name="types">Distinct weather catalog entries with codes from 0 through 31.</param>
	/// <returns>An immutable condition matching the selected weather codes.</returns>
	/// <exception cref="System.ArgumentException">Select distinct types from the weather catalog.</exception>
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
	/// <summary>
	/// Creates a threshold comparison using the units of the selected weather metric.
	/// </summary>
	/// <param name="metric">The weather metric whose documented units determine the threshold.</param>
	/// <param name="comparison">The comparison applied to the selected weather metric.</param>
	/// <param name="threshold">The threshold: -40..60 Celsius in 0.1-degree steps, 0..150 km/h in whole units, or 0..100 percent in whole units, according to the metric.</param>
	/// <returns>An immutable weather-threshold condition.</returns>
	/// <exception cref="System.ArgumentOutOfRangeException">An argument is outside the supported range described above.</exception>
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
	/// <summary>
	/// Creates a one-time condition at a home-local wall-clock time.
	/// </summary>
	/// <param name="homeLocal">A whole-minute home-local wall time in 2020 through 2083 with Unspecified kind.</param>
	/// <returns>An immutable one-time home-local condition.</returns>
	/// <exception cref="System.ArgumentException">Use a whole minute in home-local time, 2020..2083.</exception>
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
	/// <summary>
	/// Creates a recurring condition using a fixed local time or sunrise/sunset.
	/// </summary>
	/// <param name="repeat">The recurrence pattern; the operation validates the supported combinations.</param>
	/// <param name="time">A fixed local clock time, sunrise or sunset selector.</param>
	/// <param name="clockTime">A whole-minute time from midnight through 23:59, required only for a fixed-clock condition.</param>
	/// <param name="weekdays">The selected weekdays, used only with a weekly recurrence.</param>
	/// <returns>An immutable repeating clock or solar-time condition.</returns>
	/// <exception cref="System.ArgumentOutOfRangeException">An argument is outside the supported range described above.</exception>
	/// <exception cref="System.ArgumentException">Specify weekdays only for a weekday recurrence. Specify a whole-minute clock time only for a clock condition.</exception>
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
	/// <summary>
	/// Gets the original attributed response retained for guarded edits and unknown-field preservation.
	/// </summary>
	internal SceneActionWire Wire
		{
		get;
		}
	/// <summary>
	/// Initializes scene action from the supplied typed values.
	/// </summary>
	/// <param name="wire">The attributed protocol response from which to create the typed observation.</param>
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
	/// <summary>
	/// Gets the recognized action kind, or Unsupported without discarding its wire representation.
	/// </summary>
	public RainPointSceneActionKind Kind
		{
		get;
		}
	/// <summary>
	/// Gets the recognized enabled flag, or null for an unknown value.
	/// </summary>
	public bool? Enabled => Wire.Enable is 0 or 1 ? Wire.Enable == 1 : null;
	/// <summary>
	/// Gets the associated positive hub ID, or null when absent.
	/// </summary>
	public long? HubId => Wire.HubId > 0 ? Wire.HubId : null;
	/// <summary>
	/// Gets the associated child RF address, or null when absent.
	/// </summary>
	public int? DeviceAddress => Wire.Type is 0 or 2 ? Wire.Address : null;
	/// <summary>
	/// Gets the notification message, or null when this action is not a decoded notification.
	/// </summary>
	public string? Message
		{
		get;
		}
	/// <summary>Member identifiers or email addresses, never a protocol payload.</summary>
	public IReadOnlyList<string> Recipients { get; } = Array.Empty<string> ();
	/// <summary>
	/// Gets the one-based zones targeted by a decoded rain-delay action, otherwise an empty list.
	/// </summary>
	public IReadOnlyList<int> Zones { get; } = Array.Empty<int> ();
	/// <summary>
	/// Gets the decoded rain-delay length in days, or null when unavailable.
	/// </summary>
	public int? RainDelayDays
		{
		get;
		}
	/// <summary>
	/// Gets a readable decoded description or an explicit unsupported-action message.
	/// </summary>
	public string Description => Kind switch { RainPointSceneActionKind.Notification => $"Notify {Recipients.Count} recipient(s): {Message}", RainPointSceneActionKind.RainDelay => $"Rain delay {RainDelayDays} days; zones {string.Join (", ", Zones)}", _ => "Unsupported device action; use the vendor app to inspect it." };
	/// <summary>
	/// Creates a notification action for the specified home members and email recipients; creating the object sends nothing.
	/// </summary>
	/// <param name="message">The message text associated with this operation.</param>
	/// <param name="memberIds">The home-member IDs that should receive the notification when the scene executes.</param>
	/// <param name="emailAddresses">The email recipients that should receive the notification when the scene executes.</param>
	/// <returns>An immutable notification action; no message is sent by this factory.</returns>
	/// <exception cref="System.ArgumentException">Use a message of 1..100 characters. Recipients must be distinct; at most five email addresses are supported. Use plain email addresses. At least one recipient is required; the combined recipient list is limited to 200 characters.</exception>
	/// <exception cref="System.ArgumentNullException">A required argument is null.</exception>
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
	/// <summary>
	/// Creates a rain-delay action for one timer zone; execution changes future plan behavior rather than stopping a running valve.
	/// </summary>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="address">The paired child's RF address within its hub, distinct from its cloud database ID.</param>
	/// <param name="zone">The one-based zone number on the selected timer.</param>
	/// <param name="days">The supported whole-day rain delay to apply when the scene executes.</param>
	/// <returns>An immutable rain-delay action; no device change is submitted by this factory.</returns>
	/// <exception cref="System.ArgumentNullException">A required argument is null.</exception>
	/// <exception cref="System.ArgumentException">Select a supported discovered timer.</exception>
	/// <exception cref="System.ArgumentOutOfRangeException">An argument is outside the supported range described above.</exception>
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
/// <summary>
/// Describes a listed scene without loading its full conditions and actions.
/// </summary>
public sealed class RainPointSceneSummary
	{
	/// <summary>
	/// Initializes scene summary from the supplied typed values.
	/// </summary>
	/// <param name="wire">The attributed protocol response from which to create the typed observation.</param>
	internal RainPointSceneSummary (SceneWire wire)
		{
		Id = wire.Id ?? 0;
		Name = wire.Name ?? string.Empty;
		Enabled = wire.Open is 0 or 1 ? wire.Open == 1 : null;
		Available = wire.Enable is 0 or 1 ? wire.Enable == 1 : null;
		}
	/// <summary>
	/// Gets the cloud or catalog identifier for this record.
	/// </summary>
	public long Id
		{
		get;
		}
	/// <summary>
	/// Gets the display name supplied for this record.
	/// </summary>
	public string Name
		{
		get;
		}
	/// <summary>
	/// Gets the recognized scene enabled state, or null when the vendor value is unknown.
	/// </summary>
	public bool? Enabled
		{
		get;
		}
	/// <summary>
	/// Gets the recognized scene availability state, or null when the vendor value is unknown.
	/// </summary>
	public bool? Available
		{
		get;
		}
	}
/// <summary>A scene observation. Enabling can start future automation; disabling does not stop an already running valve.</summary>
public sealed class RainPointScene
	{
	/// <summary>
	/// Initializes scene from the supplied typed values.
	/// </summary>
	/// <param name="homeId">The positive cloud home identifier.</param>
	/// <param name="wire">The attributed protocol response from which to create the typed observation.</param>
	/// <param name="session">The session identity that owns this observation and guards subsequent writes.</param>
	internal RainPointScene (long homeId, SceneWire wire, object session)
		{
		HomeId = homeId;
		Wire = wire;
		Session = session;
		Conditions = Array.AsReadOnly ((wire.Conditions ?? []).Select (c => new RainPointSceneCondition (c)).ToArray ());
		Actions = Array.AsReadOnly ((wire.Actions ?? []).Select (a => new RainPointSceneAction (a)).ToArray ());
		}
	/// <summary>
	/// Gets the original attributed response retained for guarded edits and unknown-field preservation.
	/// </summary>
	internal SceneWire Wire
		{
		get;
		}
	/// <summary>
	/// Gets the session identity that owns this observation.
	/// </summary>
	internal object Session
		{
		get;
		}
	/// <summary>
	/// Tracks whether this observation has already been used for a write attempt.
	/// </summary>
	internal int Attempted;
	/// <summary>
	/// Gets the home containing this scene.
	/// </summary>
	public long HomeId
		{
		get;
		}
	/// <summary>
	/// Gets the cloud scene identifier.
	/// </summary>
	public long Id => Wire.Id!.Value;
	/// <summary>
	/// Gets the scene display name.
	/// </summary>
	public string Name => Wire.Name ?? string.Empty;
	/// <summary>
	/// Gets the cloud ID of the hub assigned to execute the scene.
	/// </summary>
	public long? ExecutingHubId => Wire.Executant;
	/// <summary>
	/// Gets the recognized enabled state, or null for an unrecognized switch value.
	/// </summary>
	public bool? Enabled => Wire.Open is 0 or 1 ? Wire.Open == 1 : null;
	/// <summary>
	/// Gets reported scene availability, or null for an unrecognized availability value.
	/// </summary>
	public bool? Available => Wire.Enable is 0 or 1 ? Wire.Enable == 1 : null;
	/// <summary>
	/// Gets whether all conditions must match, or null when the flags cannot be interpreted.
	/// </summary>
	public bool? MatchAll => Wire.Flags.HasValue ? (Wire.Flags.Value & 128) != 0 : null;
	/// <summary>Zero means unlimited.</summary>
	public int? MaximumRunsPerDay => Wire.Frequency;
	/// <summary>
	/// Gets the minimum execution interval in minutes, or null when unavailable.
	/// </summary>
	public int? MinimumIntervalMinutes => Wire.Interval;
	/// <summary>Copies a fully understood scene to an editable definition. Unsupported effective periods or components are never silently discarded.</summary>
	/// <returns>An editable complete copy, provided every effective-period and component representation is supported.</returns>
	/// <exception cref="System.NotSupportedException">This scene contains settings which cannot yet be edited without losing information. Missing executing hub.</exception>
	public RainPointSceneDraft CreateDraft ()
		{
		if (!SceneEncoding.IsRepeat (Wire.DateRepeat) || !MatchAll.HasValue || !MaximumRunsPerDay.HasValue || !MinimumIntervalMinutes.HasValue || Conditions.Any (c => c.Kind == RainPointSceneConditionKind.Unsupported || c.Enabled != true) || Actions.Any (a => a.Kind == RainPointSceneActionKind.Unsupported || a.Enabled != true))
			throw new NotSupportedException ("This scene contains settings which cannot yet be edited without losing information.");
		var solar = Wire.StartTime == 0x4000 && Wire.EndTime == 0x4001 ? RainPointSceneSolarPeriod.Daytime : Wire.StartTime == 0x4001 && Wire.EndTime == 0x4000 ? RainPointSceneSolarPeriod.Nighttime : RainPointSceneSolarPeriod.None;
		var draft = new RainPointSceneDraft { EffectiveRepeat = Wire.DateRepeat >= 128 ? RainPointSceneRepeat.Weekdays : (RainPointSceneRepeat)Wire.DateRepeat!.Value, EffectiveWeekdays = Wire.DateRepeat >= 128 ? (RainPointSceneWeekdays)(Wire.DateRepeat.Value & 127) : RainPointSceneWeekdays.None, Name = Name, MatchAll = MatchAll.Value, MaximumRunsPerDay = MaximumRunsPerDay.Value, MinimumIntervalMinutes = MinimumIntervalMinutes.Value, Conditions = Conditions, Actions = Actions, StartsOn = SceneEncoding.Date (Wire.StartDate), EndsOn = SceneEncoding.Date (Wire.EndDate), SolarPeriod = solar, WindowStartsAt = solar == RainPointSceneSolarPeriod.None ? SceneEncoding.Time (Wire.StartTime) : null, WindowEndsAt = solar == RainPointSceneSolarPeriod.None ? SceneEncoding.Time (Wire.EndTime) : null };
		draft.Encode (ExecutingHubId ?? throw new NotSupportedException ("Missing executing hub."));
		return draft;
		}
	/// <summary>
	/// Gets the observed conditions, including explicitly unsupported kinds.
	/// </summary>
	public IReadOnlyList<RainPointSceneCondition> Conditions
		{
		get;
		}
	/// <summary>
	/// Gets the observed actions, including explicitly unsupported kinds.
	/// </summary>
	public IReadOnlyList<RainPointSceneAction> Actions
		{
		get;
		}
	}
/// <summary>
/// Internal scene encoding representation or processing contract for the RainPoint protocol.
/// </summary>
internal static class SceneEncoding
	{
	/// <summary>
	/// Tests whether a vendor recurrence value has a recognized representation.
	/// </summary>
	/// <param name="value">The encoded protocol value to decode or serialize; absent or invalid values follow the method result contract.</param>
	/// <returns>True for a recognized vendor recurrence representation.</returns>
	internal static bool IsRepeat (int? value) => value is 0 or 1 or 2 or >= 129 and <= 255;
	/// <summary>
	/// Encodes a recurrence and its permitted weekday mask.
	/// </summary>
	/// <param name="repeat">The recurrence pattern; the operation validates the supported combinations.</param>
	/// <param name="weekdays">The selected weekdays, used only with a weekly recurrence.</param>
	/// <returns>The encoded recurrence and weekday bits.</returns>
	/// <exception cref="System.ArgumentException">Select valid effective weekdays only for a weekday recurrence.</exception>
	internal static int Repeat (RainPointSceneRepeat repeat, RainPointSceneWeekdays weekdays)
		{
		if (!Enum.IsDefined (typeof (RainPointSceneRepeat), repeat) || (repeat == RainPointSceneRepeat.Weekdays ? (int)weekdays is < 1 or > 127 : weekdays != RainPointSceneWeekdays.None))
			throw new ArgumentException ("Select valid effective weekdays only for a weekday recurrence.");
		return repeat == RainPointSceneRepeat.Weekdays ? 128 | (int)weekdays : (int)repeat;
		}

	/// <summary>
	/// Decodes a packed home-local date, returning null for an absent or invalid value.
	/// </summary>
	/// <param name="value">The encoded protocol value to decode or serialize; absent or invalid values follow the method result contract.</param>
	/// <returns>The home-local date, or null when the field is absent or invalid.</returns>
	/// <exception cref="System.NotSupportedException">Unsupported scene effective date. Malformed scene effective date.</exception>
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
	/// <summary>
	/// Decodes a packed home-local time of day, returning null when absent or invalid.
	/// </summary>
	/// <param name="value">The encoded protocol value to decode or serialize; absent or invalid values follow the method result contract.</param>
	/// <returns>The decoded time of day, or null when the field is absent or invalid.</returns>
	/// <exception cref="System.NotSupportedException">Unsupported scene clock window.</exception>
	internal static TimeSpan? Time (int? value)
		{
		if (value == 65535)
			return null;
		if (!value.HasValue || value < 0 || value >> 6 >= 24 || (value & 63) >= 60)
			throw new NotSupportedException ("Unsupported scene clock window.");
		return new TimeSpan (value.Value >> 6, value.Value & 63, 0);
		}

	/// <summary>
	/// Encodes an unsigned value as a fixed-width uppercase hexadecimal field.
	/// </summary>
	/// <param name="value">The encoded protocol value to decode or serialize; absent or invalid values follow the method result contract.</param>
	/// <param name="bytes">The required encoded byte width.</param>
	/// <returns>An uppercase hexadecimal field with the requested byte width.</returns>
	internal static string Hex (uint value, int bytes) => string.Concat (Enumerable.Range (0, bytes).Select (i => ((value >> (i * 8)) & 255).ToString ("X2", CultureInfo.InvariantCulture)));
	/// <summary>
	/// Attempts to decode an exact-length hexadecimal field as an unsigned integer.
	/// </summary>
	/// <param name="hex">The candidate hexadecimal field, or null when absent.</param>
	/// <param name="bytes">The required encoded byte width.</param>
	/// <param name="value">Receives the decoded unsigned value on success.</param>
	/// <returns>True when the entire field was decoded; false for an absent, malformed or wrongly sized field.</returns>
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
	/// <summary>
	/// Lists scene summaries for the selected home.
	/// </summary>
	/// <param name="homeId">The positive cloud home identifier.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the requested scene summary records.</returns>
	/// <exception cref="RainPointException">The scene list contains invalid or duplicate identities. The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
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
	/// <summary>
	/// Reads a full scene observation, retaining unsupported components for guarded edit decisions.
	/// </summary>
	/// <param name="homeId">The positive cloud home identifier.</param>
	/// <param name="sceneId">The cloud scene identifier; null leaves scene history unfiltered where permitted.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the typed scene result.</returns>
	/// <exception cref="System.ArgumentException">Select a scene belonging to this home.</exception>
	/// <exception cref="RainPointException">Scene details are incomplete or have a different identity. The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.InvalidOperationException">Session changed during the scene read.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
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
	/// <param name="expected">An unused, current scene observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="enabled">Whether the selected feature or saved plan should be enabled.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task SetSceneEnabledAsync (RainPointScene expected, bool enabled, CancellationToken cancellationToken = default) => WriteSceneAsync (expected, "open", new SceneSwitch { Id = expected?.Id ?? 0, Open = enabled ? 1 : 0 }, cancellationToken);
	/// <summary>
	/// Deletes an observed scene without retrying the write; this does not issue valve-stop commands.
	/// </summary>
	/// <param name="expected">An unused, current scene observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
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
		/// <summary>
		/// Stores the id protocol field for scene switch.
		/// </summary>
		[JsonPropertyName ("id")]
		public long Id
			{
			get; set;
			}
		/// <summary>
		/// Stores the open protocol field for scene switch.
		/// </summary>
		[JsonPropertyName ("open"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public int? Open
			{
			get; set;
			}
		}
	}
/// <summary>
/// Internal scene wire representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class SceneWire
	{
	/// <summary>
	/// Stores the id protocol field for scene wire.
	/// </summary>
	[JsonPropertyName ("id"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public long? Id
		{
		get; set;
		}
	/// <summary>
	/// Stores the sceneName protocol field for scene wire.
	/// </summary>
	[JsonPropertyName ("sceneName")]
	public string? Name
		{
		get; set;
		}
	/// <summary>
	/// Stores the sceneExecutant protocol field for scene wire.
	/// </summary>
	[JsonPropertyName ("sceneExecutant")]
	public long? Executant
		{
		get; set;
		}
	/// <summary>
	/// Stores the original bit field, including unknown bits.
	/// </summary>
	[JsonPropertyName ("sceneFlags")]
	public int? Flags
		{
		get; set;
		}
	/// <summary>
	/// Stores the sceneFreq protocol field for scene wire.
	/// </summary>
	[JsonPropertyName ("sceneFreq")]
	public int? Frequency
		{
		get; set;
		}
	/// <summary>
	/// Stores the sceneInterval protocol field for scene wire.
	/// </summary>
	[JsonPropertyName ("sceneInterval")]
	public int? Interval
		{
		get; set;
		}
	/// <summary>
	/// Stores the startDate protocol field for scene wire.
	/// </summary>
	[JsonPropertyName ("startDate")]
	public int? StartDate
		{
		get; set;
		}
	/// <summary>
	/// Stores the endDate protocol field for scene wire.
	/// </summary>
	[JsonPropertyName ("endDate")]
	public int? EndDate
		{
		get; set;
		}
	/// <summary>
	/// Stores the startTime protocol field for scene wire.
	/// </summary>
	[JsonPropertyName ("startTime")]
	public int? StartTime
		{
		get; set;
		}
	/// <summary>
	/// Stores the endTime protocol field for scene wire.
	/// </summary>
	[JsonPropertyName ("endTime")]
	public int? EndTime
		{
		get; set;
		}
	/// <summary>
	/// Stores the dateRepeat protocol field for scene wire.
	/// </summary>
	[JsonPropertyName ("dateRepeat")]
	public int? DateRepeat
		{
		get; set;
		}
	/// <summary>
	/// Stores the enable protocol field for scene wire.
	/// </summary>
	[JsonPropertyName ("enable"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Enable
		{
		get; set;
		}
	/// <summary>
	/// Stores the open protocol field for scene wire.
	/// </summary>
	[JsonPropertyName ("open"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Open
		{
		get; set;
		}
	/// <summary>
	/// Stores the conditions protocol field for scene wire.
	/// </summary>
	[JsonPropertyName ("conditions")]
	public List<SceneConditionWire>? Conditions
		{
		get; set;
		}
	/// <summary>
	/// Stores the actions protocol field for scene wire.
	/// </summary>
	[JsonPropertyName ("actions")]
	public List<SceneActionWire>? Actions
		{
		get; set;
		}
	}
/// <summary>
/// Internal scene condition wire representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class SceneConditionWire
	{
	/// <summary>
	/// Stores the id protocol field for scene condition wire.
	/// </summary>
	[JsonPropertyName ("id")]
	public long? Id
		{
		get; set;
		}
	/// <summary>
	/// Stores the type protocol field for scene condition wire.
	/// </summary>
	[JsonPropertyName ("type")] public int Type { get; set; } = -1;
	/// <summary>
	/// Stores the verified email code for one matching account operation.
	/// </summary>
	[JsonPropertyName ("code")] public int Code { get; set; } = -1;
	/// <summary>
	/// Stores the enable protocol field for scene condition wire.
	/// </summary>
	[JsonPropertyName ("enable")] public int Enable { get; set; } = -1;
	/// <summary>
	/// Stores the contrast protocol field for scene condition wire.
	/// </summary>
	[JsonPropertyName ("contrast")]
	public int Contrast
		{
		get; set;
		}
	/// <summary>
	/// Stores the mid protocol field for scene condition wire.
	/// </summary>
	[JsonPropertyName ("mid")]
	public long HubId
		{
		get; set;
		}
	/// <summary>
	/// Stores the addr protocol field for scene condition wire.
	/// </summary>
	[JsonPropertyName ("addr")]
	public int Address
		{
		get; set;
		}
	/// <summary>
	/// Stores the modelCode protocol field for scene condition wire.
	/// </summary>
	[JsonPropertyName ("modelCode")]
	public int ModelCode
		{
		get; set;
		}
	/// <summary>
	/// Stores the value1 protocol field for scene condition wire.
	/// </summary>
	[JsonPropertyName ("value1")] public string? Value1 { get; set; } = string.Empty;
	/// <summary>
	/// Stores the value2 protocol field for scene condition wire.
	/// </summary>
	[JsonPropertyName ("value2")] public string? Value2 { get; set; } = string.Empty;
	/// <summary>
	/// Stores the original encoded configuration field for bounded decoding and guarded updates.
	/// </summary>
	[JsonPropertyName ("param")] public string? Parameter { get; set; } = string.Empty;
	/// <summary>
	/// Stores the dataType protocol field for scene condition wire.
	/// </summary>
	[JsonPropertyName ("dataType")]
	public int DataType
		{
		get; set;
		}
	}
/// <summary>
/// Internal scene action wire representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class SceneActionWire
	{
	/// <summary>
	/// Stores the id protocol field for scene action wire.
	/// </summary>
	[JsonPropertyName ("id")]
	public long? Id
		{
		get; set;
		}
	/// <summary>
	/// Stores the type protocol field for scene action wire.
	/// </summary>
	[JsonPropertyName ("type")] public int Type { get; set; } = -1;
	/// <summary>
	/// Stores the verified email code for one matching account operation.
	/// </summary>
	[JsonPropertyName ("code")] public int Code { get; set; } = -1;
	/// <summary>
	/// Stores the enable protocol field for scene action wire.
	/// </summary>
	[JsonPropertyName ("enable")] public int Enable { get; set; } = -1;
	/// <summary>
	/// Stores the mid protocol field for scene action wire.
	/// </summary>
	[JsonPropertyName ("mid")]
	public long HubId
		{
		get; set;
		}
	/// <summary>
	/// Stores the addr protocol field for scene action wire.
	/// </summary>
	[JsonPropertyName ("addr")]
	public int Address
		{
		get; set;
		}
	/// <summary>
	/// Stores the modelCode protocol field for scene action wire.
	/// </summary>
	[JsonPropertyName ("modelCode")]
	public int ModelCode
		{
		get; set;
		}
	/// <summary>
	/// Stores the value protocol field for scene action wire.
	/// </summary>
	[JsonPropertyName ("value")] public string? Value { get; set; } = string.Empty;
	/// <summary>
	/// Stores the original encoded configuration field for bounded decoding and guarded updates.
	/// </summary>
	[JsonPropertyName ("param")] public string? Parameter { get; set; } = string.Empty;
	}
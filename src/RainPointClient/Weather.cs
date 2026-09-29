// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
namespace RainPointClient;
/// <summary>Weather-service signing credentials. These are separate from RainPoint account credentials.
/// The library does not embed, persist or log a vendor signing secret.</summary>
public sealed class RainPointWeatherAccess
	{
	/// <summary>
	/// Stores caller-supplied weather signing credentials independently of the RainPoint login.
	/// </summary>
	/// <param name="accessKey">The weather-service access key.</param>
	/// <param name="accessSecret">The weather-service signing secret; the library does not log or persist it.</param>
	/// <exception cref="System.ArgumentException">Use an alphanumeric weather access key. Weather signing secret is required.</exception>
	public RainPointWeatherAccess (string accessKey, string accessSecret)
		{
		if (string.IsNullOrWhiteSpace (accessKey) || accessKey.Any (c => !char.IsLetterOrDigit (c)))
			throw new ArgumentException ("Use an alphanumeric weather access key.", nameof (accessKey));
		if (string.IsNullOrEmpty (accessSecret))
			throw new ArgumentException ("Weather signing secret is required.", nameof (accessSecret));
		Key = accessKey;
		Secret = accessSecret;
		}
	/// <summary>
	/// Gets the weather-service access key used for request signing.
	/// </summary>
	internal string Key
		{
		get;
		}
	/// <summary>
	/// Gets the weather-service signing secret; never log or expose it in public diagnostics.
	/// </summary>
	internal string Secret
		{
		get;
		}
	/// <summary>
	/// Signs a weather-service request using the supplied timestamp and nonce.
	/// </summary>
	/// <param name="timestamp">The request timestamp used by the weather-service signature.</param>
	/// <param name="nonce">The per-request nonce used by the weather-service signature.</param>
	/// <returns>The request signature for the supplied timestamp and nonce.</returns>
	internal string Sign (long timestamp, string nonce)
		{
		using var mac = new HMACMD5 (Encoding.UTF8.GetBytes (Secret));
		return Convert.ToBase64String (mac.ComputeHash (Encoding.UTF8.GetBytes ("accessKey=" + Key + "&timestamp=" + timestamp.ToString (CultureInfo.InvariantCulture) + "&signatureNonce=" + nonce)));
		}
	}
/// <summary>A nullable weather observation or forecast. Missing/invalid readings remain unknown.</summary>
public sealed class RainPointWeatherReading
	{
	/// <summary>
	/// Initializes weather reading from the supplied typed values.
	/// </summary>
	/// <param name="value">The attributed weather reading to normalize into nullable typed values.</param>
	internal RainPointWeatherReading (WeatherValue value)
		{
		TemperatureCelsius = Celsius (value.Temperature);
		FeelsLikeCelsius = Celsius (value.FeelsLike);
		MinimumCelsius = Celsius (value.Minimum);
		MaximumCelsius = Celsius (value.Maximum);
		MinimumFeelsLikeCelsius = Celsius (value.MinimumFeelsLike);
		MaximumFeelsLikeCelsius = Celsius (value.MaximumFeelsLike);
		HumidityPercent = Number (value.Humidity, 0, 100);
		RainProbabilityPercent = Number (value.RainProbability, 0, 100);
		WindSpeedKilometresPerHour = Number (value.WindSpeed, 0, 1000);
		UltravioletIndex = Number (value.UvIndex, 0, 100);
		CloudCoverPercent = Number (value.CloudCover, 0, 100);
		WeatherCode = value.WeatherCode;
		WindDirection = value.WindDirection;
		ExpiresAt = Instant (value.Expires);
		if (DateTime.TryParseExact (value.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
			{
			ForecastDate = date;
			if (int.TryParse (value.Time, NumberStyles.None, CultureInfo.InvariantCulture, out int time) && time is >= 0 and <= 23)
				ForecastLocalTime = date.AddHours (time);
			}
		Sunrise = Clock (value.Sunrise);
		Sunset = Clock (value.Sunset);
		}
	/// <summary>
	/// Gets air temperature in degrees Celsius, or null when unavailable.
	/// </summary>
	public decimal? TemperatureCelsius
		{
		get;
		}
	/// <summary>
	/// Gets apparent temperature in degrees Celsius, or null when unavailable.
	/// </summary>
	public decimal? FeelsLikeCelsius
		{
		get;
		}
	/// <summary>
	/// Gets minimum temperature in degrees Celsius, or null when unavailable.
	/// </summary>
	public decimal? MinimumCelsius
		{
		get;
		}
	/// <summary>
	/// Gets maximum temperature in degrees Celsius, or null when unavailable.
	/// </summary>
	public decimal? MaximumCelsius
		{
		get;
		}
	/// <summary>
	/// Gets minimum apparent temperature in degrees Celsius, or null when unavailable.
	/// </summary>
	public decimal? MinimumFeelsLikeCelsius
		{
		get;
		}
	/// <summary>
	/// Gets maximum apparent temperature in degrees Celsius, or null when unavailable.
	/// </summary>
	public decimal? MaximumFeelsLikeCelsius
		{
		get;
		}
	/// <summary>
	/// Gets relative humidity in percent, or null when unavailable.
	/// </summary>
	public decimal? HumidityPercent
		{
		get;
		}
	/// <summary>
	/// Gets precipitation probability in percent, or null when unavailable.
	/// </summary>
	public decimal? RainProbabilityPercent
		{
		get;
		}
	/// <summary>
	/// Gets wind speed in kilometres per hour, or null when unavailable.
	/// </summary>
	public decimal? WindSpeedKilometresPerHour
		{
		get;
		}
	/// <summary>
	/// Gets the unitless UV index, or null when unavailable.
	/// </summary>
	public decimal? UltravioletIndex
		{
		get;
		}
	/// <summary>
	/// Gets cloud cover in percent, or null when unavailable.
	/// </summary>
	public decimal? CloudCoverPercent
		{
		get;
		}
	/// <summary>
	/// Gets the vendor weather-condition code, or null when unavailable.
	/// </summary>
	public string? WeatherCode
		{
		get;
		}
	/// <summary>
	/// Gets the vendor wind-direction description, or null when unavailable.
	/// </summary>
	public string? WindDirection
		{
		get;
		}
	/// <summary>
	/// Gets the reading's expiry instant, or null for an absent or invalid expiry.
	/// </summary>
	public DateTimeOffset? ExpiresAt
		{
		get;
		}
	/// <summary>Vendor-local forecast date/time, without assuming the computer's time zone.</summary>
	public DateTime? ForecastDate
		{
		get;
		}
	/// <summary>
	/// Gets the supplied forecast wall-clock time without applying the computer's local time zone.
	/// </summary>
	public DateTime? ForecastLocalTime
		{
		get;
		}
	/// <summary>
	/// Gets the supplied sunrise value, or null when unavailable.
	/// </summary>
	public TimeSpan? Sunrise
		{
		get;
		}
	/// <summary>
	/// Gets the supplied sunset value, or null when unavailable.
	/// </summary>
	public TimeSpan? Sunset
		{
		get;
		}
	private static decimal? Celsius (string? text)
		{
		var value = Number (text, -150, 180);
		return value.HasValue ? (value.Value - 32m) * 5m / 9m : null;
		}
	private static decimal? Number (string? text, decimal min, decimal max) => decimal.TryParse (text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value) && value >= min && value <= max ? value : null;
	private static TimeSpan? Clock (string? text) => TimeSpan.TryParseExact (text, @"hh\:mm", CultureInfo.InvariantCulture, out TimeSpan value) && value < TimeSpan.FromDays (1) ? value : null;
	/// <summary>
	/// Converts a usable Unix-seconds value to an instant, returning null when absent or outside the supported range.
	/// </summary>
	/// <param name="seconds">A Unix-seconds timestamp, or null when absent.</param>
	/// <returns>The timestamp as an instant, or null when absent or outside the supported range.</returns>
	internal static DateTimeOffset? Instant (long? seconds) => seconds is > 0 and <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds (seconds.Value) : null;
	}
/// <summary>
/// Contains current, hourly and daily vendor weather data with their cache-expiry metadata.
/// </summary>
public sealed class RainPointWeather
	{
	/// <summary>
	/// Initializes weather from the supplied typed values.
	/// </summary>
	/// <param name="wire">The attributed protocol response from which to create the typed observation.</param>
	internal RainPointWeather (WeatherResponse wire)
		{
		Current = wire.Current is null ? null : new (wire.Current);
		Hourly = Array.AsReadOnly ((wire.Hourly?.Items ?? []).Select (v => new RainPointWeatherReading (v)).ToArray ());
		Daily = Array.AsReadOnly ((wire.Daily?.Items ?? []).Select (v => new RainPointWeatherReading (v)).ToArray ());
		HourlyExpiresAt = RainPointWeatherReading.Instant (wire.Hourly?.Expires);
		DailyExpiresAt = RainPointWeatherReading.Instant (wire.Daily?.Expires);
		}
	/// <summary>
	/// Gets the current weather reading, or null when not supplied.
	/// </summary>
	public RainPointWeatherReading? Current
		{
		get;
		}
	/// <summary>
	/// Gets the supplied hourly forecast readings; missing periods are not synthesized.
	/// </summary>
	public IReadOnlyList<RainPointWeatherReading> Hourly
		{
		get;
		}
	/// <summary>
	/// Gets the supplied daily forecast readings; missing periods are not synthesized.
	/// </summary>
	public IReadOnlyList<RainPointWeatherReading> Daily
		{
		get;
		}
	/// <summary>
	/// Gets the hourly series expiry instant, or null for an absent or invalid expiry.
	/// </summary>
	public DateTimeOffset? HourlyExpiresAt
		{
		get;
		}
	/// <summary>
	/// Gets the daily series expiry instant, or null for an absent or invalid expiry.
	/// </summary>
	public DateTimeOffset? DailyExpiresAt
		{
		get;
		}
	}
public sealed partial class RainPointCloudClient
	{
	/// <summary>Reads the vendor weather service for the home's coordinates, up to 48 hours and 7 days.
	/// Requires separately supplied weather signing credentials; no vendor secret is embedded.</summary>
	/// <param name="home">A current home-management observation from the authenticated session.</param>
	/// <param name="access">Caller-supplied weather-service signing credentials, separate from the RainPoint login.</param>
	/// <param name="hours">The requested hourly-forecast horizon, up to 48 hours.</param>
	/// <param name="days">The requested daily-forecast horizon, up to seven days.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the typed weather result.</returns>
	/// <exception cref="System.ArgumentNullException">A required argument is null.</exception>
	/// <exception cref="System.ArgumentException">The home has no usable coordinates.</exception>
	/// <exception cref="System.ArgumentOutOfRangeException">An argument is outside the supported range described above.</exception>
	/// <exception cref="System.InvalidOperationException">Read the home in this session before requesting weather.</exception>
	/// <exception cref="RainPointException">Weather response contained a null forecast item. The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task<RainPointWeather> GetWeatherAsync (RainPointHomeDetails home, RainPointWeatherAccess access, int hours = 48, int days = 7, CancellationToken cancellationToken = default)
		{
		if (home is null)
			throw new ArgumentNullException (nameof (home));
		if (access is null)
			throw new ArgumentNullException (nameof (access));
		if (!home.Latitude.HasValue || !home.Longitude.HasValue)
			throw new ArgumentException ("The home has no usable coordinates.", nameof (home));
		if (hours is < 0 or > 48)
			throw new ArgumentOutOfRangeException (nameof (hours));
		if (days is < 0 or > 7)
			throw new ArgumentOutOfRangeException (nameof (days));
		if (!ReferenceEquals (home.Session, GetSession ()))
			throw new InvalidOperationException ("Read the home in this session before requesting weather.");
		long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds ();
		string nonce = Guid.NewGuid ().ToString ("N") + Guid.NewGuid ().ToString ("N").Substring (0, 18);
		string version = string.Join (",", new[] { hours > 0 ? "av2.h1a" : null, days > 0 ? "av2.d1a" : null }.Where (s => s is not null));
		string query = "weather/get?accessKey=" + Uri.EscapeDataString (access.Key) + "&timestamp=" + timestamp.ToString (CultureInfo.InvariantCulture)
		 + "&signatureNonce=" + nonce + "&signature=" + Uri.EscapeDataString (access.Sign (timestamp, nonce))
		 + "&lat=" + home.Wire.Latitude!.Value.ToString (CultureInfo.InvariantCulture) + "&lon=" + home.Wire.Longitude!.Value.ToString (CultureInfo.InvariantCulture)
		 + "&locationKey=&ver=" + Uri.EscapeDataString (version) + "&hours=" + hours.ToString (CultureInfo.InvariantCulture) + "&days=" + days.ToString (CultureInfo.InvariantCulture);
		var result = await GetAsync<WeatherResponse> (query, cancellationToken).ConfigureAwait (false);
		if ((result.Hourly?.Items?.Any (i => i is null) ?? false) || (result.Daily?.Items?.Any (i => i is null) ?? false))
			throw new RainPointException ("Weather response contained a null forecast item.");
		return new (result);
		}
	}
/// <summary>
/// Internal weather response representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class WeatherResponse
	{
	/// <summary>
	/// Stores the current protocol field for weather response.
	/// </summary>
	[JsonPropertyName ("current")]
	public WeatherValue? Current
		{
		get; set;
		}
	/// <summary>
	/// Stores the hourly protocol field for weather response.
	/// </summary>
	[JsonPropertyName ("hourly")]
	public WeatherSeries? Hourly
		{
		get; set;
		}
	/// <summary>
	/// Stores the daily protocol field for weather response.
	/// </summary>
	[JsonPropertyName ("daily")]
	public WeatherSeries? Daily
		{
		get; set;
		}
	}
/// <summary>
/// Internal weather series representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class WeatherSeries
	{
	/// <summary>
	/// Stores the expires protocol field for weather series.
	/// </summary>
	[JsonPropertyName ("expires")]
	public long? Expires
		{
		get; set;
		}
	/// <summary>
	/// Stores the items protocol field for weather series.
	/// </summary>
	[JsonPropertyName ("items")]
	public List<WeatherValue>? Items
		{
		get; set;
		}
	}
/// <summary>
/// Internal weather value representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class WeatherValue
	{
	/// <summary>
	/// Stores the expires protocol field for weather value.
	/// </summary>
	[JsonPropertyName ("expires")]
	public long? Expires
		{
		get; set;
		}
	/// <summary>
	/// Stores the tempF protocol field for weather value.
	/// </summary>
	[JsonPropertyName ("tempF"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? Temperature
		{
		get; set;
		}
	/// <summary>
	/// Stores the realFeelF protocol field for weather value.
	/// </summary>
	[JsonPropertyName ("realFeelF"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? FeelsLike
		{
		get; set;
		}
	/// <summary>
	/// Stores the tempMinF protocol field for weather value.
	/// </summary>
	[JsonPropertyName ("tempMinF"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? Minimum
		{
		get; set;
		}
	/// <summary>
	/// Stores the tempMaxF protocol field for weather value.
	/// </summary>
	[JsonPropertyName ("tempMaxF"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? Maximum
		{
		get; set;
		}
	/// <summary>
	/// Stores the realFeelMinF protocol field for weather value.
	/// </summary>
	[JsonPropertyName ("realFeelMinF"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? MinimumFeelsLike
		{
		get; set;
		}
	/// <summary>
	/// Stores the realFeelMaxF protocol field for weather value.
	/// </summary>
	[JsonPropertyName ("realFeelMaxF"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? MaximumFeelsLike
		{
		get; set;
		}
	/// <summary>
	/// Stores the humidity protocol field for weather value.
	/// </summary>
	[JsonPropertyName ("humidity"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? Humidity
		{
		get; set;
		}
	/// <summary>
	/// Stores the chanceofrain protocol field for weather value.
	/// </summary>
	[JsonPropertyName ("chanceofrain"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? RainProbability
		{
		get; set;
		}
	/// <summary>
	/// Stores the windSpeedKmph protocol field for weather value.
	/// </summary>
	[JsonPropertyName ("windSpeedKmph"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? WindSpeed
		{
		get; set;
		}
	/// <summary>
	/// Stores the windDir protocol field for weather value.
	/// </summary>
	[JsonPropertyName ("windDir")]
	public string? WindDirection
		{
		get; set;
		}
	/// <summary>
	/// Stores the uvi protocol field for weather value.
	/// </summary>
	[JsonPropertyName ("uvi"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? UvIndex
		{
		get; set;
		}
	/// <summary>
	/// Stores the cloudcover protocol field for weather value.
	/// </summary>
	[JsonPropertyName ("cloudcover"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? CloudCover
		{
		get; set;
		}
	/// <summary>
	/// Stores the weatherId protocol field for weather value.
	/// </summary>
	[JsonPropertyName ("weatherId"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? WeatherCode
		{
		get; set;
		}
	/// <summary>
	/// Stores the date protocol field for weather value.
	/// </summary>
	[JsonPropertyName ("date")]
	public string? Date
		{
		get; set;
		}
	/// <summary>
	/// Stores the time protocol field for weather value.
	/// </summary>
	[JsonPropertyName ("time"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? Time
		{
		get; set;
		}
	/// <summary>
	/// Stores the sunrise protocol field for weather value.
	/// </summary>
	[JsonPropertyName ("sunrise")]
	public string? Sunrise
		{
		get; set;
		}
	/// <summary>
	/// Stores the sunset protocol field for weather value.
	/// </summary>
	[JsonPropertyName ("sunset")]
	public string? Sunset
		{
		get; set;
		}
	}
// The weather service mixes numeric tokens and text in the same measurement fields.
// Keep this primitive compatibility confined to attributed weather fields.
/// <summary>
/// Internal weather scalar converter representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class WeatherScalarConverter : JsonConverter<string>
	{
	/// <inheritdoc/>
	public override string? Read (ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
		if (reader.TokenType == JsonTokenType.String)
			return reader.GetString ();
		if (reader.TokenType == JsonTokenType.Number)
			return reader.TryGetDecimal (out decimal value) ? value.ToString (CultureInfo.InvariantCulture) : null;
		throw new JsonException ("Expected a weather number or text value.");
		}
	/// <inheritdoc/>
	public override void Write (Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue (value);
	}
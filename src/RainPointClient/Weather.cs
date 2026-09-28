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
	public RainPointWeatherAccess (string accessKey, string accessSecret)
		{
		if (string.IsNullOrWhiteSpace (accessKey) || accessKey.Any (c => !char.IsLetterOrDigit (c)))
			throw new ArgumentException ("Use an alphanumeric weather access key.", nameof (accessKey));
		if (string.IsNullOrEmpty (accessSecret))
			throw new ArgumentException ("Weather signing secret is required.", nameof (accessSecret));
		Key = accessKey;
		Secret = accessSecret;
		}
	internal string Key
		{
		get;
		}
	internal string Secret
		{
		get;
		}
	internal string Sign (long timestamp, string nonce)
		{
		using var mac = new HMACMD5 (Encoding.UTF8.GetBytes (Secret));
		return Convert.ToBase64String (mac.ComputeHash (Encoding.UTF8.GetBytes ("accessKey=" + Key + "&timestamp=" + timestamp.ToString (CultureInfo.InvariantCulture) + "&signatureNonce=" + nonce)));
		}
	}
/// <summary>A nullable weather observation or forecast. Missing/invalid readings remain unknown.</summary>
public sealed class RainPointWeatherReading
	{
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
	public decimal? TemperatureCelsius
		{
		get;
		}
	public decimal? FeelsLikeCelsius
		{
		get;
		}
	public decimal? MinimumCelsius
		{
		get;
		}
	public decimal? MaximumCelsius
		{
		get;
		}
	public decimal? MinimumFeelsLikeCelsius
		{
		get;
		}
	public decimal? MaximumFeelsLikeCelsius
		{
		get;
		}
	public decimal? HumidityPercent
		{
		get;
		}
	public decimal? RainProbabilityPercent
		{
		get;
		}
	public decimal? WindSpeedKilometresPerHour
		{
		get;
		}
	public decimal? UltravioletIndex
		{
		get;
		}
	public decimal? CloudCoverPercent
		{
		get;
		}
	public string? WeatherCode
		{
		get;
		}
	public string? WindDirection
		{
		get;
		}
	public DateTimeOffset? ExpiresAt
		{
		get;
		}
	/// <summary>Vendor-local forecast date/time, without assuming the computer's time zone.</summary>
	public DateTime? ForecastDate
		{
		get;
		}
	public DateTime? ForecastLocalTime
		{
		get;
		}
	public TimeSpan? Sunrise
		{
		get;
		}
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
	internal static DateTimeOffset? Instant (long? seconds) => seconds is > 0 and <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds (seconds.Value) : null;
	}
public sealed class RainPointWeather
	{
	internal RainPointWeather (WeatherResponse wire)
		{
		Current = wire.Current is null ? null : new (wire.Current);
		Hourly = Array.AsReadOnly ((wire.Hourly?.Items ?? []).Select (v => new RainPointWeatherReading (v)).ToArray ());
		Daily = Array.AsReadOnly ((wire.Daily?.Items ?? []).Select (v => new RainPointWeatherReading (v)).ToArray ());
		HourlyExpiresAt = RainPointWeatherReading.Instant (wire.Hourly?.Expires);
		DailyExpiresAt = RainPointWeatherReading.Instant (wire.Daily?.Expires);
		}
	public RainPointWeatherReading? Current
		{
		get;
		}
	public IReadOnlyList<RainPointWeatherReading> Hourly
		{
		get;
		}
	public IReadOnlyList<RainPointWeatherReading> Daily
		{
		get;
		}
	public DateTimeOffset? HourlyExpiresAt
		{
		get;
		}
	public DateTimeOffset? DailyExpiresAt
		{
		get;
		}
	}
public sealed partial class RainPointCloudClient
	{
	/// <summary>Reads the vendor weather service for the home's coordinates, up to 48 hours and 7 days.
	/// Requires separately supplied weather signing credentials; no vendor secret is embedded.</summary>
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
internal sealed class WeatherResponse
	{
	[JsonPropertyName ("current")]
	public WeatherValue? Current
		{
		get; set;
		}
	[JsonPropertyName ("hourly")]
	public WeatherSeries? Hourly
		{
		get; set;
		}
	[JsonPropertyName ("daily")]
	public WeatherSeries? Daily
		{
		get; set;
		}
	}
internal sealed class WeatherSeries
	{
	[JsonPropertyName ("expires")]
	public long? Expires
		{
		get; set;
		}
	[JsonPropertyName ("items")]
	public List<WeatherValue>? Items
		{
		get; set;
		}
	}
internal sealed class WeatherValue
	{
	[JsonPropertyName ("expires")]
	public long? Expires
		{
		get; set;
		}
	[JsonPropertyName ("tempF"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? Temperature
		{
		get; set;
		}
	[JsonPropertyName ("realFeelF"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? FeelsLike
		{
		get; set;
		}
	[JsonPropertyName ("tempMinF"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? Minimum
		{
		get; set;
		}
	[JsonPropertyName ("tempMaxF"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? Maximum
		{
		get; set;
		}
	[JsonPropertyName ("realFeelMinF"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? MinimumFeelsLike
		{
		get; set;
		}
	[JsonPropertyName ("realFeelMaxF"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? MaximumFeelsLike
		{
		get; set;
		}
	[JsonPropertyName ("humidity"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? Humidity
		{
		get; set;
		}
	[JsonPropertyName ("chanceofrain"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? RainProbability
		{
		get; set;
		}
	[JsonPropertyName ("windSpeedKmph"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? WindSpeed
		{
		get; set;
		}
	[JsonPropertyName ("windDir")]
	public string? WindDirection
		{
		get; set;
		}
	[JsonPropertyName ("uvi"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? UvIndex
		{
		get; set;
		}
	[JsonPropertyName ("cloudcover"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? CloudCover
		{
		get; set;
		}
	[JsonPropertyName ("weatherId"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? WeatherCode
		{
		get; set;
		}
	[JsonPropertyName ("date")]
	public string? Date
		{
		get; set;
		}
	[JsonPropertyName ("time"), JsonConverter (typeof (WeatherScalarConverter))]
	public string? Time
		{
		get; set;
		}
	[JsonPropertyName ("sunrise")]
	public string? Sunrise
		{
		get; set;
		}
	[JsonPropertyName ("sunset")]
	public string? Sunset
		{
		get; set;
		}
	}
// The weather service mixes numeric tokens and text in the same measurement fields.
// Keep this primitive compatibility confined to attributed weather fields.
internal sealed class WeatherScalarConverter : JsonConverter<string>
	{
	public override string? Read (ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
		if (reader.TokenType == JsonTokenType.String)
			return reader.GetString ();
		if (reader.TokenType == JsonTokenType.Number)
			return reader.TryGetDecimal (out decimal value) ? value.ToString (CultureInfo.InvariantCulture) : null;
		throw new JsonException ("Expected a weather number or text value.");
		}
	public override void Write (Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue (value);
	}
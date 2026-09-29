// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using NUnit.Framework;
namespace RainPointClient.Tests;

[TestFixture]
public sealed class WeatherTests
	{
	[Test]
	public void WeatherSigningMatchesIndependentHmacVector ()
		{
		var access = new RainPointWeatherAccess ("fixtureKey", "fixtureSecret");
		Assert.That (access.Sign (1700000000000, "nonce"), Is.EqualTo ("XPwk28SOm44ats0qLg9fYA=="));
		}
	[Test]
	public void UnitsAndHourlyDatesAreExplicit ()
		{
		var reading = new RainPointWeatherReading (new WeatherValue { Temperature = "68", FeelsLike = "59", Humidity = "75", RainProbability = "40", WindSpeed = "12.3", Date = "2026-09-24", Time = "14", Sunrise = "06:45", Sunset = "19:12", Expires = 1700000000 });
		Assert.That (reading.TemperatureCelsius, Is.EqualTo (20m));
		Assert.That (reading.FeelsLikeCelsius, Is.EqualTo (15m));
		Assert.That (reading.ForecastLocalTime, Is.EqualTo (new DateTime (2026, 9, 24, 14, 0, 0, DateTimeKind.Unspecified)));
		Assert.That (reading.ForecastLocalTime!.Value.Kind, Is.EqualTo (DateTimeKind.Unspecified));
		Assert.That (reading.RainProbabilityPercent, Is.EqualTo (40));
		Assert.That (reading.WindSpeedKilometresPerHour, Is.EqualTo (12.3m));
		Assert.That (reading.Sunrise, Is.EqualTo (TimeSpan.FromMinutes (405)));
		Assert.That (reading.ExpiresAt, Is.EqualTo (DateTimeOffset.FromUnixTimeSeconds (1700000000)));
		}
	[TestCase (null)]
	[TestCase ("")]
	[TestCase ("N/A")]
	[TestCase ("NaN")]
	[TestCase ("99999999999999999999999999999999999999")]
	public void UnusableWeatherMeasurementsRemainUnknown (string? text)
		{
		var reading = new RainPointWeatherReading (new WeatherValue { Temperature = text, Humidity = text, RainProbability = text });
		Assert.That (reading.TemperatureCelsius, Is.Null);
		Assert.That (reading.HumidityPercent, Is.Null);
		Assert.That (reading.RainProbabilityPercent, Is.Null);
		}
	[Test]
	public void ImplausiblePercentagesAndTimesRemainUnknown ()
		{
		var reading = new RainPointWeatherReading (new WeatherValue { Humidity = "101", RainProbability = "-1", Date = "2026-09-24", Time = "24", Expires = long.MaxValue });
		Assert.That (reading.HumidityPercent, Is.Null);
		Assert.That (reading.RainProbabilityPercent, Is.Null);
		Assert.That (reading.ForecastLocalTime, Is.Null);
		Assert.That (reading.ExpiresAt, Is.Null);
		}
	[TestCase (true)]
	[TestCase (false)]
	public async Task RequestIsSignedBoundedAndReadOnly (bool includeCurrent)
		{
		using var handler = new ScriptedHandler ();
		using var http = new HttpClient (handler, false);
		using var client = new RainPointCloudClient (http);
		handler.Reply ("""{"code":0,"data":{"token":"fixture","tokenExpired":3600}}""");
		await client.LoginAsync ("fixture@example.invalid", "fixture", "44");
		handler.Reply (AdministrationTests.HOME);
		var home = await client.GetHomeAsync (5);
		handler.Reply ("""{"code":0,"data":{"current":{"tempF":"68"},"hourly":{"items":[{"tempF":"70","date":"2026-09-24","time":"14"}]},"daily":{"items":[{"tempMinF":"50","tempMaxF":"68","chanceofrain":"80"}]}}}""".Replace ("\"current\":{\"tempF\":\"68\"},", includeCurrent ? "\"current\":{\"tempF\":\"68\"}," : string.Empty));
		var weather = await client.GetWeatherAsync (home, new ("fixtureKey", "fixtureSecret"));
		Assert.That (weather.Current?.TemperatureCelsius, Is.EqualTo (includeCurrent ? 20m : (decimal?)null));
		Assert.That (weather.Hourly, Has.Count.EqualTo (1));
		Assert.That (weather.Daily.Single ().MinimumCelsius, Is.EqualTo (10));
		Assert.That (handler.Requests.Last ().Method, Is.EqualTo (HttpMethod.Get));
		Assert.That (handler.Requests.Last ().Path, Does.StartWith ("/weather/get?accessKey=fixtureKey&timestamp="));
		Assert.That (handler.Requests.Last ().Path, Does.Contain ("hours=48&days=7"));
		await Assert.ThrowsAsync<ArgumentOutOfRangeException> (async () => await client.GetWeatherAsync (home, new ("fixtureKey", "fixtureSecret"), 49));
		Assert.That (handler.Requests, Has.Count.EqualTo (3));
		}
	[Test]
	public void MixedWeatherNumbersAndTextDecodeWithoutLosingUnits ()
		{
		const string json = """{"tempF":68,"realFeelF":"59","humidity":75,"chanceofrain":40,"windSpeedKmph":12.3,"weatherId":7,"uvi":1.2,"cloudcover":65,"date":"2026-09-24","time":14} """;
		var wire = System.Text.Json.JsonSerializer.Deserialize<WeatherValue> (json)!;
		var reading = new RainPointWeatherReading (wire);
		Assert.That (reading.TemperatureCelsius, Is.EqualTo (20));
		Assert.That (reading.FeelsLikeCelsius, Is.EqualTo (15));
		Assert.That (reading.RainProbabilityPercent, Is.EqualTo (40));
		Assert.That (reading.WindSpeedKilometresPerHour, Is.EqualTo (12.3m));
		Assert.That (reading.WeatherCode, Is.EqualTo ("7"));
		Assert.That (reading.ForecastLocalTime, Is.EqualTo (new DateTime (2026, 9, 24, 14, 0, 0)));
		}
	[TestCase ("null")]
	[TestCase ("1e100")]
	[TestCase ("\"N/A\"")]
	public void UnavailableNumericWeatherValuesRemainUnknown (string value)
		{
		var wire = System.Text.Json.JsonSerializer.Deserialize<WeatherValue> ("{\"chanceofrain\":" + value + "}")!;
		Assert.That (new RainPointWeatherReading (wire).RainProbabilityPercent, Is.Null);
		}
	[TestCase ("true")]
	[TestCase ("{}")]
	[TestCase ("[]")]
	public void StructuredWeatherMeasurementIsRejected (string value) =>
	 Assert.Throws<System.Text.Json.JsonException> (() => System.Text.Json.JsonSerializer.Deserialize<WeatherValue> ("{\"chanceofrain\":" + value + "}"));

	}
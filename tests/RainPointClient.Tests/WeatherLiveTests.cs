using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using RainPointClient.Protocol;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
namespace RainPointClient.Tests;

[TestFixture, NonParallelizable, Category ("Live")]
public sealed class WeatherLiveTests
	{
	[Test, Explicit ("Read-only weather check. Requires private account settings and process-local weather access credentials.")]
	public async Task ReadsWeatherWithoutConfigurationOrValveWrites ()
		{
		string? path = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS"), key = Environment.GetEnvironmentVariable ("RAINPOINT_WEATHER_ACCESS_KEY"), secret = Environment.GetEnvironmentVariable ("RAINPOINT_WEATHER_ACCESS_SECRET");
		if (string.IsNullOrWhiteSpace (path) || string.IsNullOrWhiteSpace (key) || string.IsNullOrWhiteSpace (secret))
			Assert.Ignore ("Private account and weather access configuration required.");
		var account = JsonSerializer.Deserialize<Account> (File.ReadAllText (path!))!;
		using var http = new HttpClient (new WeatherSchemaHandler ()) { Timeout = TimeSpan.FromSeconds (30), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
		using var client = new RainPointCloudClient (http);
		using var timeout = new CancellationTokenSource (TimeSpan.FromSeconds (90));
		await client.LoginAsync (account.Email, account.Password, account.AreaCode, timeout.Token);
		RainPointHomeDetails? selected = null;
		foreach (var entry in await client.GetHomesAsync (timeout.Token))
			{
			var home = await client.GetHomeAsync (entry.Id, timeout.Token);
			if (home.Latitude.HasValue && home.Longitude.HasValue && (home.Latitude != 0 || home.Longitude != 0))
				{
				selected = home;
				break;
				}
			}
		Assert.That (selected, Is.Not.Null, "A configured home location is required.");
		var weather = await client.GetWeatherAsync (selected!, new (key!, secret!), cancellationToken: timeout.Token);
		if (weather.Current is not null)
			Assert.That (weather.Current.TemperatureCelsius, Is.Not.Null);
		Assert.That (weather.Hourly, Is.Not.Empty);
		Assert.That (weather.Daily, Is.Not.Empty);
		Assert.That (weather.Hourly.Any (h => h.ForecastLocalTime.HasValue && h.TemperatureCelsius.HasValue), Is.True);
		Assert.That (weather.Daily.Any (d => d.MinimumCelsius.HasValue && d.MaximumCelsius.HasValue), Is.True);
		TestContext.Progress.WriteLine ($"Weather decoded: current supplied: {weather.Current is not null}; {weather.Hourly.Count} hourly and {weather.Daily.Count} daily entries. No configuration, notification or valve writes.");
		}
	// Only report schema paths/types; never write payloads, request URLs or service credentials.
	private sealed class WeatherSchemaHandler : DelegatingHandler
		{
		internal WeatherSchemaHandler () : base (new HttpClientHandler { AllowAutoRedirect = false }) { }
		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken token)
			{
			var response = await base.SendAsync (request, token);
			if (request.RequestUri!.AbsolutePath == "/weather/get" && response.IsSuccessStatusCode)
				{
				string body = await response.Content.ReadAsStringAsync ();
				try
					{
					JsonSerializer.Deserialize<ApiResult<WeatherResponse>> (body, new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowReadingFromString });
					}
				catch (JsonException error) { TestContext.Progress.WriteLine ("Weather schema mismatch at " + error.Path); }
				}
			return response;
			}
		}
	private sealed class Account
		{
		[JsonPropertyName ("email")] public string Email { get; set; } = string.Empty;
		[JsonPropertyName ("password")] public string Password { get; set; } = string.Empty;
		[JsonPropertyName ("areaCode")] public string AreaCode { get; set; } = "44";
		}
	}
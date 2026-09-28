# Home weather

`GetWeatherAsync` reads hourly/daily forecasts and an optional current-weather observation for a configured home location. The attributed models return nullable temperatures in Celsius, wind speed in kilometres per hour, percentages, weather codes, sunrise/sunset, forecast dates and expiry timestamps. Invalid or absent values remain unknown. Hourly forecast time is an hour from 0 to 23, combined with its home-local date; no UTC offset is invented.

Weather-service signing access is separate from RainPoint account authentication. Supply a `RainPointWeatherAccess` instance from the host's private configuration. The client contains no embedded vendor signing secret and does not persist or log one.

```csharp
var home = await client.GetHomeAsync(homeId, cancellationToken);
var access = new RainPointWeatherAccess(weatherAccessKey, weatherAccessSecret);
var forecast = await client.GetWeatherAsync(home, access,
    hours: 48, days: 7, cancellationToken: cancellationToken);
```

The request contract uses the app's signed weather endpoint, with a timestamp and nonce. Signature construction, query escaping, coordinate conversion, Fahrenheit-to-Celsius conversion, hourly dates, malformed fields and cancellation are covered offline. Forecasts are observations, not proof that a weather-triggered scene will execute.

## Windows app

The Weather tab displays current, hourly and daily readings. At startup it optionally reads `RAINPOINT_WEATHER_ACCESS_KEY` and `RAINPOINT_WEATHER_ACCESS_SECRET` from the process environment. Without both values, weather is unavailable; ordinary sign-in does not create weather signing access. Changing homes or a failed load clears previous readings so they cannot appear to be fresh data for another home. WPF and dashboard tests use fictitious signing credentials and simulated responses.

## Live validation

`WeatherLiveTests.ReadsWeatherWithoutConfigurationOrValveWrites` is an explicit, nonparallel NUnit test. It requires private `RAINPOINT_LIVE_SETTINGS` and the two weather environment variables. It only signs in and reads configured-home metadata and weather; it sends no configuration, notification or valve commands.

The initial attempts on both targets timed out at sign-in. After connectivity returned, a live read exposed numeric tokens in fields also represented as text. Narrow attributed weather-field converters and eight additional regression cases now cover mixed numbers/text, invalid values and forecast-only responses. No JSON DOM or public raw-payload API was added to the library.

The corrected explicit test passed on net10.0 and net472 on 24 September 2026, reading 48 hourly and seven daily forecasts with decoded temperatures and dates. The service supplied no current-observation block in those responses: `Current` remains null, and no forecast is relabelled as an observation. Current-observation live decoding remains unverified. Earlier timeout/schema failures and successful corrected results are all retained under ignored `artifacts/weather-live`. No configuration, notification or valve write was sent.

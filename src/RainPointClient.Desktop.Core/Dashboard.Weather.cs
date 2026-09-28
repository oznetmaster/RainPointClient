// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
namespace RainPointClient.Desktop.Core;

public sealed partial class Dashboard
	{
	private RainPointWeatherAccess? _weatherAccess;
	public RainPointWeatherReading? CurrentWeather
		{
		get; private set;
		}
	public ObservableCollection<RainPointWeatherReading> HourlyWeather { get; } = new ();
	public ObservableCollection<RainPointWeatherReading> DailyWeather { get; } = new ();
	public string WeatherMessage { get; private set; } = "Weather service access must be configured separately from account sign-in.";
	public bool CanReadWeather => CanReadAdministration && _weatherAccess is not null;
	public void ConfigureWeather (RainPointWeatherAccess? access)
		{
		if (!CanEdit)
			throw new InvalidOperationException ("Wait for the current operation before changing weather access.");
		_weatherAccess = access;
		ClearWeather ();
		Changed ();
		}
	private void ClearWeather ()
		{
		CurrentWeather = null;
		HourlyWeather.Clear ();
		DailyWeather.Clear ();
		WeatherMessage = _weatherAccess is null ? "Weather service access has not been configured." : "Load weather for the selected home's location.";
		}
	public Task LoadWeatherAsync ()
		{
		if (!CanReadWeather)
			return Task.CompletedTask;
		long homeId = _home!.Id;
		var access = _weatherAccess!;
		return RunAsync (async token =>
		{
			ClearWeather ();
			var home = await _client.GetHomeAsync (homeId, token);
			var weather = await _client.GetWeatherAsync (home, access, cancellationToken: token);
			CurrentWeather = weather.Current;
			foreach (var item in weather.Hourly)
				HourlyWeather.Add (item);
			foreach (var item in weather.Daily)
				DailyWeather.Add (item);
			WeatherMessage = "Vendor weather loaded. Temperatures are °C, wind is km/h and forecast times are local to the location. This is a forecast, not a sensor reading.";
		}, "Weather could not be loaded. Retry explicitly; verify service access and the home's location.");
		}
	}
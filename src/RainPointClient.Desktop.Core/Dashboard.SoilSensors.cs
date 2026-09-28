// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace RainPointClient.Desktop.Core;

public sealed partial class Dashboard
	{
	private SoilSensorChoice? _soilSensor;
	public ObservableCollection<SoilSensorChoice> SoilSensors { get; } = new ();
	public string SavedSoilSettings { get; private set; } = "Unknown";
	public bool CanLoadSoilSensors => CanReadSettings && _settings?.SoilSensorAvailability == TimerReadingAvailability.Decoded;
	public bool CanSaveSoilSensor => CanLoadSoilSensors && _soilSensor is not null && SoilSensors.Contains (_soilSensor);
	public SoilSensorChoice? SelectedSoilSensor
		{
		get => _soilSensor;
		set
			{
			if (!CanEdit || (value is not null && !SoilSensors.Contains (value)))
				return;
			_soilSensor = value;
			Changed ();
			}
		}
	private void ClearSoilChoices ()
		{
		_soilSensor = null;
		SoilSensors.Clear ();
		}
	public Task LoadSoilSensorsAsync ()
		{
		if (!CanLoadSoilSensors)
			return Task.CompletedTask;
		var snapshot = _settings!;
		return RunAsync (async token =>
			{
				ClearSoilChoices ();
				try
					{
					var sensors = await _client.GetAvailableSoilSensorsAsync (_hub!, snapshot, token);
					SoilSensors.Add (new SoilSensorChoice (null, "None (remove association)"));
					foreach (var sensor in sensors)
						SoilSensors.Add (new SoilSensorChoice (sensor.Address, string.IsNullOrWhiteSpace (sensor.Name) ? sensor.Model : sensor.Name));
					_soilSensor = SoilSensors.FirstOrDefault (s => s.Address == snapshot.SoilSensorSettings!.SensorAddress);
					SettingsMessage = "Choose an already paired sensor. Removing an association preserves its saved moisture threshold.";
					}
				catch { SettingsMessage = "Sensor choices could not be verified. Reload settings before editing."; _settings = null; throw; }
				Changed ();
			}, "Sensor selection did not complete.");
		}
	public Task SaveSoilSensorAsync ()
		{
		if (!CanSaveSoilSensor)
			return Task.CompletedTask;
		var snapshot = _settings!;
		var hub = _hub!;
		int? address = _soilSensor!.Address;
		return RunAsync (async token =>
			{
				_settings = null;
				ClearSoilChoices ();
				ClearCalendar ();
				Changed ();
				try
					{
					await _client.SetTimerSoilSensorAsync (hub, snapshot, address, token);
					var after = await _client.GetTimerSchedulesAsync (hub, snapshot.Address, snapshot.Zone, token);
					ShowSettings (after);
					bool matches = after.SoilSensorAvailability == TimerReadingAvailability.Decoded && after.SoilSensorSettings!.SensorAddress == address;
					if (!matches)
						_settings = null;
					SettingsMessage = matches ? "Sensor association matches cloud read-back. No watering command was sent." : "Cloud read-back differs. Reload before another attempt.";
					}
				catch { _settings = null; SettingsMessage = "Association outcome may be unknown. Reload before another attempt; nothing was retried."; throw; }
				Changed ();
			}, "Sensor association did not complete.");
		}
	}
public sealed class SoilSensorChoice
	{
	internal SoilSensorChoice (int? address, string name)
		{
		Address = address;
		Name = name;
		}
	public int? Address
		{
		get;
		}
	public string Name
		{
		get;
		}
	}
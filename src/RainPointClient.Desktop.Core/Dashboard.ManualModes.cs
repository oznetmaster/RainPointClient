// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace RainPointClient.Desktop.Core;

public sealed partial class Dashboard
	{
	private int _manualMode;
	private string _manualBurst = "10";
	private string _manualPause = "20";
	public string[] ManualModes => ["Normal", "Misting", "Cycle and soak"];
	public int ManualMode
		{
		get => _manualMode;
		set
			{
			if (!CanEdit || value is < 0 or > 2 || value == _manualMode)
				return;
			_manualMode = value;
			_armed = false;
			_durationMinutes = value == 2 ? "5" : "1";
			_manualBurst = value == 2 ? "1" : "10";
			_manualPause = value == 2 ? "1" : "20";
			CommandMessage = "No command sent for this mode.";
			Changed ();
			}
		}
	public string ManualBurst
		{
		get => _manualBurst;
		set
			{
			if (!CanEdit)
				return;
			_manualBurst = value;
			Changed ();
			}
		}
	public string ManualPause
		{
		get => _manualPause;
		set
			{
			if (!CanEdit)
				return;
			_manualPause = value;
			Changed ();
			}
		}
	public bool CanEditManualIntervals => CanEdit && _manualMode != 0;
	public string ManualIntervalUnit => _manualMode == 2 ? "minutes (1–720)" : "seconds (5–3600)";
	public string ManualDurationLabel => _manualMode == 2 ? "Watering minutes (5–1440)" : "Duration minutes (1–720)";
	public string ManualModeHint => _manualMode == 0 ? "Continuous watering for the selected duration."
		 : _timer?.SupportsManualCycles != true ? "This mode requires reported timer firmware 120 or newer. Refresh device discovery."
		 : _manualMode == 2 ? "Watering is split into bursts. Pauses extend elapsed time; a burst cannot exceed total watering."
		 : "Alternating watering and pauses. Physical timing remains to be verified.";

	private bool ValidManualSettings ()
		{
		if (!int.TryParse (_durationMinutes, NumberStyles.None, CultureInfo.InvariantCulture, out int duration)
			 || duration < (_manualMode == 2 ? 5 : 1) || duration > (_manualMode == 2 ? 1440 : 720))
			return false;
		if (_manualMode == 0)
			return true;
		return _timer?.SupportsManualCycles == true
			 && int.TryParse (_manualBurst, NumberStyles.None, CultureInfo.InvariantCulture, out int burst)
			 && int.TryParse (_manualPause, NumberStyles.None, CultureInfo.InvariantCulture, out int pause)
			 && burst >= (_manualMode == 2 ? 1 : 5) && burst <= (_manualMode == 2 ? Math.Min (720, duration) : 3600)
			 && pause >= (_manualMode == 2 ? 1 : 5) && pause <= (_manualMode == 2 ? 720 : 3600);
		}

	private Task<RainPointWateringCommandResult> StartManualAsync (RainPointHub hub, int address, int zone, CancellationToken token)
		{
		TimeSpan duration = TimeSpan.FromMinutes (int.Parse (_durationMinutes, CultureInfo.InvariantCulture));
		if (_manualMode == 0)
			return _client.StartWateringAsync (hub, address, zone, duration, token);
		int burst = int.Parse (_manualBurst, CultureInfo.InvariantCulture);
		int pause = int.Parse (_manualPause, CultureInfo.InvariantCulture);
		return _manualMode == 1
			 ? _client.StartMistingAsync (hub, address, zone, duration, TimeSpan.FromSeconds (burst), TimeSpan.FromSeconds (pause), token)
			 : _client.StartCycleAndSoakAsync (hub, address, zone, duration, TimeSpan.FromMinutes (burst), TimeSpan.FromMinutes (pause), token);
		}
	}
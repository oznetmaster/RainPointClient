using System.Globalization;
using System.Collections.Generic;

namespace RainPointClient.Desktop.Core;

public sealed class ZoneRow
	{
	public ZoneRow (int zone)
		{
		Zone = "Zone " + zone.ToString (CultureInfo.CurrentCulture);
		State = Usage = Duration = Alarm = EventTime = "Unknown";
		ControlAvailability = "Select zone in controls below";
		}

	public ZoneRow (RainPointZoneStatus reading)
		{
		Zone = "Zone " + reading.Zone.ToString (CultureInfo.CurrentCulture);
		State = reading.WorkMode == RainPointWateringMode.Misting ? "Reported misting"
				: reading.WorkMode == RainPointWateringMode.CycleAndSoak ? "Reported cycling"
				: reading.WorkMode == RainPointWateringMode.CycleAndSoakPause ? "Reported soaking (paused)"
				: reading.IsOpen switch
					{
						true => "Reported open",
						false => "Reported closed",
						_ => "Unknown"
						};
		Usage = reading.LastWaterUsageLitres?.ToString ("0.0##", CultureInfo.CurrentCulture) + (reading.LastWaterUsageLitres.HasValue ? " L" : "Unknown");
		Duration = reading.ConfiguredRunDuration.HasValue ? reading.ConfiguredRunDuration.Value.TotalSeconds.ToString (CultureInfo.CurrentCulture) + " s" : "Unknown";
		List<string> alarms = new ();
		if (reading.WaterLeakReported == true)
			alarms.Add ("Leak reported");
		if (reading.WaterShortageReported == true)
			alarms.Add ("Water shortage reported");
		if (reading.FreezeReported == true)
			alarms.Add ("Freeze reported");
		if (reading.UnknownAlarmBits > 0)
			alarms.Add ("Unknown bits: " + reading.UnknownAlarmBits.Value.ToString ("X2", CultureInfo.InvariantCulture));
		Alarm = !reading.AlarmCode.HasValue ? "Unknown" : alarms.Count == 0 ? "None reported" : string.Join (", ", alarms);
		EventTime = reading.EventTimeLocal?.ToString ("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture) ?? "Unknown";
		ControlAvailability = "Select zone in controls below";
		}

	public string Zone
		{
		get;
		}
	public string State
		{
		get;
		}
	public string Usage
		{
		get;
		}
	public string Duration
		{
		get;
		}
	public string Alarm
		{
		get;
		}
	public string EventTime
		{
		get;
		}
	public string ControlAvailability
		{
		get;
		}
	}
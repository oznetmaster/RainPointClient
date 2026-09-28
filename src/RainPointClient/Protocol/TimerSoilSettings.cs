using System;
using System.Globalization;

namespace RainPointClient.Protocol;

internal static class TimerSoilSettings
	{
	internal static void Decode (RainPointScheduleSnapshot snapshot)
		{
		snapshot.SoilSensorSettings = null;
		snapshot.SoilSensorAvailability = TimerReadingAvailability.NotReported;
		if (string.IsNullOrEmpty (snapshot.Parameter))
			return;
		if (snapshot.PortNumber != 3 || snapshot.Parameter!.Contains ("="))
			{
			snapshot.SoilSensorAvailability = TimerReadingAvailability.UnsupportedFormat;
			return;
			}
		string[] zones = snapshot.Parameter.Split ('|');
		if (zones.Length != 3 || snapshot.Zone is < 1 or > 3)
			{
			snapshot.SoilSensorAvailability = TimerReadingAvailability.Malformed;
			return;
			}
		string text = zones[snapshot.Zone - 1].Split (',')[0];
		if (text.Length == 0)
			return;
		if (text.Length < 24 || text.Length % 2 != 0)
			{
			snapshot.SoilSensorAvailability = TimerReadingAvailability.Malformed;
			return;
			}
		byte[] data = new byte[text.Length / 2];
		for (int i = 0; i < data.Length; i++)
			if (!byte.TryParse (text.Substring (2 * i, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out data[i]))
				{
				snapshot.SoilSensorAvailability = TimerReadingAvailability.Malformed;
				return;
				}
		int threshold = data[7] & 127;
		if (threshold > 100)
			{
			snapshot.SoilSensorAvailability = TimerReadingAvailability.Malformed;
			return;
			}
		snapshot.SoilSensorSettings = new RainPointSoilSensorSettings (data[6], threshold);
		snapshot.SoilSensorAvailability = TimerReadingAvailability.Decoded;
		}
	internal static string Edit (RainPointScheduleSnapshot snapshot, int? address, int? percent, bool association)
		{
		if (snapshot is null)
			throw new ArgumentNullException (nameof (snapshot));
		if (percent is < 1 or > 100)
			throw new ArgumentOutOfRangeException (nameof (percent));
		if (snapshot.Availability != TimerReadingAvailability.Decoded || snapshot.SoilSensorAvailability != TimerReadingAvailability.Decoded || snapshot.SoilSensorSettings is null
		 || snapshot.Parameter is null || snapshot.PortNumber != 3 || snapshot.Zone is < 1 or > 3 || !int.TryParse (snapshot.FirmwareVersion, out int firmware) || firmware < 120)
			throw new NotSupportedException ("Use readable modern sensor settings on firmware 120 or newer.");
		if (!association && percent.HasValue && snapshot.SoilSensorSettings.SensorAddress is null)
			throw new InvalidOperationException ("Associate a sensor before enabling moisture stop.");
		string[] zones = snapshot.Parameter.Split ('|');
		string[] fields = zones[snapshot.Zone - 1].Split (',');
		if (fields.Length < 4 || !zones[snapshot.Zone - 1].Contains ("/"))
			throw new NotSupportedException ("Only modern timer settings can be edited.");
		int offset = association ? 12 : 14;
		int value = association ? address ?? 0 : (int.Parse (fields[0].Substring (14, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) & 128) | (percent ?? 0);
		fields[0] = fields[0].Substring (0, offset) + value.ToString ("x2", CultureInfo.InvariantCulture) + fields[0].Substring (offset + 2);
		zones[snapshot.Zone - 1] = string.Join (",", fields);
		return string.Join ("|", zones);
		}
	}
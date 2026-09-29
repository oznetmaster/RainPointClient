// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Globalization;

namespace RainPointClient.Protocol;

/// <summary>
/// Decodes and edits the recognized flow-calibration percentage without changing unrelated configuration.
/// </summary>
internal static class TimerFlowCalibration
	{
	/// <summary>
	/// Populates the typed snapshot with decoded flow-calibration percentage and explicit section availability.
	/// </summary>
	/// <param name="snapshot">The observed zone configuration; unrelated encoded fields are preserved when editing.</param>
	internal static void Decode (RainPointScheduleSnapshot snapshot)
		{
		snapshot.FlowCalibrationPercent = null;
		snapshot.FlowCalibrationAvailability = TimerReadingAvailability.NotReported;
		if (string.IsNullOrEmpty (snapshot.Parameter))
			{
			return;
			}
		if (snapshot.PortNumber != 3 || snapshot.Parameter!.IndexOf ('=') >= 0)
			{
			snapshot.FlowCalibrationAvailability = TimerReadingAvailability.UnsupportedFormat;
			return;
			}
		string[] ports = snapshot.Parameter.Split ('|');
		if (ports.Length != 3 || snapshot.Zone is < 1 or > 3)
			{
			snapshot.FlowCalibrationAvailability = TimerReadingAvailability.Malformed;
			return;
			}
		string field = ports[snapshot.Zone - 1].Split (',')[0];
		if (field.Length == 0)
			{
			return;
			}
		if (field.Length % 2 != 0 || field.Length < 24 || field.Length == 26)
			{
			snapshot.FlowCalibrationAvailability = TimerReadingAvailability.Malformed;
			return;
			}
		byte correction = 0;
		for (int i = 0; i < field.Length; i += 2)
			{
			if (!byte.TryParse (field.Substring (i, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte value))
				{
				snapshot.FlowCalibrationAvailability = TimerReadingAvailability.Malformed;
				return;
				}
			if (i == 24)
				{
				correction = value;
				}
			}
		// The inspected app only recognizes calibration when both extension bytes exist.
		if (field.Length == 24)
			{
			return;
			}
		int percent = correction > 127 ? correction - 256 : correction;
		if (percent is < -20 or > 20)
			{
			snapshot.FlowCalibrationAvailability = TimerReadingAvailability.Malformed;
			return;
			}
		snapshot.FlowCalibrationPercent = percent;
		snapshot.FlowCalibrationAvailability = TimerReadingAvailability.Decoded;
		}

	/// <summary>
	/// Validates and replaces the selected flow-calibration percentage fields while preserving unrelated bytes.
	/// </summary>
	/// <param name="snapshot">The observed zone configuration; unrelated encoded fields are preserved when editing.</param>
	/// <param name="percentage">The signed flow-calibration correction in percent, from -20 through 20.</param>
	/// <returns>The complete updated parameter field.</returns>
	/// <exception cref="System.ArgumentNullException">A required argument is null.</exception>
	/// <exception cref="System.ArgumentOutOfRangeException">Use a whole correction percentage from -20 to 20.</exception>
	/// <exception cref="System.NotSupportedException">Use a decoded three-zone calibration snapshot with firmware 120 or newer. Unsupported timer configuration. A complete modern calibration field is required; missing defaults are not invented.</exception>
	internal static string Edit (RainPointScheduleSnapshot snapshot, int percentage)
		{
		if (snapshot is null)
			{
			throw new ArgumentNullException (nameof (snapshot));
			}
		if (percentage is < -20 or > 20)
			{
			throw new ArgumentOutOfRangeException (nameof (percentage), "Use a whole correction percentage from -20 to 20.");
			}
		if (snapshot.Availability != TimerReadingAvailability.Decoded || snapshot.FlowCalibrationAvailability != TimerReadingAvailability.Decoded
		 || !snapshot.FlowCalibrationPercent.HasValue || snapshot.Parameter is null || snapshot.PortNumber != 3 || snapshot.Zone is < 1 or > 3
		 || !int.TryParse (snapshot.FirmwareVersion, NumberStyles.None, CultureInfo.InvariantCulture, out int firmware) || firmware < 120)
			{
			throw new NotSupportedException ("Use a decoded three-zone calibration snapshot with firmware 120 or newer.");
			}
		string[] ports = snapshot.Parameter.Split ('|');
		if (ports.Length != 3 || snapshot.Parameter.IndexOf ('=') >= 0)
			{
			throw new NotSupportedException ("Unsupported timer configuration.");
			}
		string[] fields = ports[snapshot.Zone - 1].Split (',');
		if (fields.Length < 4 || ports[snapshot.Zone - 1].IndexOf ('/') < 0 || fields[0].Length < 28)
			{
			throw new NotSupportedException ("A complete modern calibration field is required; missing defaults are not invented.");
			}
		// Do not rewrite pressure compensation, calibration extensions, sensor flags or any other setting.
		fields[0] = fields[0].Substring (0, 24) + (percentage & 255).ToString ("x2", CultureInfo.InvariantCulture) + fields[0].Substring (26);
		ports[snapshot.Zone - 1] = string.Join (",", fields);
		return string.Join ("|", ports);
		}
	}
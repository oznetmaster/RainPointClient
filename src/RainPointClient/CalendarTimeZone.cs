using System;
using System.Collections.Generic;
using System.Globalization;

namespace RainPointClient;

/// <summary>Reported home offset and daylight transitions used by the vendor calendar. This is not a general operating-system timezone database.</summary>
public sealed class RainPointCalendarTimeZone
	{
	private RainPointCalendarTimeZone (int offset, int adjustment, DateTimeOffset[] transitions)
		{
		BaseOffset = TimeSpan.FromMinutes (offset);
		DaylightAdjustment = TimeSpan.FromMinutes (adjustment);
		Transitions = Array.AsReadOnly (transitions);
		}
	public TimeSpan BaseOffset
		{
		get;
		}
	public TimeSpan DaylightAdjustment
		{
		get;
		}
	/// <summary>Alternating daylight start/end instants supplied by the service. No future rules are invented beyond this table.</summary>
	public IReadOnlyList<DateTimeOffset> Transitions
		{
		get;
		}
	internal static RainPointCalendarTimeZone? Decode (int? offset, string? encoded)
		{
		if (offset is null or < -840 or > 840 || encoded is null || encoded.Length > 8192)
			return null;
		if (encoded.Length == 0)
			return new (offset.Value, 0, []);
		string[] fields = encoded.Split (',');
		if (fields.Length > 513 || (fields.Length - 1) % 2 != 0
		 || !int.TryParse (fields[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int adjustment) || adjustment is < -180 or > 180)
			return null;
		List<DateTimeOffset> transitions = [];
		DateTimeOffset current = new (2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
		for (int i = 1; i < fields.Length; i++)
			{
			if (!uint.TryParse (fields[i], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint units) || units == 0
			 || units > (DateTimeOffset.MaxValue - current).TotalMinutes / 10)
				return null;
			current = current.AddMinutes (units * 10d);
			transitions.Add (current);
			}
		return new (offset.Value, adjustment, transitions.ToArray ());
		}
	internal long UtcDayNumber (DateTime localDate)
		{
		// The app tests DST against the base-offset instant before applying the daylight adjustment.
		long ticks = localDate.Ticks - BaseOffset.Ticks;
		int index = 0;
		while (index < Transitions.Count && Transitions[index].UtcDateTime.Ticks <= ticks)
			index++;
		if (index < Transitions.Count && index % 2 == 1)
			ticks -= DaylightAdjustment.Ticks;
		return (long)Math.Floor ((ticks - new DateTime (1970, 1, 1).Ticks) / (double)TimeSpan.TicksPerDay);
		}
	}
// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.Linq;

namespace RainPointClient.Desktop.Core;

public sealed class PlanRow
	{
	internal PlanRow (RainPointSchedule plan) => Plan = plan;
	internal RainPointSchedule Plan
		{
		get;
		}
	public int Number => Plan.Index + 1;
	public string State => Plan.Enabled ? "Enabled" : "Disabled";
	public string Mode => Plan.Mode == RainPointScheduleMode.Irrigation ? "Normal irrigation" : Plan.Mode == RainPointScheduleMode.Misting ? "Misting" : "Cycle and soak";
	public string Start => Plan.StartTime.ToString (@"hh\:mm", CultureInfo.InvariantCulture);
	public string Duration => Plan.Duration.TotalSeconds.ToString ("0.########", CultureInfo.InvariantCulture) + " seconds";
	public string Repeat => Plan.Repeat switch
		{
			RainPointScheduleRepeat.Once => "Once",
			RainPointScheduleRepeat.EveryDay => "Every day",
			RainPointScheduleRepeat.OddDays => "Odd days",
			RainPointScheduleRepeat.EvenDays => "Even days",
			RainPointScheduleRepeat.Weekdays => string.Join (", ", Plan.Weekdays),
			RainPointScheduleRepeat.IntervalDays => $"Every {Plan.Interval} days",
			_ => $"Every {Plan.Interval} hours"
			};
	public string Details => $"Plan {Number} · {State} · {Mode} · {Start} · {Duration} · {Repeat}"
	 + "\nEffective date: " + (Plan.EffectiveDate?.ToString ("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Not set") + " · Water limit: " + (Plan.WaterLimitLitres.HasValue ? Plan.WaterLimitLitres.Value.ToString (CultureInfo.InvariantCulture) + " L" : "Not set")
	 + "\nWater per cycle: " + Seconds (Plan.CycleWateringTime) + " · Pause: " + Seconds (Plan.CyclePauseTime)
	 + "\nTimes use the home's local calendar. Saved configuration does not confirm scheduled execution.";
	private static string Seconds (TimeSpan? time) => time?.TotalSeconds.ToString ("0.########", CultureInfo.InvariantCulture) + (time.HasValue ? " seconds" : "Not set");
	internal static bool Same (RainPointSchedule a, RainPointSchedule b, bool? enabled = null) => a.Enabled == (enabled ?? b.Enabled) && a.Mode == b.Mode && a.StartTime == b.StartTime && a.Duration == b.Duration && a.Repeat == b.Repeat && a.Interval == b.Interval && a.EffectiveDate == b.EffectiveDate && a.WaterLimitLitres == b.WaterLimitLitres && a.CycleWateringTime == b.CycleWateringTime && a.CyclePauseTime == b.CyclePauseTime && a.Weekdays.OrderBy (d => d).SequenceEqual (b.Weekdays.OrderBy (d => d));
	}
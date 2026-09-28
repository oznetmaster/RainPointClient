using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;

using NUnit.Framework;

using RainPointClient.Protocol;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class CalendarTimeZoneTests
	{
	internal static string LondonRules ()
		{
		DateTimeOffset origin = new (2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
		DateTimeOffset start = new (2026, 3, 29, 1, 0, 0, TimeSpan.Zero);
		DateTimeOffset end = new (2026, 10, 25, 1, 0, 0, TimeSpan.Zero);
		return "60," + ((long)(start - origin).TotalMinutes / 10).ToString ("X", CultureInfo.InvariantCulture) + "," + ((long)(end - start).TotalMinutes / 10).ToString ("X", CultureInfo.InvariantCulture);
		}
	internal static RainPointScheduleSnapshot IntervalSnapshot (int zone, DateTime anchor) => new (2, zone, TimerReadingAvailability.Decoded,
		Array.AsReadOnly (new[] { new RainPointSchedule (0, true, RainPointScheduleMode.Irrigation, new TimeSpan (23, 39, 0), TimeSpan.FromMinutes (1), RainPointScheduleRepeat.IntervalDays, Array.Empty<DayOfWeek> (), 2, null, anchor, null, null) }));
	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public void AppObservedOctoberCalendarUsesUtcDaysAcrossClockChange (int zone)
		{
		var rules = RainPointCalendarTimeZone.Decode (0, LondonRules ())!;
		var snapshot = IntervalSnapshot (zone, new DateTime (2026, 9, 27));
		var app = ScheduleCalendar.Create (snapshot, new DateTime (2026, 10, 23), new DateTime (2026, 10, 31), rules);
		// Observed in the official app: consecutive 25/26 entries, then 28/30 after the UK clock change.
		Assert.That (app.Occurrences.Select (o => o.StartsAt.Day), Is.EqualTo (new[] { 23, 25, 26, 28, 30 }));
		Assert.That (app.Occurrences.All (o => o.Zone == zone), Is.True);
		var civil = ScheduleCalendar.Create (snapshot, new DateTime (2026, 10, 23), new DateTime (2026, 10, 31));
		Assert.That (civil.Occurrences.Select (o => o.StartsAt.Day), Is.EqualTo (new[] { 23, 25, 27, 29, 31 }));
		}
	[Test]
	public void SpringTransitionPreservesVendorSkippedUtcDayBehavior ()
		{
		var preview = ScheduleCalendar.Create (IntervalSnapshot (1, new DateTime (2026, 3, 28)), new DateTime (2026, 3, 28), new DateTime (2026, 4, 3), RainPointCalendarTimeZone.Decode (0, LondonRules ())!);
		Assert.That (preview.Occurrences.Select (o => o.StartsAt.Date), Is.EqualTo (new[] { new DateTime (2026, 3, 28), new DateTime (2026, 3, 31), new DateTime (2026, 4, 2) }));
		}
	[Test]
	public void ExplicitHomeAttributesProduceImmutableTypedRules ()
		{
		string json = JsonSerializer.Serialize (new
			{
			hid = 5,
			zoneName = "Europe/London",
			zoneOffset = 0,
			zoneDst = LondonRules ()
			});
		var home = new RainPointHomeDetails (JsonSerializer.Deserialize<HomeDetailsResponse> (json)!, new object ());
		Assert.That (home.CalendarTimeZone!.BaseOffset, Is.EqualTo (TimeSpan.Zero));
		Assert.That (home.CalendarTimeZone.DaylightAdjustment, Is.EqualTo (TimeSpan.FromHours (1)));
		Assert.That (home.CalendarTimeZone.Transitions.Count, Is.EqualTo (2));
		Assert.Throws<NotSupportedException> (() => ((System.Collections.Generic.IList<DateTimeOffset>)home.CalendarTimeZone.Transitions)[0] = DateTimeOffset.UtcNow);
		}
	[TestCase (null)]
	[TestCase ("bad")]
	[TestCase ("60,1")]
	[TestCase ("60,0,1")]
	[TestCase ("60,xyz,1")]
	[TestCase ("999,1,1")]
	[TestCase ("60,FFFFFFFF,FFFFFFFF")]
	public void MissingOrMalformedRuleDoesNotBecomeUtc (string? rule) => Assert.That (RainPointCalendarTimeZone.Decode (0, rule), Is.Null);
	[TestCase (-841)]
	[TestCase (841)]
	public void InvalidBaseOffsetIsUnavailable (int offset) => Assert.That (RainPointCalendarTimeZone.Decode (offset, ""), Is.Null);
	[TestCase (-480)]
	[TestCase (330)]
	[TestCase (840)]
	public void ExplicitFixedOffsetRetainsCalendarIntervals (int offset)
		{
		var preview = ScheduleCalendar.Create (IntervalSnapshot (1, new DateTime (2026, 9, 27)), new DateTime (2026, 9, 27), new DateTime (2026, 10, 2), RainPointCalendarTimeZone.Decode (offset, "")!);
		Assert.That (preview.Occurrences.Select (o => o.StartsAt.Day), Is.EqualTo (new[] { 27, 29, 1 }));
		}
	}
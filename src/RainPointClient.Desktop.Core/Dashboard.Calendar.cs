using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace RainPointClient.Desktop.Core;

public sealed partial class Dashboard
	{
	private RainPointScheduleSnapshot? _calendarSnapshot;
	private RainPointCalendarTimeZone? _calendarTimeZone;
	private int _calendarZone = 1;
	private DateTime? _calendarDate = DateTime.SpecifyKind (DateTime.Today, DateTimeKind.Unspecified);
	public int CalendarZone
		{
		get => _calendarZone;
		set
			{
			if (!CanEdit || value is < 1 or > 3 || value == _calendarZone)
				return;
			_calendarZone = value;
			ClearCalendar ();
			Changed ();
			}
		}
	public DateTime? CalendarDate
		{
		get => _calendarDate;
		set
			{
			if (!CanEdit || value is null)
				return;
			_calendarDate = DateTime.SpecifyKind (value.Value.Date, DateTimeKind.Unspecified);
			ShowCalendar ();
			Changed ();
			}
		}
	public ObservableCollection<CalendarRow> CalendarRows { get; } = new ();
	public string CalendarMessage { get; private set; } = "Load a fresh snapshot to preview the selected zone.";
	public string CalendarReadAt { get; private set; } = "No calendar snapshot loaded.";
	public string CalendarNext { get; private set; } = "Next start is unknown.";
	public bool CanLoadCalendar => CanRefresh && _timer is not null;
	public Task LoadCalendarAsync ()
		{
		if (!CanLoadCalendar)
			return Task.CompletedTask;
		return RunAsync (async token =>
		 {
			 ClearCalendar ();
			 try
				 {
				 _calendarSnapshot = await _client.GetTimerSchedulesAsync (_hub!, _timer!.Address, _calendarZone, token);
				 if (_calendarSnapshot.Schedules.Any (plan => plan.Enabled && plan.Repeat == RainPointScheduleRepeat.IntervalDays))
					 _calendarTimeZone = (await _client.GetHomeAsync (_hub!.HomeId, token)).CalendarTimeZone;
				 token.ThrowIfCancellationRequested ();
				 CalendarReadAt = $"Zone {_calendarZone} · snapshot read {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}";
				 ShowCalendar ();
				 }
			 catch { ClearCalendar (); CalendarMessage = "Calendar settings could not be read. Reload explicitly."; throw; }
		 }, "Calendar read failed.");
		}
	private void ClearCalendar ()
		{
		_calendarSnapshot = null;
		_calendarTimeZone = null;
		CalendarRows.Clear ();
		CalendarReadAt = "No calendar snapshot loaded for this selection.";
		CalendarMessage = "Load a fresh snapshot to preview the selected zone.";
		CalendarNext = "Next start is unknown.";
		}
	private void ShowCalendar ()
		{
		CalendarRows.Clear ();
		if (_calendarSnapshot is null || _calendarDate is null)
			return;
		DateTime day = _calendarDate.Value;
		DateTime through = day.Year == 9999 && day.Month == 12 ? new DateTime (9999, 12, 31) : day.AddDays (Math.Min (365, (DateTime.MaxValue.Date - day).Days));
		if (_calendarTimeZone is null && _calendarSnapshot.Schedules.Any (plan => plan.Enabled && plan.Repeat == RainPointScheduleRepeat.IntervalDays))
			{
			CalendarMessage = "The home's timezone rules are unavailable; interval-plan dates cannot be matched to the app.";
			CalendarNext = "Next start is unknown.";
			return;
			}
		var preview = _calendarTimeZone is null ? ScheduleCalendar.Create (_calendarSnapshot, day, through) : ScheduleCalendar.Create (_calendarSnapshot, day, through, _calendarTimeZone);
		if (preview.Availability != TimerReadingAvailability.Decoded)
			{
			CalendarMessage = $"Calendar is {preview.Availability}. An empty list does not mean no plans are saved.";
			CalendarNext = "Next start is unknown.";
			return;
			}
		foreach (var occurrence in preview.Occurrences.Where (item => item.StartsAt.Date == day))
			CalendarRows.Add (new (occurrence));
		var next = preview.GetNextOccurrence (day);
		CalendarNext = next is null ? $"No non-delayed projected start through {through:yyyy-MM-dd}." : $"Next from selected day: {next.StartsAt:yyyy-MM-dd HH:mm} · plan {next.Plan.Index + 1}" + (next.RainDelay == RainPointCalendarRainDelay.Unknown ? " · rain delay unknown" : "");
		CalendarMessage = $"{CalendarRows.Count} enabled-plan start(s) on {day:yyyy-MM-dd}. Calendar durations follow the app's minute-rounded display, not elapsed watering time. This snapshot does not confirm RF delivery, execution, conflicts or sensor/weather suppression.";
		}
	}

public sealed class CalendarRow (RainPointScheduleOccurrence occurrence)
	{
	public int Plan => occurrence.Plan.Index + 1;
	public int Zone => occurrence.Zone;
	public string Start => occurrence.StartsAt.ToString ("HH:mm", CultureInfo.InvariantCulture);
	public string Mode => occurrence.Plan.Mode.ToString ();
	public string Seasonal => occurrence.SeasonalPercentage is { } p ? p.ToString (CultureInfo.InvariantCulture) + "%" : "Unknown";
	public string Duration => occurrence.CalendarDuration is { } d ? d.TotalMinutes.ToString ("0", CultureInfo.InvariantCulture) + " min" : "Unknown";
	public string RainDelay => occurrence.RainDelay switch { RainPointCalendarRainDelay.Delayed => "Rain delayed", RainPointCalendarRainDelay.NotDelayed => "Not rain delayed", _ => "Unknown" };
	}
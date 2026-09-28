using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace RainPointClient.Desktop.Core;

public sealed partial class Dashboard
	{
	private int _historyZone = 1;
	private string _usagePeriod = "Daily";
	private string _usageStart = DateTime.Today.AddDays (-29).ToString ("yyyy-MM-dd", CultureInfo.InvariantCulture);
	private string _usageEnd = DateTime.Today.ToString ("yyyy-MM-dd", CultureInfo.InvariantCulture);
	private string _eventFilter = "All event types";
	private string _eventBegin = string.Empty;
	private string _eventEnd = string.Empty;
	private DateTimeOffset? _eventCursor;
	private bool _hasOlderEvents;
	private EventHistoryRow? _selectedHistoryEvent;
	private readonly HashSet<string> _eventIds = new (StringComparer.Ordinal);
	public IReadOnlyList<int> HistoryZones { get; } = Array.AsReadOnly (new[] { 1, 2, 3 });
	public IReadOnlyList<string> UsagePeriods { get; } = Array.AsReadOnly (new[] { "Daily", "Monthly" });
	public IReadOnlyList<string> EventFilters { get; } = Array.AsReadOnly (new[] { "All event types", "Watering", "Water usage", "Water control", "Hub status", "Power on" });
	public ObservableCollection<UsageHistoryRow> UsageHistory { get; } = new ();
	public ObservableCollection<EventHistoryRow> EventHistory { get; } = new ();
	public int HistoryZone
		{
		get => _historyZone;
		set
			{
			if (!CanEdit || value is < 1 or > 3 || value == _historyZone)
				return;
			_historyZone = value;
			ClearHistory ();
			Changed ();
			}
		}
	public string UsagePeriod
		{
		get => _usagePeriod;
		set
			{
			if (!CanEdit || !UsagePeriods.Contains (value) || value == _usagePeriod)
				return;
			_usagePeriod = value;
			ClearUsageHistory ();
			Changed ();
			}
		}
	public string UsageStart
		{
		get => _usageStart;
		set
			{
			if (!CanEdit || value == _usageStart)
				return;
			_usageStart = value ?? string.Empty;
			ClearUsageHistory ();
			Changed ();
			}
		}
	public string UsageEnd
		{
		get => _usageEnd;
		set
			{
			if (!CanEdit || value == _usageEnd)
				return;
			_usageEnd = value ?? string.Empty;
			ClearUsageHistory ();
			Changed ();
			}
		}
	public string EventFilter
		{
		get => _eventFilter;
		set
			{
			if (!CanEdit || !EventFilters.Contains (value) || value == _eventFilter)
				return;
			_eventFilter = value;
			ClearEventHistory ();
			Changed ();
			}
		}
	public string EventBegin
		{
		get => _eventBegin;
		set
			{
			if (!CanEdit || value == _eventBegin)
				return;
			_eventBegin = value ?? string.Empty;
			ClearEventHistory ();
			Changed ();
			}
		}
	public string EventEnd
		{
		get => _eventEnd;
		set
			{
			if (!CanEdit || value == _eventEnd)
				return;
			_eventEnd = value ?? string.Empty;
			ClearEventHistory ();
			Changed ();
			}
		}
	public EventHistoryRow? SelectedHistoryEvent
		{
		get => _selectedHistoryEvent;
		set
			{
			if (value is not null && !EventHistory.Contains (value))
				return;
			_selectedHistoryEvent = value;
			Changed ();
			}
		}
	public string EventDetails => _selectedHistoryEvent?.Details ?? "Select an event to see its details.";
	public bool CanReadHistory => CanRefresh && _timer is not null;
	public bool CanLoadUsage => CanReadHistory && UsageDates (out _, out _);
	public bool CanLoadEvents => CanReadHistory && EventBounds (out _, out _);
	public bool CanLoadOlderEvents => CanLoadEvents && _hasOlderEvents && _eventCursor.HasValue;
	public string UsageRangeHelp => UsageDates (out _, out _)
	 ? "Inclusive home-calendar dates. Missing days/months are not zero. Monthly buckets can cover a partial month."
	 : "Use yyyy-MM-dd dates from 1970 onward, start before or equal to end; at most 30 days for daily usage or one year for monthly usage.";
	public string EventRangeHelp => EventBounds (out _, out _)
	 ? "Optional UTC bounds apply to cloud time. Leave blank for no bound. Events are limited to the selected timer and zone."
	 : "Use yyyy-MM-dd HH:mm:ss in UTC, from 1970 onward; the start must be before the end. Either field can be blank.";
	public string UsageMessage { get; private set; } = "Load usage for the selected zone and dates.";
	public string UsageSummary { get; private set; } = "No usage loaded.";
	public string UsageReadAt { get; private set; } = "No successful history read.";
	public string EventsMessage { get; private set; } = "Load events for the selected zone.";
	public string EventsReadAt { get; private set; } = "No successful event read.";

	public Task LoadUsageAsync ()
		{
		if (!CanLoadUsage || !UsageDates (out DateTime start, out DateTime end))
			return Task.CompletedTask;
		return RunAsync (async token =>
		{
			ClearUsageHistory ();
			UsageMessage = "Loading usage…";
			Changed ();
			try
				{
				var rows = await _client.GetTimerWaterUsageAsync (_hub!, _timer!.Address, _historyZone, _usagePeriod == "Daily" ? RainPointUsagePeriod.Day : RainPointUsagePeriod.Month, start, end, token);
				token.ThrowIfCancellationRequested ();
				foreach (var row in rows)
					UsageHistory.Add (new UsageHistoryRow (row, _usagePeriod == "Monthly"));
				int known = rows.Count (row => row.Litres.HasValue);
				UsageSummary = rows.Count == 0 ? "No usage buckets returned; this does not establish zero consumption."
			: known == 0 ? $"{rows.Count} reported buckets · all amounts unknown."
			: $"{rows.Where (row => row.Litres.HasValue).Sum (row => row.Litres!.Value).ToString ("0.0", CultureInfo.CurrentCulture)} L across {known} known buckets · {rows.Count - known} unknown. This is not a guaranteed complete total.";
				UsageReadAt = $"Zone {_historyZone} · {_usagePeriod.ToLowerInvariant ()} usage · {start:yyyy-MM-dd} through {end:yyyy-MM-dd} · read {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}";
				UsageMessage = "Usage received. Only returned buckets are shown.";
				Message = "Usage history loaded. See the History tab.";
				}
			catch { ClearUsageHistory (); UsageMessage = "Usage could not be read. Check the dates and session, then reload explicitly."; throw; }
		}, "History read failed. Check the history tab.");
		}

	public Task LoadEventsAsync () => ReadEventsAsync (false);
	public Task LoadOlderEventsAsync () => ReadEventsAsync (true);
	private Task ReadEventsAsync (bool older)
		{
		if (!(older ? CanLoadOlderEvents : CanLoadEvents) || !EventBounds (out DateTimeOffset? begin, out DateTimeOffset? end))
			return Task.CompletedTask;
		if (older)
			end = _eventCursor;
		int code = Array.IndexOf (EventFilters.ToArray (), _eventFilter);
		RainPointEventQuery query = new ()
			{
			HubId = _hub!.Id,
			Address = _timer!.Address,
			Zone = _historyZone,
			Code = code == 0 ? null : code,
			Begin = begin,
			End = end,
			Limit = 50
			};
		return RunAsync (async token =>
		{
			if (!older)
				ClearEventHistory ();
			EventsMessage = older ? "Loading older events…" : "Loading events…";
			Changed ();
			try
				{
				RainPointEventPage page = await _client.GetEventsAsync (_hub!.HomeId, query, token);
				token.ThrowIfCancellationRequested ();
				int added = 0;
				foreach (var item in page.Events.OrderByDescending (item => item.CloudTimestamp))
					if (EventHistory.Count < 500 && _eventIds.Add (item.Id))
						{
						EventHistory.Add (new EventHistoryRow (item));
						added++;
						}
				EventHistoryRow? selected = _selectedHistoryEvent;
				var sorted = EventHistory.OrderByDescending (row => row.Event.CloudTimestamp).ToArray ();
				EventHistory.Clear ();
				foreach (var row in sorted)
					EventHistory.Add (row);
				_selectedHistoryEvent = selected is not null && EventHistory.Contains (selected) ? selected : EventHistory.FirstOrDefault ();
				Message = "Event history loaded. See the History tab.";
				_eventCursor = page.OldestTimestamp;
				bool advances = _eventCursor.HasValue && _eventCursor.Value.ToUnixTimeMilliseconds () > 0
			&& (!query.End.HasValue || _eventCursor.Value < query.End.Value) && (!begin.HasValue || _eventCursor.Value > begin.Value);
				_hasOlderEvents = page.IsLimitReached && advances && added > 0 && EventHistory.Count < 500;
				EventsReadAt = $"Zone {_historyZone} · {EventHistory.Count} distinct events shown · last read {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}";
				EventsMessage = EventHistory.Count >= 500 ? "500-event display limit reached. Narrow the time range to read another window."
			: older && (!advances || added == 0) && page.Events.Count > 0 ? "Paging made no progress toward older events. Loading stopped; timestamp ties may hide additional records."
			: page.Events.Count == 0 ? "No events returned for this page. This is not a retention or completeness guarantee."
			: _hasOlderEvents ? "Page received. Choose Load older to request another page. Timestamp ties can prevent a complete history."
			: "Page received. No further page is offered for these bounds; this does not guarantee a complete history.";
				}
			catch { EventsMessage = older ? "Older events could not be read. Existing rows remain; retry explicitly or change the filters." : "Events could not be read. Check the filters and session, then reload explicitly."; throw; }
		}, "Event read failed. Check the history tab.");
		}

	private bool UsageDates (out DateTime start, out DateTime end)
		{
		end = default;
		return DateTime.TryParseExact (_usageStart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start)
		 && DateTime.TryParseExact (_usageEnd, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out end)
		 && start.Year >= 1970 && start <= end && (_usagePeriod == "Daily" ? (end - start).TotalDays <= 29 : start >= end.AddYears (-1));
		}
	private bool EventBounds (out DateTimeOffset? begin, out DateTimeOffset? end)
		{
		end = null;
		return UtcBound (_eventBegin, out begin) && UtcBound (_eventEnd, out end) && (!begin.HasValue || !end.HasValue || begin.Value < end.Value);
		}
	private static bool UtcBound (string text, out DateTimeOffset? value)
		{
		value = null;
		if (string.IsNullOrWhiteSpace (text))
			return true;
		if (!DateTimeOffset.TryParseExact (text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset date) || date.ToUnixTimeMilliseconds () < 0)
			return false;
		value = date;
		return true;
		}
	private void ClearHistory ()
		{
		ClearUsageHistory ();
		ClearEventHistory ();
		}
	private void ClearUsageHistory ()
		{
		UsageHistory.Clear ();
		UsageSummary = "No usage loaded.";
		UsageReadAt = "No successful history read for these filters.";
		UsageMessage = "Load usage for the selected zone and dates.";
		}
	private void ClearEventHistory ()
		{
		EventHistory.Clear ();
		_eventIds.Clear ();
		_eventCursor = null;
		_hasOlderEvents = false;
		_selectedHistoryEvent = null;
		EventsReadAt = "No successful event read for these filters.";
		EventsMessage = "Load events for the selected zone.";
		}
	}
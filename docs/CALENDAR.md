# Saved-plan calendar projections

`ScheduleCalendar.Create` calculates a bounded calendar from an existing `RainPointScheduleSnapshot`. It performs no network I/O or device commands. `RainPointCalendarPreview` contains immutable, chronologically sorted occurrences, and `GetNextOccurrence` finds the first projected start at or after a supplied local wall time within that preview.

```csharp
var snapshot = await client.GetTimerSchedulesAsync(hub, timer.Address, zone, cancellationToken);
var first = new DateTime(2026, 9, 24); // Home-local calendar date, Unspecified kind.
var home = await client.GetHomeAsync(hub.HomeId, cancellationToken);
// For interval plans, require reported rules instead of silently assuming civil-day spacing.
if (home.CalendarTimeZone is null)
    throw new InvalidOperationException("Home calendar timezone rules are unavailable.");
var preview = ScheduleCalendar.Create(snapshot, first, first.AddDays(30), home.CalendarTimeZone);
if (preview.Availability == TimerReadingAvailability.Decoded)
{
    foreach (var occurrence in preview.Occurrences)
    {
        // StartsAt, Plan, Zone, RainDelay, SeasonalPercentage, CalendarDuration.
    }
    var next = preview.GetNextOccurrence(first.AddHours(12));
    // null means no eligible start in this window, not that no future plan exists.
}
```

Inclusive bounds must be midnight dates spanning 1–366 days. More than the supported six plans per zone yields UnsupportedFormat, bounding output to at most 2,196 occurrences. Input and output use `DateTimeKind.Unspecified`; callers supply the home's local date/time. UTC and machine-local kinds are rejected rather than converted with an assumed time zone. A preview never predicts a UTC instant or the handling of ambiguous/nonexistent DST wall times.

## Supported rules

Only enabled plans appear. Once-only plans match their effective date exactly. Daily, odd/even day-of-month, selected weekdays and anchored interval-day repeats are supported. All repeat types respect an effective date when present. The overload accepting `RainPointCalendarTimeZone` reproduces the vendor interval calculation: it converts each local midnight using the reported base offset and daylight-transition table, then compares UTC day buckets. This can shift interval dates at daylight-saving boundaries. The original three-argument overload explicitly retains civil-day spacing and should not be used to claim vendor interval-calendar parity. Other recurrence types use local calendar dates in both overloads. An expired once-only plan yields no future occurrence and cannot start an unbounded search.

Hourly recurrence remains unsupported for this RF calendar. If an enabled plan uses an unsupported recurrence, the whole preview reports `UnsupportedFormat`; it does not return a deceptively complete partial next-plan result. Missing required once/interval anchors report `NotReported`. An unreadable saved-plan snapshot retains its original availability. Empty results mean no projected starts only when availability is `Decoded`.

A known rain-delay expiry marks starts at or before that time as `Delayed`, matching the app's strict greater-than comparison for an active calendar entry. Those starts remain visible but are excluded from next-start lookup. Missing rain-delay data is `Unknown`, never assumed clear. A returned next occurrence can therefore have an unknown delay status; consumers must inspect it.

`CalendarDuration` reproduces the app calendar's display: configured duration multiplied by the relevant month's percentage, rounded to whole minutes with half-minutes rounded up and a one-minute minimum. Missing seasonal settings produce null duration/percentage rather than a fabricated 100%. This display duration is not elapsed runtime, a cycle/soak completion time or an estimate of when a water-volume limit will be reached. The original typed plan remains attached to the occurrence.

## Protocol evidence and limits

The inspected RainPoint Home 1.19.1065 RF bundle uses its common-plan recurrence model (module 541) and calendar screen (module 1225); the latter calls the common model and applies monthly scaling and the rain-delay comparison locally. Source/package provenance is recorded in [schedules](SCHEDULES.md#protocol-evidence). Vendor source and private captures are not distributed. Other bundled device-family models differ, especially for hourly and once-only behavior; those implementations are not silently applied to HTV345FRF.

The calendar is a projection of saved configuration, not confirmation of RF delivery, valve movement or future execution. Device disconnection, stale settings, conflicting plans/zones, sensors and weather-based suppression can affect operation. Scheduled execution and populated official-app comparison are separate evidence items, recorded below. A consumer should display the snapshot's read time and reload after changes.

## Windows app

The **Calendar** tab provides all three zones, a date picker, selected-day occurrences and the next non-delayed start from the selected day's midnight over at most 366 days. It is not labeled as a live next-run countdown. The initial date comes from the PC and can be changed to the home's date. Load/refresh reads a fresh snapshot and, when enabled interval plans are present, the home calendar rules. Missing/malformed rules make the interval preview unknown rather than returning guessed dates. Subsequent date navigation is local and sends no commands. Zone/timer/account changes, session recovery and attempted plan/settings writes clear the cached preview. Unknown availability is shown explicitly.

## Validation

Thirty calendar cases and seven dashboard lifecycle cases cover all zones, six recurrences, date bounds, leap days, month/year and DST-boundary civil dates, exact rain-delay expiry, unknown settings, display rounding, immutable ordering, unsupported/missing anchors, stale-preview clearing and cancellation. Three actual WPF cases cover the selected zone, date/month navigation, read-only behavior and rendering on both Windows targets. Results are retained under `artifacts/calendar-offline` and `artifacts/calendar-focused`.

The read-only `CompletionLiveTests` fixture also projects 31 dates from each zone's live saved snapshot, without changing or enabling plans. Live empty-plan results validate transport/snapshot integration, not populated-calendar parity or scheduled execution. See [test instructions](../tests/README.md).

The focused `CalendarLiveTests.ProjectsAllZonesWithoutChangingPlans` check passed on net10.0 and net472 on 24 September 2026. Each zone had zero saved plans and zero projected starts across 31 dates. This confirms all-zone snapshot/projection integration with empty live configuration; populated recurrence behavior is covered offline. Earlier broad completion attempts timed out at the unrelated product-catalog request; those failures are retained, not reclassified as passes.

The common Once recurrence remains readable and projectable for historical configurations, but the HTV345FRF write API and Windows editor no longer create or enable it after the [execution comparison](SCHEDULES.md#daily-execution-passed-and-once-writes-restricted--25-september-2026). A projection does not establish firmware support. The official app's populated daily plan details and next-start indicator matched one 07:01 occurrence; the later populated interval-calendar comparison below extends that evidence.


## Populated official-app comparison — 27 September 2026

The one-off RainPoint Home 1.19.1065 inspection matched an enabled two-day interval plan: normal mode, one-minute duration, a delayed 27 September occurrence, 29 September next start and no plan on 28 September. The app also reflected the fixture's later move from 23:32 to 23:39 and showed empty plans after restoration.

The October month grid revealed a real daylight-saving mismatch in the original civil-day projection: the app showed 23, 25, **26, 28 and 30 October**, whereas civil-day spacing gave 23, 25, 27, 29 and 31. Selecting 26 October showed the one-minute 23:39 plan. The corrected overload follows the reported home transition table used by the app's common plan model. Typed, attributed home decoding now exposes immutable offset/transition observations without a public raw payload. Offline cases cover all three zones, both UK clock changes, missing/malformed rules, fixed offsets and Windows unknown-state handling. This comparison validates the observed interval calendar, not every recurrence across every world timezone.

The read-only actual-home check passed on net472 using the paired home's reported base offset and 24 daylight transitions. Its 23/25/26/28/30 October dates match the observed app. Result: `artifacts/calendar-app-comparison/reported-paired-home-calendar_net472_20260927235646.trx`. An earlier fixture incorrectly required the account to have exactly one home; it failed before any write, was corrected to select the unique paired kit, and remains retained as a failed result.
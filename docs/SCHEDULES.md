# Saved timer schedules

`GetTimerSchedulesAsync(hub, address, zone, cancellationToken)` reads fresh cloud configuration for one HTV345FRF zone, numbered 1 through 3. Supply the hub and timer from discovery. The client checks that the same cloud sub-device is still paired at that RF address. The operation does not save settings or actuate a valve.

```csharp
RainPointScheduleSnapshot snapshot = await client.GetTimerSchedulesAsync(
    hub, timer.Address, 1, cancellationToken);

if (snapshot.Availability == TimerReadingAvailability.Decoded)
{
    foreach (RainPointSchedule plan in snapshot.Schedules)
    {
        // Display plan.Enabled, Mode, StartTime, Duration, Repeat, etc.
    }
}
```

Only `Decoded` with an empty list means there are no saved plans. `NotReported`, `UnsupportedFormat` and `Malformed` mean the configuration is unavailable or unreadable. A malformed record invalidates the zone's entire result; the client never presents a partial list as complete.

The immutable models expose enabled state, normal/misting/cycle-and-soak mode, local start time, duration, recurrence, selected weekdays, day/hour interval, effective date, water limit in litres, and optional cycle watering/pause durations. Disabled plans are retained. Index is the position in this configuration, not a durable identifier. Collections are read-only.

Times use the home's local calendar; effective dates have `DateTimeKind.Unspecified`. The [calendar API](CALENDAR.md) projects bounded next starts from saved local-date recurrence; it does not confirm execution or infer missing anchors. Monthly percentage settings are available through [seasonal-adjustment APIs](PLAN-SETTINGS.md). Normal and misting wire durations are seconds; cycle-and-soak durations are minutes converted to `TimeSpan`. Positive water limits use tenths of a litre. The total misting duration's treatment of pauses still needs behavioral verification.

## Protocol evidence

The installed RainPoint Home 1.19.1065 package contains a common RF timer plan implementation in `assets/HTV103FRF_Android.jsBundle`. Its device-parameter model, common-plan model and date/time helpers establish the layout below. This is protocol evidence from related timer code bundled with the app, not proof of every populated HTV345FRF plan variant. Vendor code and private captures are not distributed with this project.

The inspected base APK has SHA-256 `A0A97ED04FE2950408E88A773F345F38C32FDF1E536794579200E8EFDC411FEF`.

- Saved RF plans are in each child device's attributed `param` field, returned by existing home device discovery. `portNumber` must identify the three-zone layout.
- Pipe separators divide zones. The older container places comma-separated records after the first settings field. The newer container places slash-separated records in the second comma-separated field; a trailing slash identifies a single record, and `/` identifies an empty list.
- Records contain enabled/weekday/interval bits, packed local start time/repeat/mode, duration, and optional water-limit/date/cycle fields. Multi-byte numbers are little-endian. The supported record lengths are 5, 7, 9 and 13 bytes; other lengths are not guessed.
- Weekday bit zero is Sunday. Dates contain day, month and year offset from 2020; zero represents an absent date. Invalid calendar dates and invalid times are rejected.
- The similarly named `getPlanValueByDevice` endpoint supplies recommendation values. It is not used as a saved-plan list endpoint. LoRa task endpoints and DP key/value configurations are outside this RF decoder's scope.

The encoded parameter remains internal. Public APIs expose no JSON, hex or parameter strings. The JSON transport fields use explicit System.Text.Json attributes.

## Editing plans

The client now provides typed `AddTimerScheduleAsync`, `UpdateTimerScheduleAsync`, `DeleteTimerScheduleAsync` and `SetTimerScheduleEnabledAsync` operations. Each takes a decoded snapshot returned by `GetTimerSchedulesAsync`; update/delete/enable also take the plan's index from that snapshot. Creation and replacement accept `RainPointIrrigationSchedule`, `RainPointCycleAndSoakSchedule` or `RainPointMistingSchedule`, all **disabled by default**. Enabling is an explicit caller decision.

```csharp
RainPointScheduleSnapshot before = await client.GetTimerSchedulesAsync(
    hub, timer.Address, 1, cancellationToken);
await client.AddTimerScheduleAsync(hub, before, new RainPointIrrigationSchedule
{
    Enabled = false,
    StartTime = new TimeSpan(8, 0, 0),
    Duration = TimeSpan.FromMinutes(1),
    Repeat = RainPointScheduleRepeat.EveryDay
}, cancellationToken);
```

Normal plan writes support daily, odd/even days, selected weekdays and interval days. Once records remain readable but cannot be created, used as replacement timing, or enabled. Existing Once records can be disabled or deleted. Start times are whole local minutes; duration is 60..43200 whole seconds. Interval-day repeats require an effective local date. Interval days are checked against their encoded range and volume limits against the app's litre-entry range; application-specific recommendation and overlap rules are not reproduced. Hourly interval writes remain unsupported. Existing decoded plans can be deleted or disabled without rewriting their timing fields; enabling Once is rejected before any network write.

### Cycle-and-soak

Use the same add/update operations with `RainPointCycleAndSoakSchedule`:

```csharp
await client.AddTimerScheduleAsync(hub, before, new RainPointCycleAndSoakSchedule
{
    StartTime = new TimeSpan(8, 0, 0),
    Duration = TimeSpan.FromMinutes(10),
    CycleWateringTime = TimeSpan.FromMinutes(5),
    CyclePauseTime = TimeSpan.FromMinutes(30),
    Repeat = RainPointScheduleRepeat.EveryDay
}, cancellationToken);
```

`Duration` is total watering time, excluding pauses. The vendor plan screen requires 5..1440 whole minutes. Its custom cycle picker allows 1..720 whole minutes for each watering period and pause; the watering period must not exceed total watering. The client rejects invalid values rather than silently clamping them. The same local-time, date and recurrence rules apply as for normal plans. An optional `WaterLimitLitres` uses the same validation as normal and misting plans.

At 100% seasonal adjustment, elapsed time is `watering + (ceiling(watering / cycle) - 1) * pause`: there is no pause after the last cycle. Thus 10/5/30 takes 40 minutes. The client rejects elapsed times longer than the repeat spacing: one day for daily/odd days, two for even days, the specified interval for interval days, or the shortest selected-weekday gap including the week boundary. These checks come from the bundled app's save validation, custom picker and elapsed-duration calculation.

Seasonal percentages can change actual duration and the number of cycles. Seasonal settings now have [typed reads/writes](PLAN-SETTINGS.md); conflicts with other plans/zones and recommendation rules are not yet implemented; the repeat-spacing check uses the unadjusted 100% duration. Saving a plan is not a guarantee of conflict-free execution. Existing seasonal settings are preserved.

### Misting

Use `RainPointMistingSchedule` with the same add/update operations:

```csharp
await client.AddTimerScheduleAsync(hub, before, new RainPointMistingSchedule
{
    StartTime = new TimeSpan(8, 0, 0),
    Duration = TimeSpan.FromMinutes(10),
    CycleWateringTime = TimeSpan.FromSeconds(10),
    CyclePauseTime = TimeSpan.FromSeconds(20),
    Repeat = RainPointScheduleRepeat.EveryDay
}, cancellationToken);
```

The bundled app's plan-duration picker accepts 1..720 whole minutes. Each burst and pause accepts 5..3600 whole seconds. Firmware 120 or newer advertises per-plan mist timing support; older shared mist-setting behavior is not written by this API. The encoder uses seconds for all three timing fields and stores an optional water limit in tenths of a litre. Dates and recurrence use the same validation as the other modes. It preserves the existing shared/manual settings and other plans.

The vendor save path does not clamp a burst to the configured duration, and this client does not invent that restriction. The app's conflict calculator treats misting duration without adding pauses, while its explanatory text is ambiguous. Accordingly `Duration` describes the configured value; the API does not claim it is accumulated valve-open time or calculate an estimated physical elapsed time. Execution and pause accounting need separate hardware validation. Seasonal settings can be edited separately; seasonal effects and conflicts with other plans remain outside schedule creation/replacement validation, as above.

Writes require the three-zone HTV345FRF layout and a reported numeric firmware version of at least 120. There is a six-plan limit per zone. The client preserves the other zones, other settings fields, and untouched plan records and their order. It does not convert the container format or sort the remaining plans.

Before writing, discovery is repeated and the full timer parameter, device identity, firmware and port count are compared with the supplied snapshot. A difference aborts the edit. A snapshot can be used for at most one write attempt; after success or an uncertain result, read again and reconcile before deciding whether another operation is needed. No write is automatically replayed. The service exposes no established atomic compare-and-swap contract: this preflight check cannot eliminate a change by another writer between the read and the update. Avoid concurrent editors.

The app's `updateDeviceParam` passes `{param}` to its `updateSubDeviceInfo`, which sends attributed `mid`, `sid` and `param` fields to `/app/device/sub/update`. A successful client call means cloud acceptance, not RF delivery or a completed scheduled watering run. The public API exposes none of those encoded parameter strings.

## Optional volume limits

All three write models expose nullable `WaterLimitLitres`. Leave it null for duration-only watering, or supply **0.3..6000 litres in 0.1 L steps**, matching the vendor app's litre input. This narrows the earlier normal-plan write range of 0.1..6553.5 L; decoding still preserves the full wire range so existing values are not lost. Zero is not a write alias for null. The Windows editor supports the same limits and can load, replace or clear volume limits in any mode, in all three zones.

The vendor help describes stopping when either usage or duration reaches its limit. A volume limit does not remove the duration cap, burst/pause validation or cycle recurrence-spacing guard. It is not a promise of measured volume accuracy. Seasonal-setting validation retains the configured cap for volume-limited plans, matching the plan-details screen. The separate [calendar display](CALENDAR.md) follows its own monthly display calculation and is not a runtime or volume estimate.

The common RF plan model (bundle module 541) writes the same little-endian word at bytes 5–6 for every supported mode, with ten units per litre. Zero denotes duration-only. The plan screen exposes the water option independently of normal/misting/soak selection and clamps litre input to 0.3..6000. Cycle intervals remain minutes; misting intervals remain seconds. This source evidence does not establish physical threshold enforcement.

Offline coverage includes exact bytes and boundaries, rejection of invalid limits, preservation of existing out-of-write-range readings, seasonal and recurrence guards, every zone/mode/recurrence combination with and without a limit, and actual Windows create/edit controls. The portable suite passes 890 cases per framework and the WPF suite 65 per framework (1,910 total). Results are retained under `artifacts/volume-plans-corrected` and `artifacts/volume-plans-offline`; initial fixture-metadata failures remain alongside their corrected results.

The explicit disabled-volume round trip passed sequentially on net10.0 and net472, covering normal irrigation, cycle-and-soak and misting with a 1.4 L limit. Each plan was disabled, used the remote future date 31 December 2083, and was removed after read-back. Complete original configuration was restored after each mode; the private recovery journal is absent. No valve command was sent, and zones 2/3 settings were preserved. Results are in `artifacts/volume-plans-session-live`.

The initial fixture used separate preparation and cleanup logins. Normal-plan storage succeeded but immediate re-login failed; the independent net472 recovery fixture restored the original configuration. A follow-up isolated the rapid-login rejection as API 9993. The final fixture uses one session for all modes and cleanup, with an independent cleanup timeout and no write replay. Initial failed results remain in `artifacts/volume-plans-live` and `artifacts/volume-plans-corrected-live`, with recovery evidence in `artifacts/volume-plans-cleanup`. These tests prove cloud storage and restoration, not populated vendor-app display, RF delivery, scheduled execution or physical volume cutoff.

## Validation and limits — 24 September 2026

34 new offline NUnit cases pass on both net472 and net10.0. They cover normal, misting and cycle-and-soak units; all known repeat codes; weekday mapping; local dates; water limits; disabled plans; zone selection; old/new containers; malformed and unsupported records; fresh discovery; replaced-device rejection; and invalid-zone rejection before network access.

The initial explicit `ScheduleLiveTests.ReadsSavedSchedulesWithoutChangingConfiguration` NUnit test passed on both frameworks with zero saved plans. A first restricted-network attempt failed before cloud access; the network-enabled rerun passed. An initial net472 invocation without the settings environment variable skipped; its configured rerun passed.

24 additional offline write cases pass on both targets, covering exact requests, disabled defaults, limits, record/settings preservation, stale snapshots, changed device identity, firmware gating, cancellation and uncertain-write replay rejection. A further 26 cycle-and-soak cases cover exact encoding, API addition/replacement, calendar validation, minute boundaries, partial last cycles, no final pause, and repeat gaps including the week boundary. Another 21 misting cases verify independent burst/pause seconds, minute duration limits, exact wire requests, shared calendar validation, disabled defaults and settings preservation. With the additional 28 seasonal/rain-delay cases, the complete offline suite has 310 library/dashboard plus 12 Windows cases per framework: **644 passes**.

Live comparison used the owner-authorized account and these NUnit fixtures, run separately through NUnit3TestAdapter:

1. `ScheduleWriteLiveTests.PrepareDisabledZone1PlanForAppComparison` passed on net10.0. It saved one **disabled** zone-1 normal plan: 23:57, 60 seconds, daily, effective 31 December 2083. It read back the expected complete configuration and retained a private recovery journal.
2. `ScheduleLiveTests.ReadsSavedSchedulesWithoutChangingConfiguration` passed on net472 with one zone-1 plan and zero in zones 2/3.
3. The Android app displayed the same normal mode, 11:57 PM start, one-minute duration, Everyday repeat and 31/12/2083 effective date. The plan switch was visibly off. This was a one-off manual app comparison using BlueStacks, not an automated emulator test or a project dependency. No app settings were saved.
4. `ScheduleWriteLiveTests.RemoveDisabledZone1ComparisonPlan` passed on net472. It removed only the fixture plan, compared the complete returned parameter with the original, and deleted the recovery journal after exact restoration. No valve command was sent. The app was signed out and the Android reservation released.

The same sequence subsequently verified a disabled cycle-and-soak plan: `PrepareDisabledZone1CyclePlanForAppComparison` passed on net10.0, and the populated read passed on net472. The app showed Cycle&Soak, 10-minute irrigation duration, five-minute Each Watering, 30-minute Soaking After Watering, a 40-minute estimated schedule, 100% seasonal adjustment, 11:57 PM, Everyday, and 31/12/2083. The switch was visibly off; no app changes were saved. Cleanup initially timed out after 30 seconds. A fresh journal-based reconciliation on net472 passed, restoring the complete original configuration exactly and removing the journal. Results are retained under `artifacts/cycle-soak-*`, including the failed attempt.

Misting followed the same sequence: `PrepareDisabledZone1MistingPlanForAppComparison` passed on net10.0, followed by populated read and cleanup on net472. The app showed Misting Irrigation, 10-minute irrigation duration, 10-second Run Time, 20-second Interval, 10-minute seasonal-adjusted duration at 100%, 11:57 PM, Everyday and 31/12/2083. The switch was off. No app changes were saved. Cleanup restored the full original configuration exactly and removed the private journal. These runs passed without retry; results are under `artifacts/misting-*`.

Live validation establishes creation/readback/display/deletion of disabled normal, cycle-and-soak and misting plans. At that checkpoint, replacement, enable/disable and other recurrence variants were only offline-tested; the later all-zone live matrix below establishes disabled replacements and recurrence storage. Nonzero volume storage and restoration subsequently passed on both targets as recorded above. The [Windows Plans tab](WINDOWS-APP.md#saved-plans) now supports all three zones with offline NUnit and WPF coverage on both frameworks. Scheduled execution, RF delivery and enforcement of water-volume thresholds remain follow-up work. Seasonal settings and rain-delay expiry now have [typed operations](PLAN-SETTINGS.md); their effect on scheduled execution remains unverified. There are no test plans left on the timer.

## Disabled all-zone schedule round trips

The explicit all-zone fixture extends live coverage to creation, replacement and deletion across normal, cycle-and-soak and misting plans in zones 1, 2 and 3. It now exercises all five supported recurrence writes and alternates between duration-only and volume-limited definitions. Each plan remains disabled with a distant future effective date. Every write is read back and the complete original timer configuration must be restored after each zone/mode pair.

The fixture has exact-identity recovery journaling and stops on uncertainty. See [selection and reconciliation instructions](../tests/README.md#disabled-all-zone-schedule-round-trips). Results are recorded separately after execution; adding the fixture alone does not establish a live pass. Enabled operation and physical timing remain separate checks.


Before the Once restriction, the matrix passed on net10.0 and net472 on 25 September: 54 zone/mode/recurrence combinations per runtime, nine temporary plans created/deleted in each run, and full original configuration restored after every mode. The journal and temporary replacement file are absent. Results are in ignored `artifacts/all-zone-schedules-live`.

The first net472 attempt failed while persisting the next recovery record, before its corresponding cloud write; cleanup restored the original settings. That failure remains retained. A separate local probe reproduced Windows atomic-replacement failure `0x80070497` and sharing failure `0x80070020`. The fixture now retries only bounded local replacement failures that preserve both file names, following [Microsoft's ReplaceFile error contract](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew). Isolated checks on both runtimes verified 180 replacements, temporary-lock recovery and persistent-lock failure. No cloud request is retried. This affects fixture bookkeeping, not the public client API.

## First enabled execution check — 25 September 2026

The explicit net472 once-only fixture created and enabled a normal 60-second zone-1 plan for 06:01 Europe/London local time (05:01 UTC). Full configuration read-back confirmed the disabled and enabled states, but no new normal-mode MQTT start arrived in the bounded execution window. The run failed; cloud storage is not counted as scheduled-execution success.

Cleanup removed the exact fixture plan, restored the complete original timer configuration, sent a zone-1 Stop, confirmed a closed cloud state and removed the private journal. Results, including the earlier precondition-only failure, remain in ignored `artifacts/scheduled-zone1-live`. No other zone was commanded. No claim is made that physical water flowed.

Subsequent read-only diagnostics found both hub and timer enabled and a device-local report consistent with Europe/London daylight-saving time. The app's inspected RF save path uses the same cloud update endpoint, and its native follow-up updates local app data. These findings narrow the investigation but do not establish why execution was absent. Another watering attempt must be based on new evidence rather than repeating acceptance checks. Recurrence execution, seasonal effects, rain-delay behavior and volume cutoff remain unverified.


### Official-app permission prerequisite

A further one-off app inspection on 25 September confirmed rain delay off, seasonal adjustment at 100%, and no remaining plans. The shared account did not have an Add Plan control. The inspected RF app screen shows that control, and permits plan-detail editing, only when the current home reports owner or administrator access. This is a relevant difference between cloud parameter storage and the official app's permitted editing path; it does not by itself explain the missed scheduled start.

The explicit scheduled-execution fixture now requires reported owner or administrator access before writing anything. A separate read-only `SharedAccountLiveTests.PairedHomeReportsAccountRole` check reports only the access flags, without changing membership or printing account identifiers. Unknown access fails the check. No account is promoted automatically, and no configuration write is replayed to investigate permissions.

The read-only role checks confirmed the original account as owner/administrator (net472) and the shared account as a regular member (net10.0). The earlier failed scheduled execution used the member account. This is a test prerequisite mismatch, not proof that membership caused the missed start. Results are retained in `artifacts/account-role-live`; membership was not changed.


### Common model versus this timer's recurrence picker

The vendor's common RF model defines recurrence code 0 as Once and decodes an effective date for it. However, the inspected RF plan editor and its recurrence picker offer only daily, odd dates, even dates, custom weekdays and interval days. The picker does not offer Once. The earlier client could encode the common Once representation and its disabled form survived cloud storage. After both enabled Once checks failed while a daily plan succeeded, new Once writes and enabling are rejected. Decoding and historical calendar projection remain available. A failed Once test must not be generalized to every recurring plan, nor may model-level support be advertised as verified firmware behavior.


### Owner-account Once comparison

The follow-up net472 Once test used confirmed owner/administrator access, enabling one 60-second zone-1 plan for 06:52 Europe/London (05:52 UTC). It also failed to receive a start. The monitor remained connected, accepted no pushes during the window, and had a successful poll five seconds before observation ended. Therefore the first failure cannot be attributed solely to member permissions.

Cleanup at 05:53:28 UTC removed the exact plan, restored the complete original configuration, acknowledged Stop, confirmed closed status and removed the recovery journal. Result: `artifacts/scheduled-zone1-live/scheduled-owner-net472_net472_20260925065328.trx`. No physical-flow claim is made. A separately opted-in daily comparison uses a recurrence offered by the app, then deletes the recurring plan after the single observed occurrence.


## Daily execution passed and Once writes restricted — 25 September 2026

The owner-account net472 daily comparison enabled one normal 60-second zone-1 occurrence for 07:01 Europe/London (06:01 UTC). MQTT reported active normal watering at 06:01:02.762 UTC and automatic idle at 06:02:01.780 UTC, 59 seconds apart and before the cleanup Stop. The observer remained connected. At 06:02:14 UTC cleanup confirmed complete original configuration, removal of the recurring plan, acknowledged Stop, closed cloud state and removal of the private recovery journal. Result: `artifacts/scheduled-zone1-live/scheduled-daily-owner-net472_net472_20260925070214.trx`.

During this run, the separate shared account's official app was used only to read the saved state. It displayed the enabled Normal Irrigation plan, Everyday recurrence, one-minute duration and a next start of 07:01 on 25 September. It showed no next plan after cleanup. This is a populated next-start and plan-details comparison, not a completed traversal of the calendar screen or proof of independent physical flow. Both app and cloud last usage were 0.0 L; no measured-volume claim is made. The app was signed out and the Android reservation released.

Two Once attempts failed to report a start; the daily comparison passed. Together with the app picker offering no Once option, this is sufficient to exclude Once from the supported HTV345FRF write surface. It is not a claim that no firmware or other timer family can implement the common code. All three zones now reject Once creation/replacement and enabling; reads, disabling, deletion and historical projection remain supported. The Windows editor offers five recurring choices and permits safe cleanup of existing Once records. It does not silently convert an existing Once record into a daily plan.

The revised disabled matrix contains 45 combinations (three zones, three modes, five writable recurrences). Its historical 54-combination results are retained, including their Once storage observations; they are not reclassified as execution passes. The Once actuation fixture is retired, with its failed results retained. The daily execution fixture remains explicit and restores the recurring plan after one observed occurrence. No further watering was required for this correction.

Full offline validation passed 1,320 portable cases and 87 WPF cases on each runtime (2,814 total), with no failures or skips. Regression coverage rejects Once writes before network access in every zone/mode/enabled-state combination and checks all-zone read/disable/delete behavior and actual Windows controls. Results: `artifacts/once-restriction-offline` and `artifacts/once-restriction-wpf`. Other recurrence execution, seasonal effects, rain-delay suppression/resumption and volume cutoff remain unverified.

The same correction passed the separate processor workflow: 2,640 local and 1,320 processor checks, no failures/skips, test-instance removal and reservation release confirmed. The existing archive was preserved. Nothing was pushed or released.

## Selected-weekday volume-limit execution — 27 September 2026

`ScheduledZone1LiveTests.VolumeLimitedZone1PlanStopsBeforeDuration` passed on net10.0. The fixture enabled one zone-1 occurrence for Sunday 27 September at 11:05 Europe/London (10:05 UTC), using the Weekdays recurrence, a 1.0 L limit and a 180-second duration cap. The enabled plan was created after the zone-3 manual run, with its due time more than seven minutes after that run's final stop. No manual start was sent.

Fresh MQTT reports showed normal watering at 10:05:03.297 UTC and idle at 10:05:22.302 UTC, 19.0 seconds apart. Last usage was reported as 1.1 L at completion. The idle report arrived before cleanup and well before the three-minute cap, supporting volume-limit enforcement for this one zone/mode/recurrence combination. This does not independently measure the physical water volume, establish an exact one-litre cutoff, or verify other modes/zones/recurrences. The owner could not watch.

At observation end the monitor state was Reconnecting, despite accepting the two relevant fresh push reports; the last successful poll was 10:03:54 UTC. This transient state is retained in the evidence and is not described as an uninterrupted MQTT session. Cleanup remained successful: at 10:05:28 UTC the exact original timer configuration was restored, the temporary recurring plan was removed, Stop had been acknowledged, final zone-1 cloud state was idle, and the recovery journal was deleted. A further idle push arrived during cleanup.

Result: `artifacts/volume-execution-live/volume-zone1-180_net10.0_20260927110529.trx`. This is the first live execution check of the saved volume cap and the Weekdays recurrence; the earlier disabled storage tests and failed Once execution checks retain their original status. No further watering was performed after this result.



## Odd-day execution, seasonal duration and simultaneous accounts — 27 September 2026

The explicit net472 check programmed one zone-1 odd-day occurrence for 23:14 Europe/London on 27 September, with a configured duration of 120 seconds and the current month's seasonal adjustment temporarily set to 50%. Both already shared accounts connected their MQTT observers before any setting was changed. Both received identical fresh active and idle timestamps: 22:14:02.423 and 22:15:01.466 UTC, 59.043 seconds apart. Completion reported 3.2 L. This establishes device-reported odd-day execution, the tested seasonal duration effect and simultaneous changing event delivery to the two accounts.

The original full timer configuration was restored, the temporary recurring plan was removed, cleanup stop was acknowledged and final idle confirmed at 22:15:05 UTC; the recovery journal was removed. Evidence: ignored `artifacts/schedule-behavior-live/odd-season-shared-retry_net472_20260927231506.trx`. The first attempt timed out during initial login before writes and remains a failed result. Neither flow-meter accuracy nor independent physical burst timing was measured.


## Interval plan and rain-delay execution — 27 September 2026

`IntervalPlanSkipsRainDelayThenResumes` passed on net472. A two-day interval plan anchored on 27 September was due at 23:32 home-local time while rain delay remained active until 23:35. The selected zone stayed idle throughout the bounded observation window, including a successful status read after the missed occurrence, with the push observer connected. The fixture then explicitly cleared rain delay and moved the same plan to 23:39. Fresh active/idle reports were 59.053 seconds apart, with 3.3 L reported. This verifies suppression followed by an explicitly rescheduled occurrence after clearing the delay; it does not establish natural expiry behavior for an unchanged plan.

Cleanup acknowledged stop, restored the entire original configuration, confirmed idle and removed the recovery journal at 22:40:10 UTC. Result: `artifacts/schedule-behavior-live/interval-rain-resume_net472_20260927234011.trx`.

## Even-day execution and cleanup recovery — 28 September 2026

The even-day plan started on the actual home-local date 28 September at 00:02. Fresh MQTT active/idle reports were 58.936 seconds apart, with 3.3 L reported. The net472 fixture subsequently failed because a cleanup configuration read timed out. Its separate stop was accepted, but the recovery journal correctly remained.

The explicit net10.0 cleanup-only run then passed: it removed the exact temporary plan, verified the full original configuration, acknowledged stop, confirmed zone 1 idle and removed the journal at 23:04:36 UTC (00:04:36 local). No second start was sent. The original failed run is `artifacts/schedule-behavior-live/even-day-60_net472_20260928000357.trx`; recovery is `even-day-reconcile_net10.0_20260928000436.trx` in the same directory. This establishes observed even-day execution plus verified recovery, not a passing uninterrupted fixture.
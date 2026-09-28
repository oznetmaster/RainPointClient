# Seasonal adjustment and rain delay

The HTV345FRF schedule snapshot now includes two independently decoded settings. `GetTimerSchedulesAsync` reads them with fresh device discovery. An unavailable or malformed setting is not presented as a default.

- `SeasonalAdjustmentAvailability` and `SeasonalPercentages`: twelve integer percentages, January through December, in a read-only collection. The supported range is 10..200. An empty or missing field remains `NotReported`, even though the app may display a default of 100%.
- `RainDelayAvailability` and `RainDelayUntil`: a home-local `DateTime` with `Unspecified` kind, or null for an explicitly zero field. Expired timestamps are retained, including the app's 1 January 2020 clear sentinel. Determine activity by comparing the value with the home's current local time, after checking availability.

## Writes

```csharp
RainPointScheduleSnapshot before = await client.GetTimerSchedulesAsync(
    hub, timer.Address, 1, cancellationToken);
await client.SetTimerSeasonalAdjustmentAsync(hub, before,
    new[] { 100, 100, 100, 100, 100, 100, 100, 100, 90, 100, 100, 100 },
    cancellationToken);

before = await client.GetTimerSchedulesAsync(hub, timer.Address, 1, cancellationToken);
// Supply the intended HOME-LOCAL expiry, not the computer's UTC/local DateTime.
await client.SetTimerRainDelayAsync(hub, before,
    new DateTime(2026, 9, 25, 11, 0, 0, DateTimeKind.Unspecified), cancellationToken);

before = await client.GetTimerSchedulesAsync(hub, timer.Address, 1, cancellationToken);
await client.SetTimerRainDelayAsync(hub, before, null, cancellationToken);
```

Rain-delay expiry accepts whole seconds in 2020..2083. Null clears the four-byte field to zero, the disabled representation observed on the actual timer. The app offers 24/48/72-hour choices; callers can calculate the desired expiry using the home's calendar. The API does not guess a time zone, handle DST automatically, apply a home-wide delay or send a valve stop command.

Seasonal writes require an existing valid twelve-month field in the modern RF container. Rain-delay writes require an existing readable timer-settings field. Both require a decoded schedule list, three-zone HTV345FRF and numeric firmware 120 or newer. Missing or unfamiliar layouts are rejected instead of converted.

Writes preserve other zones, schedules, sensor flags, calibration, auxiliary fields and unknown suffixes. They use the schedule API's full-configuration freshness check and single-write snapshot guard. Read again after every attempted write, including a timeout; no uncertain write is automatically replayed. The server has no established atomic compare-and-swap contract, so concurrent editors must still be avoided.

## Seasonal timing checks and limitations

Before changing percentages, the client checks the largest supplied percentage against existing plan durations, rounded to whole minutes as in the app and with a one-minute minimum. Volume-limited plans retain their configured duration. Normal/misting durations cannot exceed 12 hours; cycle-and-soak watering cannot exceed 24 hours, and its pauses must fit the repeat spacing. These checks include disabled plans, as the app's validation does.

This does not yet calculate conflicts between separate plans or zones, next-run times, a calendar, or actual firmware execution. Existing schedule creation/replacement checks have not gained seasonal conflict validation in this slice. The setting is stored and read back, but changing a percentage has not been tested during a running plan. Rain-delay suppression and expiry/resumption also remain unverified on hardware.

## Protocol evidence

RainPoint Home 1.19.1065's bundled common RF parameter code stores twelve month bytes in the fourth comma-separated field of each modern zone container. Its seasonal page saves through the same sub-device update endpoint as schedules. The timer's first settings field contains the rain-delay timestamp at byte offsets 8..11. It packs seconds, minutes, hours, day, month and year offset from 2020 into a little-endian 32-bit value. Only those four bytes are replaced. The app clears to its expired default date; the connected timer's original cleared fields were all zero. Both are decoded distinctly.

See [schedule protocol provenance](SCHEDULES.md#protocol-evidence) for the inspected package hash and scope. Vendor source and private configuration captures are not distributed.

## Validation

28 offline cases cover reads, immutable month values, exact writes and preservation, range checks, malformed data, date kinds, year limits, fractional seconds, unsigned packed dates, stale snapshots, uncertain-write rejection, no-op clears and seasonal duration limits. They pass on both target frameworks. The complete suite has 310 library/dashboard plus 12 Windows tests per framework: **644 passes**.

The explicit `TimerPlanSettingsLiveTests` preparation and restoration tests use the NUnit adapter, a private recovery journal and an empty-plan precondition for all three zones. Only zone 1 is edited. See [test instructions](../tests/README.md). App display comparison is a one-off observation, not a dependency on BlueStacks.

On 24 September 2026, the net10.0 read-only baseline found all zones empty, all twelve percentages at 100%, and zero rain-delay fields. `PrepareZone1SettingsForAppComparison` passed on net10.0 with zone 1 set to twelve 90% values and expiry 25 September 2026 at 11:00 home-local. The Android app displayed the same end time and all twelve monthly percentages. No app changes were saved. `RestoreZone1Settings` passed on net472, restoring the complete original timer configuration exactly and removing the recovery journal. There were no valve commands. Retained results are under `artifacts/plan-settings-baseline`, `artifacts/plan-settings-prepare-net10` and `artifacts/plan-settings-restore-net472`.

An initial offline test contained an incorrectly hand-calculated month/date fixture; the independent bit-layout check corrected it, and the focused rerun and complete suite passed. The initial failure remains in the test artifacts. Live configuration storage and app display are verified; actual suppression, timed resumption and seasonal watering behavior are not.

## Windows coverage

The Windows Zone settings tab now edits twelve monthly percentages and rain-delay expiry for all three zones, with fresh snapshots and cloud read-back. NUnit tests exercise actual controls on both Windows targets. See [Windows usage](WINDOWS-APP.md) and [current test totals](../tests/README.md); the earlier totals above are historical slice results.

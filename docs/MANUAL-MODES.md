# Manual misting and cycle-and-soak

The client and Windows workbench support normal watering, misting and cycle-and-soak for every HTV345FRF zone. New cyclic commands require discovery to report timer firmware 120 or newer (`SupportsManualCycles`). Unknown or older firmware is rejected before any request. Normal start/stop remains available under its existing rules.

```csharp
// One configured minute of misting with 10-second bursts and 20-second pauses.
RainPointWateringCommandResult result = await client.StartMistingAsync(
    hub, timer.Address, zone, TimeSpan.FromMinutes(1),
    TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), cancellationToken);

// Five watering minutes, split into one-minute bursts with one-minute soaking pauses.
result = await client.StartCycleAndSoakAsync(
    hub, timer.Address, zone, TimeSpan.FromMinutes(5),
    TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), cancellationToken);

// The same stop operation applies to every mode.
await client.StopWateringAsync(hub, timer.Address, zone, cancellationToken);
```

| Mode | Configured duration | Burst and pause | Transport units |
| --- | --- | --- | --- |
| Normal | 60–43200 whole seconds | None | Duration in seconds, empty parameter |
| Misting | 1–720 whole minutes | Each 5–3600 whole seconds | Mode 2; duration and intervals in seconds |
| Cycle-and-soak | 5–1440 whole watering minutes | Each 1–720 whole minutes; burst no greater than total watering | Mode 3; duration and intervals in minutes |

All public inputs are `TimeSpan`. Cyclic parameters encode burst followed by pause as two little-endian unsigned 16-bit values. There is no public payload argument. Fractional units and values outside these limits are rejected, not rounded or clamped. The caller supplies intervals explicitly; commands do not change saved defaults or plans, or apply monthly seasonal percentages locally. No command is retried automatically.

Cycle pauses extend elapsed time beyond the configured watering duration. Actual misting treatment of pauses, RF delivery and physical burst timing must not be inferred from acknowledgement alone. The result separates [acknowledgement and optional reported feedback](COMMAND-FEEDBACK.md). Cancellation, logout and closing the Windows app do not stop a run already delivered to the timer.

## Windows controls

Choose Normal, Misting or Cycle and soak in Manual watering, then enter the displayed duration and interval units. Changing modes resets the form to short defaults and disarms controls. All zones have the same controls. The selected timer's firmware gates cyclic Start; malformed inputs also block Start. Stop does not depend on timing fields or reported valve state once controls are enabled. Burst/pause inputs are inactive for Normal mode.

## Evidence and validation

The contract is traced from RainPoint Home 1.19.1065's bundled RF timer code: manual selector/form 657, misting picker 661, soak picker 1031, command handler 538, parameter encoder `prefixParamZero`, `getPlanVersion` and `controlWorkMode`. The manual form passes cycle duration/intervals in minutes, and misting duration/intervals in seconds. The handler appends both intervals in little-endian order. The firmware capability boundary is consistent with the existing modern schedule support. The Python references cover normal valve control but do not establish this full manual form. Private vendor source remains outside this repository.

On 24 September 2026, all 788 portable library/dashboard and 56 actual Windows cases passed per target (1,688 offline passes). The new slice adds 54 protocol and eight dashboard cases, plus six additional WPF mode/zone combinations. Exact requests, boundary units, little-endian values greater than 255, firmware gating, malformed inputs, no retry and unchanged stop requests are covered. Results: ignored `artifacts/manual-modes-corrected`.

The explicit `Zone1LiveTests.OneMinuteZone1MistingReportsMqttTransitions` requires private settings and `RAINPOINT_LIVE_ZONE1=misting60`. It waits for a closed baseline and connected MQTT observer, sends one start with a one-minute configured duration and 10/20-second intervals, then sends cleanup stop at 65 seconds from the attempt. Cleanup has an independent cancellation budget if the test fails. It checks a new open observation, a closed observation after the stop attempt and final closed cloud status. No zones 2/3 commands or saved-plan/settings writes are sent. This fixture does not measure physical timing or volume. Cycle-and-soak actuation remains unverified.

## Mode feedback correction

The first net10.0 live misting run was accepted and received three decoded MQTT updates, but its transition assertions failed. The old status decoder treated only bit 0 as the active flag, incorrectly displaying mode 2 as closed. The app's status model 495 reads the complete low nibble: 0 idle, 1 normal, 2 misting, 3 cycle-and-soak. The decoder now exposes typed `WorkMode` and numeric `WorkModeCode`; known active modes set `IsOpen=true`, while unknown mode codes leave activity unknown. High control-origin bits are not activity flags. Fifteen all-zone regression cases cover these values. The Windows app labels cyclic activity as Reported misting or Reported cycling; it does not claim physical opening during every pause.

The initial failed run remains in `artifacts/manual-misting-live`. Its cleanup stop was accepted, final cloud status was closed, and last usage was zero. That does not demonstrate physical water delivery. Repeats must remain short, restricted to zone 1, with intervals between runs. The current validation session uses at least five minutes after a stop before another start and limits repetition.

The corrected net472 repeat passed on 24 September 2026. The start was sent at 15:43:28 UTC, over six minutes after the previous cleanup stop at 15:37:24 UTC. MQTT reported active misting at 15:43:29, idle at 15:44:29, and idle again after the explicit cleanup stop at 15:44:33. The final cloud read was closed. Last reported usage remained 0 L, so water delivery, physical timing and volume are not established. Evidence: `artifacts/manual-misting-corrected-live`. The first failed net10 run is retained; the corrected code passes offline on both targets, while this successful misting hardware check was net472 only. No further watering run was made in this batch. Zones 2/3 were never commanded.

### Corrected .NET 10 live check — 25 September 2026

The matching .NET 10 misting fixture also passed. It sent one zone-1 start at 04:29:13 UTC, observed new active-mode feedback at 04:29:14 and idle feedback at 04:30:13, then sent the cleanup stop at 04:30:18. That stop was acknowledged and a newer idle MQTT report and final closed cloud reading followed at 04:30:19. There were four accepted push updates. Evidence: ignored `artifacts/manual-misting-corrected-live/misting-net10_net10.0_20260925053019.trx`.

The corrected feedback path now has successful live evidence on both desktop runtimes. Reported usage remained 0 L and nobody confirmed physical flow during this run; it establishes reported mode transitions and closure, not delivered water or burst/pause timing. No saved configuration or zones 2/3 commands were sent. The preceding watering test ended on 24 September, so this was a single isolated run rather than a rapid sequence. Physical cycle-and-soak and saved-plan execution remain open.


## Cycle soaking feedback — 25 September 2026

The explicit net472 cycle test sent one zone-1 command at 10:10:15 UTC, with five watering minutes, one-minute watering bursts and one-minute pauses. It deliberately stopped the program after 150 seconds. MQTT reported mode 3 at 10:10:16.345, mode 7 at 10:11:16.331, mode 3 at 10:12:16.410 and idle at 10:12:47.184. Stop was acknowledged; the final cloud state was idle and zones 2/3 retained their baseline modes. Result: ignored `artifacts/manual-cycle-live/cycle150-preserve-net472_net472_20260925111249.trx`.

The owner saw no water in either burst, and usage remained zero. The master supply is being checked. This is reported program-state evidence, not physical cycle delivery. Earlier attempts failed before any start: a net10 login timeout and a net472 precondition requiring every zone idle. The latter was too strict for the owner's normal zone-3 tap use; the test now requires zone 1 idle and preserves other-zone modes. Both failures remain retained.

RainPoint Home's RF control screen (bundle module 538) groups modes 3 and 7 as cycle-and-soak, labels mode 3 watering and mode 7 soaking, and disables the watering animation during mode 7. That matches the observed minute-long pause. `RainPointWateringMode.CycleAndSoakPause` exposes code 7; `WorkModeCode` retains the numeric code. `IsOpen` continues to mean an active reported irrigation program, so it remains true during this pause: callers must not mistake the pause for a completed run or independent valve-position feedback. Other unknown codes remain unknown. The Windows status is `Reported soaking (paused)`.

Six additional portable regression cases cover mode 7 and the 3/7/3/0 sequence across all three zones, including high control-origin bits and the Windows presentation model. The strengthened live fixture also requires fresh pause/resumption reports and bounded one-minute intervals; its earlier successful result predates those added assertions.

### Correction to the zone-3 observation

At 11:06 UK time the initial test decoded zone 3 as active from a polled timer record whose last-change timestamp was 09:07:36 UK. The first fixture did not retain the original frame or zone-3 mode code. A subsequent read-only observation reported all three zones idle, with a newer last-change timestamp of 11:06:21 UK; the owner also confirmed that the official app and actual timer showed zone 3 closed. The earlier report must not be described as a physically open valve. The evidence is consistent with an older cloud state but cannot prove the cause of the discrepancy without the original frame; a last-change timestamp alone is not proof of staleness. No zone-3 command was sent. The owner's usual tap use does not establish its state during this test.

## Cycle delivery feedback — 27 September 2026

The bounded net10.0 cycle-and-soak test passed with `RAINPOINT_LIVE_REQUIRE_FLOW=1`: fresh watering at 22:51:46.090 UTC, soaking pause at 22:52:45.954, resumed watering at 22:53:45.895, then cleanup idle at 22:54:16.494. The two phase intervals were approximately one minute. Final reported usage was 5 L, with a fresh completion event, acknowledged stop and unchanged other-zone modes. This establishes reported cycle sequencing and water delivery without requiring the owner to watch. It does not calibrate the flow meter or independently measure each physical burst. Result: `artifacts/manual-cycle-live/cycle150-flow_net10.0_20260927235417.trx`.

## Misting delivery feedback — 28 September 2026

The net10.0 one-minute misting check passed with fresh-completion/nonzero-flow validation enabled. A single start specified 10-second watering and 20-second pause intervals. The timer reported active at 23:11:38.455 UTC and automatic idle at 23:12:38.474, with 1.1 L. Independent cleanup stop was accepted at 23:12:43; the final selected zone was idle and other-zone modes unchanged. Result: `artifacts/manual-misting-live/misting60-flow_net10.0_20260928001244.trx`. These fresh reports establish misting completion and nonzero delivered-volume feedback. Individual physical 10/20-second burst boundaries were not independently observed.
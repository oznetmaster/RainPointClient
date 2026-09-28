# Short live tests on all three timer zones

On 27 September 2026 the owner made all three zones of the paired HTV345FRF available for short live tests. This supersedes the earlier zone-1-only actuation restriction. The owner cannot watch these runs, so results establish reported operation, not independent physical valve movement or calibrated water volume. Tests run sequentially, below ten minutes each, with at least five minutes between runs.

The explicit NUnit fixture `Zone1LiveTests` retains its existing test identities and now also provides `OneMinuteZone2ReportsMqttTransitions` and `OneMinuteZone3ReportsMqttTransitions`. The selected-zone helper requires exactly one supported paired hub/timer, an idle selected zone, a connected observer, empty schedules and inactive moisture rules. It sends one 60-second start with no retry, then an independent stop around 65 seconds after the request. New MQTT timestamps must follow the start attempt. Final reads require the selected zone idle and other zones' reported modes unchanged; those other zones are never commanded.

Both net472 and net10.0 builds passed with zero warnings/errors. The unchanged library/dashboard offline suites passed 1,330 cases per runtime (2,660 total), under `artifacts/all-zone-fixture-offline`. These fixture-only changes do not alter production library code or require another processor package deployment.

## Zone 2: net472

The one-minute run passed on 27 September. UTC timestamps: start request 09:49:49; accepted and fresh normal-mode MQTT report 09:49:50.837; idle MQTT report 09:50:49.756; cleanup stop sent 09:50:54 and accepted 09:50:55. Final cloud read reported idle and the other zones retained their reported modes. The automatic idle report preceded the cleanup command, about 59 seconds after the active report. Last usage changed from 2.6 L before the run to 2.8 L at the idle report; 2.8 L is the reported last-run amount, not the difference between those values.

Evidence: `artifacts/all-zone-manual-live/zone2-60_net472_20260927105056.trx`. No claim of visually confirmed flow is made.

## Zone 3: net10.0

The one-minute feedback run passed on 27 September. UTC timestamps: start request 09:56:38; accepted 09:56:39; fresh active report 09:56:40.096; idle report 09:57:38.764; cleanup stop sent 09:57:43 and accepted 09:57:44. Final cloud state was idle and the other zones retained their initial reported modes. The automatic idle report preceded cleanup, about 59 seconds after the active report. This start followed the zone-2 cleanup by more than five minutes.

Last usage was 1.8 L before the run and **0 L** at completion. The fixture verifies fresh work-state transitions, not nonzero water delivery; the zero result is retained explicitly and is not a physical-flow pass. The owner could not watch during the run and subsequently confirmed that zone 3 feeds a tap whose downstream manual valve is closed. Zero reported usage is therefore consistent with that closed outlet and is not evidence of a client or timer fault. The run establishes reported start/stop behavior; physical opening and water delivery remain unobserved.

Evidence: `artifacts/all-zone-manual-live/zone3-60_net10.0_20260927105745.trx`.

## Zone 1: net10.0 saved volume-limit plan

After the next interval, the previously prepared one-litre/three-minute-cap test passed: one selected-weekday occurrence reported active at 10:05:03.297 UTC and idle at 10:05:22.302 UTC, with reported last usage 1.1 L. Cleanup restored the exact original settings, removed the recurring plan, acknowledged Stop, confirmed idle and removed the journal. See [full schedule evidence](SCHEDULES.md#selected-weekday-volume-limit-execution--27-september-2026), including the transient Reconnecting monitor state and limits of reported-volume evidence. This batch contained three starts total, one per zone; no additional watering run followed.

# Zone 1 live test — 23 September 2026

**Current outcome:** the owner visually confirmed physical start and automatic stop during the corrected .NET 10 60-second run, and on/off during the net472 explicit-stop run. Both the client and official app can continue displaying the earlier open cloud reading after water stops. Exact actuation latency was not independently timed. The initial sub-minute attempts below are retained as failed/inconclusive history.

Only zone 1 was authorized for actuation. Zones 2 and 3 were not commanded. The test ran against the .NET 10 client from a separate local test harness; the shipped sample remains read-only.

All times below are UTC.

| Time | Observation |
| --- | --- |
| 15:59:56 | Initial cloud status: zone 1 closed, duration 0. |
| 15:59:56–57 | Start zone 1 for 10 seconds; cloud acknowledged acceptance. |
| 15:59:59 | Cloud reported zone 1 open and duration 10; data-change timestamp 15:59:57.422. |
| Through 16:00:25 | Subsequent readings retained that open state and unchanged timestamp. |
| 16:00:57 | After a status request timed out, the cleanup stop was sent for zone 1. |
| 16:01:12 | Cloud acknowledged the stop. |
| 16:01:15–18 | Status still returned the earlier open reading with the original timestamp. |
| 16:01:22 | Cleanup observation exceeded its initial time budget; closure remained unconfirmed. |
| 16:02:17 | A read-only follow-up again returned the same earlier open reading. |
| 16:03:32 | A follow-up status request timed out with a 65-second HTTP allowance. |

Result: **inconclusive actuation verification**. Start and stop API acceptance and an open cloud reading were observed. Automatic shutoff timing and physical movement were not confirmed by the probe. Afterward, the owner checked the RainPoint app and confirmed that it reported zone 1 closed. The second planned start/early-stop test was skipped because closure was not established. No command was replayed automatically; the one explicit cleanup stop is recorded above.

The owner reports cloud latency sometimes reaches 30 seconds. The initial observation budgets were therefore too short to distinguish all delayed transitions reliably. The later status request also timed out; these observations do not establish a valve timer failure. Future live tests should allow for cloud latency and compare app/physical state, while keeping final stop cleanup independent of caller cancellation.

Follow-up: the owner confirmed the RainPoint app reports zone 1 closed. This resolves the immediate app-state check; direct physical observation and the distinction between automatic timeout and the explicit stop remain unverified. No further actuation was performed. The Python references normally use multipleDeviceStatus for polling and getDeviceStatus as a fallback; comparing these read paths is the next diagnostic step, not yet evidence of the cause.
## Follow-up investigation

Both read endpoints returned the same closed record during an idle comparison at 16:11–16:12 UTC. A repeat using multipleDeviceStatus then showed cloud open/closed transitions for 10 seconds and an early stop of a 30-second run, but the owner reported **no water**. Those cloud transitions are not a successful physical test and do not establish that changing endpoints solved the earlier stale reads.

The exact HTV345FRF manufacturer manual (printed page 21, PDF page 22) specifies normal manual watering from one minute to 12 hours. Its one-second timing belongs to misting. Client validation was corrected to 60..43200 whole seconds for the implemented normal-irrigation command. Batch polling was adopted to match the principal references, with typed addressing and hub-ID matching.

A subsequent single 60-second start was accepted at 16:18:50 UTC. The owner visually confirmed **water started** and later **water stopped** before an explicit cleanup stop was sent. This establishes physical start and automatic stop for zone 1; exact elapsed watering time was not independently measured. The cloud continued reporting the previous open record afterward, proving that polled work-state is not real-time flow confirmation. Zones 2 and 3 were never commanded.
## Explicit stop on net472

At 16:23:34 UTC the net472 client sent a zone-1 normal-irrigation start with a 60-second duration. The cloud acknowledged at 16:23:35. An explicit stop was sent at 16:24:00, 25 seconds after start acknowledgement, and acknowledged at 16:24:01. The owner subsequently reported observing water "on" and "off" during this test. This provides physical start/stop confirmation in addition to the .NET 10 automatic-stop observation; no precise latency claim is made from chat timing.

The 16:24:11 status read still reported open. The owner also observed the official app showing flow during this cloud lag. Batch polling therefore does not eliminate the upstream delay, and neither the cloud work-state nor API acknowledgement is proof of present flow. The client exposes the reported state and data-change timestamp and does not fabricate closure from an elapsed duration.

Final observation: water off, confirmed by the owner. No further actuation was sent. Zones 2 and 3 remain untested. The API library has 80 passing offline tests on each target framework and the full Release solution builds without warnings/errors.
## Water-usage feedback comparison

The owner subsequently reported zone 1 last usage of **1.4 L** in the app. A read-only client request at 16:32:42 UTC on 23 September returned zone 1 **closed**, configured duration **0 seconds**, and **14 usage counts**, with a data-change timestamp of 16:31:01 UTC. Zones 2 and 3 reported closed and zero usage. This finally showed the cloud state catching up; it does not quantify the exact lag for the previous stop.

The client now exposes `LastWaterUsageLitres` using 0.1 L/count for HTV345FRF. This matches the owner's rounded display at this one point; there was no unique run ID or independently measured water volume. The comparison required login and status reads only, with no further actuation. Flow rate, daily totals and watering history remain outside the implemented feedback.

## MQTT validation — 24 September 2026

One additional 60-second zone-1 start was explicitly authorized and sent at 03:16:15 UTC, acknowledged at 03:16:16. Polling reported open at 03:16:24. Cleanup stop was sent at 03:17:20, acknowledged at 03:17:21, and polling reported closed at 03:17:26. No physical-flow observation was provided for this run. Zones 2/3 were never commanded; no second watering start was sent.

Four MQTT packets arrived but were rejected because numeric non-timer metadata did not match the original timer-only dictionary model. The full-cycle MQTT fixture failed. A stop-only diagnostic isolated the issue; typed serializer contract filtering now preserves supported timer records while skipping unrelated metadata. Regression coverage was added on both frameworks.

Post-fix stop-only checks passed on net10.0 and net472, with typed MQTT closed-state observations at 03:23:25 and 03:24:07 UTC respectively and closed polling afterward. Last usage was reported as 0 L. Those stop-only checks do not prove physical flow or calibrated volume. A subsequent corrected full cycle is recorded below. See [the full MQTT record](MQTT-FEEDBACK.md#zone-1-follow-up-and-metadata-correction).


## Corrected MQTT full cycle — 24 September 2026

The corrected .NET 10 NUnit cycle passed. One 60-second zone-1 start was sent and acknowledged at 03:29:22 UTC. MQTT reported open at 03:29:23 and closed at 03:30:22, before the independent cleanup stop was sent and acknowledged at 03:30:27. Three MQTT updates were accepted, none rejected. The test finished at 03:30:28 with zone 1 reported closed. No other zones were commanded.

This verifies typed open/closed feedback through the complete monitor on .NET 10. Physical water flow was not independently observed; last usage remained reported as 0 L. net472 has the corrected stop-only MQTT verification and offline regression coverage; a corrected full-cycle test on that target was not performed. See [timestamps and retained NUnit evidence](MQTT-FEEDBACK.md#corrected-full-cycle--24-september-2026).

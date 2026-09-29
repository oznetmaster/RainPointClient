# MQTT feedback

`RainPointMonitor` observes one discovered HWG023WBRF / HWG023WBRF-V2 hub using an authenticated MQTT observer and periodic REST reads. It sends no watering commands. It exposes typed, immutable status updates, connection-state events and accepted/rejected push counters. Raw payloads and observer credentials remain internal. The library does not log.

## Use

Sign in and discover the home and hub before constructing the monitor. Each instance starts once; only one monitor may run on a client. Treat the account's observer as exclusive: another client or the phone app may displace it.

```csharp
var monitor = new RainPointMonitor(client, hub,
    new RainPointMonitorOptions { PollInterval = TimeSpan.FromSeconds(30) });
monitor.StatusReceived += (_, update) => RenderOnUiThread(update);
monitor.StateChanged += (_, update) => ShowMonitorStateOnUiThread(update.State);
Task running = monitor.RunAsync(cancellationToken);
try
{
    await running;
}
finally
{
    await monitor.StopAsync();
}
```

Event handlers run on background threads: post UI work and return promptly. Never block an event handler waiting for `StopAsync`. Consumer handler exceptions are isolated. Parallel callbacks can arrive out of order; a consumer should accept only increasing `Revision` values within the same monitor instance. `Current` is the latest merged snapshot.

`RefreshAsync` requests an immediate serialized poll while the monitor runs. Stopping cancels and joins the MQTT connection and active reads. Await stop before changing accounts, signing out or disposing the client. To monitor another hub, stop the old instance and create a new one. `EnablePush = false` selects polling only.

### Optional polling suppression (1.1.0)

Set `RainPointMonitorOptions.PollWhilePushConnected = false` to stop routine cloud status reads while synchronized MQTT is available. The default remains `true`, preserving existing polling behavior. An initial read and a catch-up read for each new MQTT connection establish the baseline; a reconnect may wait until the next poll interval for its catch-up read. Failed catch-up reads retain polling fallback. Manual `RefreshAsync` still reads immediately.

`LiveUpdatesAvailable` becomes true only when the current authenticated session and MQTT connection have a successful catch-up read. A disconnected or replaced session requires synchronization again. Applications can use this flag to retain unchanged reported state while the observer is healthy. It is not a physical-device heartbeat or proof of valve position; retain explicit unknown/offline values in device reports.

The Windows app offers **Start live feedback** after hub selection. Its monitor polls every 30 seconds; the separate 15-second refresh option is suspended while monitoring. Refresh status uses the monitor's merge path. Selecting another hub/home, signing in, signing out or closing stops the monitor. Selecting another timer on the same hub keeps monitoring. Startup sign-in does not automatically start monitoring or arm controls.

## Ordering and meaning

- Push frames must match the discovered hub ID and a known supported timer address. Malformed, oversized, duplicate-key and implausibly future-dated frames are rejected.
- Each timer retains the latest accepted timestamped reading. Older, equal-timestamp, missing or malformed polls cannot replace a newer decoded reading. Equal-timestamp conflicting values retain the first accepted reading.
- An accepted newer timer snapshot replaces that timer's previous snapshot. Missing fields in the newer snapshot are unknown; they are not filled from older readings.
- Each `RainPointTimerObservation` retains its own source and receipt time. A successful poll can refresh `LastSuccessfulPollAt` without changing an older retained timer observation. The update's `Source` identifies the triggering operation, not the source of every timer in the merged snapshot.
- Hub connection timestamps are reconciled independently. Wi-Fi signal comes from polling.
- Receipt time and a connected broker do not prove fresh physical valve state. Last usage retains the established HTV345FRF litres interpretation; it is not instantaneous flow. Command acknowledgements never update valve readings optimistically.

## Connection and session behavior

When sign-in supplies an account MQTT identity, the monitor retains it privately and registers it through the typed `/app/device/subscribeStatus` request. Token refresh preserves that identity. Older responses without an account identity use the temporary observer credentials returned by registration. The transport uses TLS 1.2 on port 8883, checks the broker hostname and validates its chain against the embedded Aliyun IoT root. It never accepts an arbitrary certificate or changes the machine trust store. As in the upstream observer, certificate revocation checking is disabled for this private CA. The broker's advertised plaintext port is ignored.

The observer receives unsolicited downlinks on its device-specific property-set topic; it sends neither SUBSCRIBE nor application PUBLISH messages. It renews observer credentials before their expiry (bounded fallback when expiry is missing), reconnects with bounded jittered backoff and observes server cooldowns. A manually replaced or refreshed cloud session causes observer renewal.

A rejected/expired session reports `AuthenticationRequired` and waits without repeated authenticated requests. The caller can explicitly renew or sign in again, or use the optional [session recovery worker](SESSION-RECOVERY.md). No watering command is retried.

MQTTnet **4.3.7.1207** is pinned because its framework support includes net472. MQTTnet 5 targets newer runtimes and is not a drop-in update for this client. System.Text.Json remains the only JSON dependency. The library and transitive dependency audit on 24 September 2026 reported no known vulnerabilities in the configured NuGet feeds.

Protocol and trust-anchor reference: [funkadelic/ha-rainpoint](https://github.com/funkadelic/ha-rainpoint/tree/3b1492c62b70e6c194348bbfe68f58cc5920b568), particularly `api/mqtt.py`, `api/client.py` and `certs/ali_iot_ca.crt`. Attribution is preserved in [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).

## Validation on 24 September 2026

Offline NUnit suites passed **177 library/dashboard cases plus 12 WPF cases per target: 378 passes**. Coverage includes TLS trust/hostname/expiry, broker allowlisting, observer authentication, no subscribe/publish, topic/payload filtering, push/poll ordering, cancellation during manual reads, observer expiry, throttling, subscriber failure isolation and queued UI updates after a selection change. Both targets are built from the same sources.

The explicit `LiveMonitorTests.ObserverConnectsAndPollsWithoutValveCommands` fixture passed separately on net10.0 and net472 using the configured HWG023WBRF-V2/HTV345FRF account. Both obtained observer credentials, established a verified TLS MQTT connection and decoded REST timer feedback. No valve or settings commands were sent.

- net10.0: approximately 40 seconds; connected, decoded polling, zero received push frames.
- net472: approximately 93 seconds; connected after a retry, subsequently entered reconnecting, decoded polling, zero received push frames. Remote logout did not complete within the cleanup allowance; the monitor was stopped and the client disposed. This does not establish uninterrupted connection stability.

Idle hardware sent no push frames during those initial runs. Subsequent bounded zone-1 testing and the decoder correction are recorded below. Physical transition latency, long-running credential rotation and independent app comparison remain unverified in this MQTT phase. Decoder and renewal behavior have offline coverage.

Results are retained locally under `artifacts/mqtt-offline-r2`, `artifacts/mqtt-live-net10` and `artifacts/mqtt-live-net472`. See [test setup](../tests/README.md) for explicit NUnit execution.


## Zone-1 follow-up and metadata correction

On 24 September 2026, one 60-second zone-1 start was sent at **03:16:15 UTC** and acknowledged at 03:16:16. Four MQTT packets arrived during the cycle, but the original decoder rejected all four. Polling reported open at 03:16:24. An independent cleanup stop was sent at 03:17:20 and acknowledged at 03:17:21; polling reported closed at 03:17:26. The original end-to-end MQTT cycle fixture failed, correctly identifying missing typed push observations. Physical water flow was not independently observed in this run. No other zones were commanded. The later corrected cycle is recorded below.

A stop-only capture identified the defect: the status object contains the timer record alongside a numeric `update.value` and a string-valued hub `state`. Deserializing every dictionary value as a timer record rejected the numeric flag and discarded the valid timer data too. The fix registers discovered timer datapoint names in a System.Text.Json contract and deserializes them into attributed timer models; unrelated metadata is skipped by the serializer. It introduces no JSON DOM, raw-token parser or public raw-payload API. Unknown timer addresses remain ignored, and malformed known timer records remain rejected.

After the fix, stop-only NUnit checks passed on **net10.0 at 03:23:25 UTC** and **net472 at 03:24:07 UTC**, receiving and decoding zone-1 closed-state feedback through the expected MQTT topic. Subsequent polls also reported closed. The messages reported last usage as 0 L; this is a cloud reading, not a measurement or confirmation of physical water flow.

Seven offline cases cover mixed metadata, invalid timer value types and non-default discovered RF addresses. The final offline suite has 378 passing cases across both frameworks. The Windows app is rebuilt with the same decoder correction.

The corrected decoder's live closed-state path is verified on both targets. A subsequent full .NET 10 cycle also passed, as recorded below; the corrected net472 checks remain stop-only. Original failure and subsequent passes remain in `artifacts/mqtt-zone1-net10`, `artifacts/mqtt-zone1-stop-diagnostic`, `artifacts/mqtt-zone1-stop-fixed-net10`, `artifacts/mqtt-zone1-stop-fixed-net472` and `artifacts/mqtt-zone1-fix-offline`. Bounded diagnostic frames stay beside the ignored private settings file; they are not test attachments or repository fixtures.


## Corrected full cycle — 24 September 2026

The explicitly selected NUnit test `Zone1LiveTests.OneMinuteZone1CycleReportsMqttTransitions` **passed on net10.0** using the corrected decoder. One 60-second start was sent. All times below are UTC.

| Time | Observation |
| --- | --- |
| 03:29:20 | Baseline polling reported zone 1 closed. |
| 03:29:22 | Observer connected; one 60-second zone-1 start sent and acknowledged. |
| 03:29:23 | MQTT reported zone 1 open, cloud timestamp 03:29:23.188. |
| 03:29:28 | A newer open MQTT reading arrived, timestamp 03:29:28.950. |
| 03:30:22 | MQTT reported zone 1 closed, timestamp 03:30:22.393, before the cleanup stop. |
| 03:30:27 | Independent cleanup stop sent and acknowledged. Monitor stopped cleanly. |
| 03:30:28 | NUnit reported the cycle passed. |

Three push updates were accepted and none rejected. The newer closed timestamp followed the open timestamp, satisfying the live test's ordering assertions. This verifies the full library monitor path from broker delivery through typed decoding and merged status events on .NET 10. The closed report arrived approximately one minute after the start request and before the explicit stop; it is evidence of cloud-reported automatic closure, not independent physical-flow timing. Last usage remained 0 L.

No other zones were commanded. The net472 corrected live evidence is the successful stop-only test; its full open/closed cycle was not rerun. Offline coverage still totals 378 passing cases across both targets. No production code changed during this follow-up.

Result: `artifacts/mqtt-zone1-fixed-cycle-net10/njc_SCOTTISHNEIL_2026-09-24_04_30_28_net10.0.trx`. The failed pre-fix cycle remains retained separately. Long-running connection/credential-rotation testing and independent app/physical-flow comparison remain separate checks.

## Session lifecycle update

The monitor can share its client with the optional [session recovery worker](SESSION-RECOVERY.md). Observer reconnection and cloud session renewal are distinct lifecycles; stop and await both before disposing the client. The Windows app manages both. One renewal path passed live on both targets, while prolonged MQTT operation across natural session expiry remains unverified. The offline totals above describe earlier slices; [current validation](../tests/README.md) is maintained separately.

## Portable TLS-purpose validation

The certificate callback requires the expected hostname, a valid chain ending at the exact pinned root, and server-authentication purpose. It now checks enhanced key usage explicitly for every certificate in the built chain, because older chain engines do not reliably enforce ApplicationPolicy. An absent EKU imposes no additional restriction; a present EKU must allow server authentication or any purpose. Expired, wrongly named, unrelated-root and client-authentication-only fixture certificates remain rejected.

The older [Mono chain implementation](https://github.com/mono/mono/blob/main/mcs/class/System/System.Security.Cryptography.X509Certificates/X509ChainImplMono.cs) recognizes only key-usage and basic-constraints critical extensions. A critical EKU can therefore remain unsupported on that runtime; the client fails closed and does not ignore InvalidExtension. Portable positive fixtures use a noncritical server-authentication EKU, with the same explicit purpose enforcement. No certificate is added to the machine trust store.

After the purpose check was added, the explicit read-only MQTT connection/poll fixture passed again on both net10.0 and net472 on 24 September 2026. Results are retained under `artifacts/completion-mqtt-net10` and `artifacts/completion-mqtt-net472`. No valve commands were sent.

## Home configuration changes

RainPointMonitor.ConfigurationChanged reports a typed home ID and configuration revision for command 04 notifications addressed to the signed-in account and monitored home. Duplicate or older revisions are suppressed. These notifications invalidate configuration; consumers reread discovery or schedules to obtain current names, plans and settings. They do not replace timer status, extend status freshness, or confirm a watering command. A login response without an account ID cannot establish the notification recipient, so configuration notifications are rejected. Subscribers should queue asynchronous refresh work and coalesce bursts.

This decoder follows the official app's home-change message handling. Offline tests cover routing, malformed frames, revision ordering, subscriber failures and shutdown. On 29 September 2026, a real app rename delivered command 04 to a separate shared account. The notification contained an empty description field between the home ID and revision. The decoder now accepts that field while retaining account/home checks and revision ordering; regression fixtures cover both the plain frame and attributed JSON envelope. A subsequent rename was reflected by the connected consumer without an explicit session restart or manual refresh. A disabled 08:00 five-minute zone-1 interval plan also appeared automatically with its disabled state intact. These checks verify shared-account name and plan invalidation followed by rereads; they do not establish delivery for every configuration edit.
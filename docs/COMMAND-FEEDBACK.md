# Watering-command feedback

`StartWateringAsync` and `StopWateringAsync` return `RainPointWateringCommandResult`. This development API previously returned the acknowledgement enum directly; consumers that inspect the return value should now read `.Outcome`.

```csharp
RainPointWateringCommandResult result = await client.StopWateringAsync(hub, timer.Address, 1, cancellationToken);
RainPointCommandOutcome acknowledgement = result.Outcome;
RainPointTimerStatus responseStatus = result.Status;
```

`Outcome` distinguishes accepted commands from already-requested/transitioning commands (service code 4). Other error codes still throw; authentication failures invalidate the matching session. There is no automatic command retry or extra status read.

`Status.Availability` distinguishes missing, decoded, unsupported and malformed optional feedback. Recognized `11#`/`01#` records retain their own zone identities, including fields for zones other than the requested zone. Missing fields remain null. The requested action never supplies a missing open/closed state. Unsupported legacy per-zone strings remain unsupported. No public property exposes the transport payload.

`ResponseTimestamp` is the optional server timestamp in Unix milliseconds, also accepted as a numeric string. Invalid or missing timestamps remain null without discarding valid status. It is distinct from receipt time and device data-change time; it does not populate `Status.LastDataChange` or drive monitored-state reconciliation.

The Windows control message shows the requested zone's response state, usage, configured duration and server time separately from monitored readings. This may be stale or partial and does not prove physical flow. Switching the control zone clears the previous command message. Monitored readings change only through normal feedback.

## Protocol evidence and validation

The pinned `funkadelic/ha-rainpoint` client accepts both an object containing `state` and a direct status string from `controlWorkMode`. RainPoint Home 1.19.1065's bundled timer control code reads `state` and `timestamp` for both codes 0 and 4; framed records are returned unchanged, while its legacy non-framed representation is port-specific. See [source inventory](PROTOCOL-SOURCES.md). These are reference-derived contracts, not proof of physical actuation.

An internal System.Text.Json converter handles only this object/string union; object fields remain explicitly attributed models. There is no JSON DOM or property-name traversal. Unexpected optional shapes do not conceal a valid acknowledgement or authentication rejection. Syntactically invalid JSON and invalid envelopes still fail.

Offline NUnit cases exercise all three zones, both outcomes, start/stop disagreement with reported state, partial records, supported and unsupported shapes, malformed fields, timestamps, authentication rejection and no replay. Dashboard and actual WPF binding tests keep command-response feedback separate from monitored state on both targets.

The explicit `CommandFeedbackLiveTests.StopAlreadyClosedZone1ReportsOptionalFeedback` test requires `RAINPOINT_LIVE_SETTINGS`. It reads a closed baseline, sends exactly one zone-1 stop, and records typed availability without opening any valve. A missing optional observation is legitimate and is reported as such; this test cannot prove physical movement or flow accuracy.

Validation on 24 September 2026: 726 library/dashboard and 50 WPF cases passed per target (1,552 offline passes). The updated actual-control binding checks passed for zones 1–3 on both Windows targets. Release build and formatting verification passed with no warnings or errors.

The explicit stop-only check passed sequentially on net10.0 and net472. Both returned Accepted with Decoded status and a server timestamp; all three zones reported closed, with last usage 4.7 L for zone 1 and 0 L for zones 2/3. These are returned readings, not independently measured volumes. Exactly one zone-1 stop was sent per framework after a closed baseline; no start or configuration write was sent. Evidence remains under ignored `artifacts/command-feedback-live`.
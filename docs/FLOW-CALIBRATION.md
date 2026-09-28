# Zone flow calibration

`GetTimerSchedulesAsync` includes `FlowCalibrationAvailability` and nullable `FlowCalibrationPercent`. For the supported HTV345FRF configuration, the percentage is a signed integer from -20 to +20. Zero is an explicit neutral setting; missing calibration remains null with `NotReported` availability. Incomplete, unsupported or out-of-range records are not replaced with invented defaults.

```csharp
var before = await client.GetTimerSchedulesAsync (hub, timer.Address, 1);
if (before.FlowCalibrationAvailability == TimerReadingAvailability.Decoded)
    await client.SetTimerFlowCalibrationAsync (hub, before, 1);
```

`SetTimerFlowCalibrationAsync` accepts only whole percentages within those bounds and requires a freshly read, decoded modern three-zone snapshot with firmware 120 or newer. It changes only byte 12 of the selected zone's settings field. Pressure compensation, unknown suffixes, sensor settings, durations, rain delay, plans, seasonal percentages and other zones are preserved. No valve command is sent.

The writer rechecks device identity, firmware, port count and the complete current configuration before submitting an update. Stale snapshots are rejected; a snapshot cannot be replayed after a write attempt, and uncertain writes are not retried. The service provides no atomic compare-and-swap guarantee, so a concurrent external change after the preflight read remains a limitation.

The library does not apply this percentage again to returned litres. This setting is distinct from a live flow-rate reading. Its physical effect, correction direction and measurement accuracy have not been established through a measured-volume experiment. The [Windows workbench](WINDOWS-APP.md#zone-settings) now reads calibration for all zones and edits zone 1, with offline validation on both frameworks.

## Contract and validation

The contract was traced from RainPoint Home 1.19.1065's RF settings model (module 643) and flow-calibration form (module 1209). The app stores a signed byte and offers -20..20 in one-percent steps. It recognizes the extension only when both calibration and pressure bytes exist. The adjacent pressure control is hidden in that form; this client preserves its byte without exposing an unverified setting. Vendor implementation code and private captures are not distributed in this repository.

On 24 September 2026, the explicit net10.0 NUnit fixture used the invited member account to set zone 1 temporarily to **1%**. Cloud read-back matched the expected full configuration, and the app's Zone 1 settings page displayed **1%** beside Flow Rate Calibration. The net472 restoration fixture then matched the complete original timer configuration exactly and removed its private recovery journal. No valve commands were issued and no plans were created.

Preparation requires no existing plans on any zone and proves that the original representation can be restored before it writes. Its private journal records the original and expected configurations. Cleanup accepts only those recorded states; an unexpected external change retains the journal for reconciliation. Always run cleanup after preparation, including an uncertain result. See [NUnit fixture instructions](../tests/README.md#flow-calibration).

The app comparison was a one-off BlueStacks inspection, not a workflow dependency. The app was signed out, its account field cleared and the shared Android reservation released.

Thirty new offline cases per framework cover signed values and limits, neutral versus absent calibration, malformed/unsupported records, firmware restrictions, exact unrelated-byte preservation, zone routing, stale snapshots, no-op writes and uncertain-write replay prevention. At completion of this library slice, coverage was **413 library/dashboard and 12 Windows cases per framework: 850 passes**. The [test README](../tests/README.md) records subsequent Windows UI coverage. An existing net472 observer-expiry test timed out during the first full regression run; its seven-case fixture and the entire 413-case net472 suite subsequently passed. The initial failure and subsequent results are retained under ignored `artifacts/flow-calibration-offline`; live results are under `artifacts/flow-calibration-live`. The transient timeout's cause is not established.

Remaining zone-profile and sensor-association features require separate contracts and validation; calibration setting support does not establish full vendor-app parity.
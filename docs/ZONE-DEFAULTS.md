# Zone watering defaults

`GetTimerSchedulesAsync` now includes `ZoneDefaultsAvailability` and a typed `ZoneDefaults` result for HTV345FRF. This reads the same saved configuration as schedules, seasonal adjustment and rain delay; it sends no valve command.

| Property | Saved meaning | App fallback for encoded zero |
| --- | --- | --- |
| `WateringDuration` | Default watering duration | 10 minutes |
| `MistingRunTime` | Misting on-time | 10 seconds |
| `MistingInterval` | Misting off-time | 30 seconds |

Each duration is nullable: null within a decoded `ZoneDefaults` means the explicit zero sentinel, not an invented saved duration. If the entire defaults record is missing, malformed or unsupported, `ZoneDefaults` itself is null and its availability explains why. These fallback values describe the inspected app; independent firmware fallback execution has not been tested.

```csharp
var before = await client.GetTimerSchedulesAsync (hub, timer.Address, 1);
await client.SetTimerDefaultWateringDurationAsync (hub, before, TimeSpan.FromMinutes (10));

// Every edit requires a newly read snapshot.
before = await client.GetTimerSchedulesAsync (hub, timer.Address, 1);
await client.SetTimerMistingDefaultsAsync (hub, before,
    TimeSpan.FromSeconds (10), TimeSpan.FromSeconds (30));
```

Default duration writes accept 1–720 whole minutes. Misting on/off defaults each accept 5–3600 whole seconds. A null argument explicitly writes that field's zero sentinel; `TimeSpan.Zero` is rejected. Existing non-minute whole-second watering defaults are retained faithfully on reads, but the app's current picker only writes whole minutes.

Writes require a decoded modern three-zone configuration and firmware 120 or newer. Before writing, the client reads fresh configuration and rejects mismatched device identity, firmware, port count or parameter content. A snapshot cannot be reused after a write attempt. Neither timeout nor uncertain transport failure triggers a command retry. The service has no atomic compare-and-swap guarantee: a change after the preflight read remains a cloud concurrency limitation.

Only bytes 0–1 of the zone settings field change for default duration; only bytes 2–5 change for misting defaults. The little-endian fields count seconds. Other zones, plans, sensor flags, rain delay, seasonal percentages, calibration and unknown suffixes are preserved exactly. This is saved configuration, not a start/stop operation or a modification to existing schedule timing. Callers still supply an explicit duration to `StartWateringAsync`; manual misting and cycle-and-soak use [separate explicit commands](MANUAL-MODES.md).

## Evidence and tests

The contract and picker bounds were traced from RainPoint Home 1.19.1065's RF timer bundle: settings model 643, duration picker 1162, settings form 1161 and misting form 1205. Vendor implementation code and private captures are not included in the repository.

On 24 September 2026, an explicit NUnit net10.0 fixture used the invited member account to temporarily set zone 1 to 11 minutes and 15/45-second misting defaults. The app's Zone 1 settings page showed exactly `11min` and `15sec,45sec`. A net472 restoration fixture restored the complete original timer configuration with exact equality and deleted the recovery journal. No valve command or saved plan was created. This demonstrates those member-account setting writes; it does not establish all membership permissions or physical use of the defaults.

The live fixture requires explicit opt-in, no existing plans on any zone and a restorable original duration. It records the original and intermediate configurations before writing. Cleanup accepts only its recorded states and retains the journal if external changes require reconciliation. The app inspection was a one-off BlueStacks comparison; it is not a testing workflow dependency. The app was signed out, its account field cleared and the shared Android reservation released.

Thirty-three new offline cases pass on each runtime. Coverage includes sentinel values, range/precision limits, exact unrelated-byte preservation, zone routing, malformed settings, firmware restrictions, stale snapshots, no-op writes and uncertain-write replay protection. The full suite passes **383 library/dashboard plus 12 Windows cases per framework: 790 passes**. See [NUnit execution instructions](../tests/README.md#zone-default-settings).

The [Windows workbench](WINDOWS-APP.md#zone-settings) now reads defaults for all zones and edits zone 1, with offline validation on both frameworks. [Flow calibration](FLOW-CALIBRATION.md) was subsequently implemented and verified against the app. Sensor association and additional zone-profile settings remain separate work.
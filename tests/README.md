# Offline tests

Latest verified full offline run (28 September 2026): **1,352 library/dashboard cases and 87 WPF cases on each target, 2,878 passes with zero failures/skips**. The release-preparation rerun also passed all 2,878 cases; its TRX files are under ignored `artifacts/release-tests`. Earlier completion results remain under `artifacts/completion-offline` and `artifacts/completion-wpf`. See [publication validation](../PUBLISHING.md#local-preparation-validation--28-september-2026) for package, workflow and documentation checks. The additions cover scene-history page translation and the vendor interval calendar across daylight-saving changes. Earlier results and failures remain retained; counts in dated sections below are historical. Live Android fixtures are excluded from offline runs.

The suite targets net472 and net10.0 with C# 14. Every HTTP response is supplied by an in-process `HttpMessageHandler`; it cannot fall through to the network. Decoder fixtures include explicitly synthetic three-zone frames and a family-layout example from the protocol references, not captures from the owner's timer.

| Dependency | Version | Purpose |
| --- | --- | --- |
| NUnit | 4.6.1 | Test framework |
| NUnit3TestAdapter | 6.3.0 | Visual Studio / VSTest discovery and execution |
| Microsoft.NET.Test.Sdk | 18.10.1 | Test host |
| NUnit.Analyzers | 4.15.0 | Compile-time test checks |
| Microsoft.NETFramework.ReferenceAssemblies | 1.0.3 | net472 reference assemblies, build only |
| System.Text.Json | 10.0.12 | Client serialization on net472; .NET 10 uses its built-in implementation |

Versions were checked against the stable NuGet feed on 23 September 2026.

Run from the solution root:

```powershell
dotnet test tests/RainPointClient.Tests -c Release -f net472 --filter "TestCategory!=Live"
dotnet test tests/RainPointClient.Tests -c Release -f net10.0 --filter "TestCategory!=Live"
dotnet format whitespace RainPointClient.slnx --verify-no-changes
```

Command-response coverage and the explicit zone-1 stop-only live test are documented in [COMMAND-FEEDBACK.md](../docs/COMMAND-FEEDBACK.md).

[Manual watering mode tests](../docs/MANUAL-MODES.md) cover the firmware gate, mode-specific units and Windows controls. The explicit misting live fixture opens zone 1 and requires the separate actuation opt-in.

Coverage includes authentication wire fields, discovery, exact start/stop requests, all three zones, malformed JSON/envelopes, API errors, session invalidation, throttle cooldown, cancellation, borrowed HTTP lifetime, duration/address/model validation, truncated binary records, extended headers, record-boundary collisions, duplicate data, absent data and unsupported formats. Raw JSON inspection is confined to tests that assert the wire contract; production uses typed serialization only.

The opt-in probe in `samples/RainPointClient.Probe` passed against a real RainPoint Home account on 23 September 2026 on both net472 and net10.0. It discovered an HWG023WBRF-V2 hub and HTV345FRF timer, decoded all three zones as closed, and reported RSSI -65 dBm and zero configured durations. These are cloud-reported readings, not confirmation of physical valve position or nonzero duration units. No watering commands were sent. The probe remains separate from automatic offline tests. Real valve tests require deliberate execution and post-test state checks.

Validation on 23 September 2026: **67 tests passed on net472 and 67 on net10.0**, with no skips. The complete solution built in Release with zero warnings and errors. The client dependency audit reported no known vulnerable packages.

Subsequent live actuation: the zone-1-only .NET 10 test was **inconclusive**, and is not counted among the 67 offline passes. One 10-second start and one cleanup stop were accepted; stale cloud readings/timeouts prevented final-state confirmation. The second start was skipped. See [the dated test record](../docs/ZONE1-LIVE-TEST.md).

Follow-up: the owner confirmed the app reports zone 1 closed. Cloud polling reliability and automatic-shutoff timing remain unverified; no additional watering run was performed.

Expanded validation: **80 offline tests passed per framework** after adding typed batch polling, hub-ID matching and ambiguity checks, documented normal-irrigation duration boundaries, and a sanitized HTV345FRF binary capture. Initial sub-minute live attempts were outside the documented normal-irrigation range and produced no water according to the owner, despite cloud state changes.

Final live follow-up: the owner visually confirmed zone 1 starts and automatically stops in the corrected .NET 10 60-second run, then reported on/off in the net472 explicit-stop test. The app and client both showed delayed open status after water stopped. All actuation remained restricted to zone 1; final physical state was off. Exact latency and zones 2/3 are not verified. See the full dated record for failed early attempts and successful later observations.


Water-usage follow-up: **83 offline tests pass per framework** after adding nullable decimal litres feedback for HTV345FRF. Added coverage checks the owner's 14-count / 1.4 L comparison, reported zero versus missing usage, invalid field width, and the full unsigned count range. The read-only probe now prints litres and the underlying count. See [protocol evidence and limits](../docs/PROTOCOL-SOURCES.md#htv345frf-water-usage-comparison).


## Hub/timer parity additions

117 offline NUnit cases pass per target (net472 and net10.0). New cases cover hub/timer signal separation, absent and duplicate connectivity records, metadata, firmware addressing and malformed responses, local packed timestamps, nullable battery/alarm feedback, time-broadcast request construction with unrelated-setting preservation, refresh-token rotation/expiry/rejection, and logout cleanup.

Read-only .NET 10 development probes on 23 September verified hub connectivity, Wi-Fi signal, firmware checks for the hub and timer, normal battery condition, local report time, zero alarm codes and retained zone 1 usage of 1.4 L. Refresh followed by authenticated discovery and logout also completed. These exploratory observations are not a completed NUnit hardware workflow. Repeatable live/emulator tests must use the established adapter workflow; see [the parity inventory](../docs/UPSTREAM-PARITY.md).

## Windows reference app (24 September 2026)

At the initial desktop-integration checkpoint, the main test project covered presentation logic: 130 cases per library target (117 library cases plus 13 dashboard cases). The separate RainPointClient.Desktop.Tests project has 12 offline Windows tests per target, including encrypted credential storage and startup sign-in, exercising signed-out startup and a simulated sign-in/discovery/status sequence through actual controls and bindings. Both projects use NUnit and NUnit3TestAdapter. Offscreen PNGs are test attachments; no real account or valve is contacted. See [Windows app usage and test dependencies](../docs/WINDOWS-APP.md). Run dotnet test RainPointClient.slnx -c Release on Windows to include both projects.

## MQTT feedback validation

Historical MQTT-slice result: **788 library/dashboard tests and 56 Windows tests per framework, 1,688 passes total** (24 September 2026). Run `dotnet test RainPointClient.slnx -c Release --filter "TestCategory!=Live"` for the offline suite. Windows credential tests require a normal user profile with DPAPI available; a restricted impersonated test host is insufficient.

MQTTnet 4.3.7.1207 is the added client dependency, shared by net472 and net10.0. Offline tests cover observer request/authentication, private-root certificate validation, no subscribe/publish, malformed or misrouted frames, stale/equal timestamp rejection, cancellation, expiry, cooldown and UI selection cleanup.

## Read-only MQTT live checks

`LiveMonitorTests` is an explicit, nonparallel NUnit fixture in the main test project, discoverable through NUnit3TestAdapter. Discovery and ordinary runs do not contact the cloud. Select the exact test in Test Explorer, or deliberately run each target sequentially:

```powershell
$env:RAINPOINT_LIVE_SETTINGS = (Resolve-Path .local/rainpoint.json).Path
dotnet test tests/RainPointClient.Tests -c Release -f net10.0 --filter "FullyQualifiedName=RainPointClient.Tests.LiveMonitorTests.ObserverConnectsAndPollsWithoutValveCommands" --logger trx --results-directory artifacts/mqtt-live-net10
dotnet test tests/RainPointClient.Tests -c Release -f net472 --filter "FullyQualifiedName=RainPointClient.Tests.LiveMonitorTests.ObserverConnectsAndPollsWithoutValveCommands" --logger trx --results-directory artifacts/mqtt-live-net472
Remove-Item Env:RAINPOINT_LIVE_SETTINGS
```

For Test Explorer, start Visual Studio with that environment variable set. The ignored settings file has the `email`, `password` and `areaCode` properties shown in the root README. The fixture requires exactly one matching HWG023WBRF/HTV345FRF hub on the account, makes one explicit login, then checks MQTT connection and decoded polling with bounded waits. Another login may displace the phone/desktop app. It never starts/stops watering or changes device settings. Cleanup stops the observer and attempts logout.

Both target checks passed on 24 September 2026. Zero push frames arrived from idle hardware; this proves connection and polling, not timer-change delivery. net472 also reconnected and its remote logout did not complete. Full evidence and remaining hardware checks are in [MQTT-FEEDBACK.md](../docs/MQTT-FEEDBACK.md). These desktop NUnit checks do not constitute an independent vendor-app comparison.


## Bounded single-zone MQTT actuation checks

`Zone1LiveTests` contains explicit, nonparallel NUnit tests in categories `Live` and `Actuation`. They do not run during discovery or offline suites. The same private settings variable is required. These tests require exactly one matching hub and timer; never run target frameworks concurrently against the account.

- `OneMinuteZone1CycleReportsMqttTransitions` additionally requires `RAINPOINT_LIVE_ZONE1=60`. It requires a connected observer and closed baseline, sends exactly one 60-second zone-1 start, sends an independent cleanup stop around 65 seconds after the start request, and allows up to 90 more seconds for delayed MQTT closure. Cleanup still runs after start uncertainty or an assertion failure. It never replays a start. Select this test only when that physical watering cycle is intended.
- `OneMinuteZone2ReportsMqttTransitions` requires `RAINPOINT_LIVE_ZONE=zone2-60`; `OneMinuteZone3ReportsMqttTransitions` requires `RAINPOINT_LIVE_ZONE=zone3-60`. Each uses the same one-minute normal-watering guard and cleanup as zone 1, with exactly one selected zone. Final reads require that zone idle and the other zones' reported modes unchanged. The owner authorized short runs on all three zones on 27 September 2026. Run sequentially with at least five minutes between watering runs; never infer physical flow from command acceptance or device reports alone. See [all-zone evidence](../docs/ALL-ZONE-LIVE-TEST.md).
- `StopOnlyObservesZone1Feedback` requires `RAINPOINT_LIVE_ZONE1=stop`. It only sends a zone-1 stop and checks typed closed-state MQTT feedback and a closed poll. It saves at most 20 packets of at most 8192 bytes each into a uniquely named diagnostic file beside the ignored private settings file. These contain protocol/device data; keep them private and do not attach them to published results. No token/password is printed.

For example, after setting the private settings path, deliberately select the stop-only test:

```powershell
$env:RAINPOINT_LIVE_ZONE1 = 'stop'
dotnet test tests/RainPointClient.Tests -c Release -f net10.0 --filter "FullyQualifiedName=RainPointClient.Tests.Zone1LiveTests.StopOnlyObservesZone1Feedback" --logger trx
Remove-Item Env:RAINPOINT_LIVE_ZONE1
```

The first 60-second cycle exposed mixed numeric metadata and failed its MQTT assertions; cleanup was acknowledged and polling reported closed. After correction, stop-only verification passed on both frameworks. The subsequent corrected .NET 10 cycle also passed with one 60-second start, three accepted MQTT updates and an acknowledged cleanup stop. Seven offline regression cases preserve the fix. See [dated MQTT evidence](../docs/MQTT-FEEDBACK.md#zone-1-follow-up-and-metadata-correction). The corrected complete monitor path is now live-verified for open/closed feedback on .NET 10; net472 has stop-only live verification. Physical flow was not independently observed. The full-cycle result is retained under `artifacts/mqtt-zone1-fixed-cycle-net10`.

## Android vendor-app inspection

`RainPointClient.Android.Tests` is a standalone net10.0 NUnit project, using the same NUnit, NUnit3TestAdapter, Test SDK and analyzer versions listed above. It additionally uses `CrestronHomeNUnit.TestAdapter` 1.12.1 for the established Android device, hierarchy and shared-session reservation helpers. This tooling target does not change the library's net472/net10.0 targets.

`AppInspectionTests.InspectReviewedPage` is explicit, nonparallel and categorized `Android` and `Live`. Offline runs and discovery do not operate the emulator. Supply `RAINPOINT_ANDROID_PLAN` with an ignored private JSON file containing `adbExecutable`, `serial`, `package`, `lockPath`, `evidenceDirectory` and `steps`. Use the actual shared Android reservation path configured for the environment, never a separate per-project lock. The evidence directory must already exist.

Each step has `action` and optional `waitSeconds` (maximum effective wait ten seconds). Supported actions are `capture`, `open-store` (the official RainPoint Home Play listing), `tap` and `back`. Input requires nonempty `requireText` containing exact labels from the freshly inspected page; taps also require `selectorKind` (`Text`, `ResourceId` or `ContentDescription`) and `selector`. Review each navigation step against captured UI before executing it. Do not select watering, save, delete, reset, pairing or update controls during a read-only audit.

```powershell
$env:RAINPOINT_ANDROID_PLAN = (Resolve-Path .local/android-audit/plan.json).Path
dotnet test tests/RainPointClient.Android.Tests -c Release --filter "FullyQualifiedName=RainPointClient.Android.Tests.AppInspectionTests.InspectReviewedPage" --logger trx --results-directory artifacts/android-audit
Remove-Item Env:RAINPOINT_ANDROID_PLAN
```

Captures remain in the private evidence directory and are not published as test attachments. Password fields in hierarchy XML are masked by the helper; screenshots can still contain private information. Navigation remains on the final page for the next reviewed step. A successful run releases the reservation; failure intentionally retains it for reconciliation. Do not clear an existing reservation without establishing ownership and resolving the uncertain operation. Inputs are never automatically retried.

Google Play installation of RainPoint Home 1.19.1065 completed on 24 September 2026. The app then crashed before its first screen because its native loader could not locate libreactnative.so in this emulator configuration. A capture and a package diagnostic both passed with the emulator minimized. These passes establish the inspection tooling, not a successful app launch or app/client parity. See [audit status and capability gaps](../docs/ANDROID-APP-AUDIT.md).


The read-only `package-diagnostics` action is restricted to the RainPoint Home package. It saves installed-package metadata, up to 200 crash-buffer lines and copies of the installed APK splits to the private evidence directory. It does not copy application data, install a package, launch the app or clear logs. These files are private diagnostics, not test attachments; the crash buffer can include other Android processes.

The compatibility investigation subsequently launched the exact current signed APK splits in the existing BlueStacks Android 9 instance. After the owner approved the agreement and explicitly authorized one-off evaluation, a signed-in, read-only feature inventory was recorded and the temporary app session was signed out. This is exploratory UI evidence, not a new NUnit regression pass. BlueStacks is not a project dependency, supported test target or workflow requirement; do not add its launcher, ports, account handling or navigation to repository workflows. Repeatable emulator validation remains directed at Android Studio, where the current app startup issue is unresolved. The older 1.14.1047 build demanded an upgrade and cannot establish current app parity. See [the observed capability inventory and remaining checks](../docs/ANDROID-APP-AUDIT.md).

Two explicit setup operations support this recorded comparison: `install-reviewed-1047` requires `RAINPOINT_ANDROID_APK` and `RAINPOINT_ANDROID_REPLACE_SIGNED_OUT=1`, verifies the pinned older APK hash, then uninstalls only RainPoint and installs that APK. This clears RainPoint app data and must only be selected for a confirmed never-signed-in test installation. `install-reviewed-1065` requires `RAINPOINT_ANDROID_APK_DIRECTORY` and verifies the pinned hashes of the four publisher-verified Google Play splits before installing them with replacement enabled; it does not clear app data. Both operations require the RainPoint package. `open-app` launches that package's verified main activity. These are opt-in emulator setup operations, not library or device commands. An app launch request alone does not assert a successful destination screen; inspect the private after-capture.

## Read-only schedule checks

The 34 schedule cases in `ScheduleTests` run offline on both frameworks. See [the schedule contract and evidence](../docs/SCHEDULES.md) for fixture scope and live limitations. `ScheduleLiveTests` is explicit and nonparallel, uses the existing private account settings, reads all three zones, and sends no configuration or valve commands. Its configured runs passed on net472 and net10.0 with zero saved plans in each zone.

Run deliberately and sequentially through NUnit3TestAdapter:

```powershell
$env:RAINPOINT_LIVE_SETTINGS = (Resolve-Path .local/rainpoint.json).Path
dotnet test tests/RainPointClient.Tests -c Release -f net10.0 --filter "FullyQualifiedName=RainPointClient.Tests.ScheduleLiveTests.ReadsSavedSchedulesWithoutChangingConfiguration" --logger trx --results-directory artifacts/schedule-live-net10
dotnet test tests/RainPointClient.Tests -c Release -f net472 --filter "FullyQualifiedName=RainPointClient.Tests.ScheduleLiveTests.ReadsSavedSchedulesWithoutChangingConfiguration" --logger trx --results-directory artifacts/schedule-live-net472
```

## Disabled schedule comparison fixtures

`ScheduleWriteTests`, `CycleAndSoakScheduleTests` and `MistingScheduleTests` contain 71 offline write cases per framework. `ScheduleWriteLiveTests` contains four explicit, nonparallel NUnit tests in the `Live` and `Configuration` categories; ordinary test runs do not execute them. They use the existing private account file plus `RAINPOINT_LIVE_SCHEDULE=disabled-zone1`. BlueStacks is not invoked or required by these tests.

Preparation requires an empty zone-1 plan list and no outstanding recovery journal. Choose normal preparation (60 seconds), `PrepareDisabledZone1CyclePlanForAppComparison` (10 minutes total watering, five-minute cycles, 30-minute pauses), or `PrepareDisabledZone1MistingPlanForAppComparison` (10-minute configured duration, 10-second bursts, 20-second pauses). All save one disabled zone-1 plan at 23:57, daily, effective 31 December 2083, and a private journal beside the account file before the write. It intentionally leaves the disabled plan for a one-off app comparison. **Always follow preparation with cleanup**, including after an uncertain result. Cleanup checks exact device identity and configuration, removes only the known fixture plan, verifies complete original configuration restoration, and then removes the journal. A mismatched configuration stops cleanup for reconciliation; it is never blindly overwritten. None of these tests sends valve commands or enables the plan.

Both normal and cycle-and-soak preparation passed on net10.0; populated-plan reads and cleanup passed on net472 on 24 September 2026. Cycle-and-soak cleanup initially timed out; a fresh journal-based reconciliation passed and verified exact restoration. Misting preparation also passed on net10.0, followed by populated read and cleanup on net472, without retry. All three app comparisons matched the configured timing fields, date, recurrence and disabled switch. Every comparison ended with exact restoration and removal of its recovery journal. See [schedule evidence and remaining limits](../docs/SCHEDULES.md).

```powershell
$env:RAINPOINT_LIVE_SETTINGS = (Resolve-Path .local/rainpoint.json).Path
$env:RAINPOINT_LIVE_SCHEDULE = 'disabled-zone1'
dotnet test tests/RainPointClient.Tests -c Release -f net10.0 --filter "FullyQualifiedName=RainPointClient.Tests.ScheduleWriteLiveTests.PrepareDisabledZone1PlanForAppComparison" --logger trx --results-directory artifacts/schedule-write-prepare-net10
# To compare another mode, select PrepareDisabledZone1CyclePlanForAppComparison
# or PrepareDisabledZone1MistingPlanForAppComparison above.
# Run only one preparation before cleanup.
# Perform the one-off app comparison, without saving or enabling the plan.
dotnet test tests/RainPointClient.Tests -c Release -f net472 --filter "FullyQualifiedName=RainPointClient.Tests.ScheduleWriteLiveTests.RemoveDisabledZone1ComparisonPlan" --logger trx --results-directory artifacts/schedule-write-cleanup-net472
```

Sequential use is required: another sign-in with the same account can invalidate the previous session. The app displayed its signed-in-elsewhere message when the cleanup fixture authenticated. Later invitation acceptance and separate-account concurrent reads are recorded in [history validation](../docs/HISTORY.md).

## Seasonal adjustment and rain-delay comparison

`TimerPlanSettingsTests` adds 28 offline cases per framework. `TimerPlanSettingsLiveTests` has two explicit nonparallel `Live`/`Configuration` tests. Preparation requires readable settings, no saved plans in any zone, `RAINPOINT_LIVE_SETTINGS_EDIT=zone1`, a private account file, and an explicit home-local expiry in `RAINPOINT_LIVE_RAIN_END` (`yyyy-MM-dd HH:mm:ss`). It changes zone 1 to twelve 90% values and the supplied bounded rain delay, with no valve commands.

It writes a private `.settings-comparison.json` journal beside the account settings before changing anything. The journal records the original and both known intermediate configurations. **Always run `RestoreZone1Settings` after preparation, including after an uncertain result.** Restoration first reads the current configuration, refuses unexpected changes, restores rain delay and percentages through typed APIs, then verifies exact original configuration before removing the journal. An incomplete restore keeps the journal for reconciliation. Never delete it merely to permit another preparation.

```powershell
$env:RAINPOINT_LIVE_SETTINGS = (Resolve-Path .local/rainpoint.json).Path
$env:RAINPOINT_LIVE_SETTINGS_EDIT = 'zone1'
# Set RAINPOINT_LIVE_RAIN_END to the deliberately chosen home-local expiry before preparation.
dotnet test tests/RainPointClient.Tests -c Release -f net10.0 --filter "FullyQualifiedName=RainPointClient.Tests.TimerPlanSettingsLiveTests.PrepareZone1SettingsForAppComparison" --logger trx
# Inspect the app without saving any changes.
dotnet test tests/RainPointClient.Tests -c Release -f net472 --filter "FullyQualifiedName=RainPointClient.Tests.TimerPlanSettingsLiveTests.RestoreZone1Settings" --logger trx
```

Run sequentially because another login can displace the app. These fixtures do not launch or depend on BlueStacks. See [settings evidence](../docs/PLAN-SETTINGS.md).

On 24 September 2026, read-only baseline and preparation passed on net10.0, the app matched all twelve 90% values and the expiry, and restoration passed on net472 with exact original-configuration equality. No recovery journal or altered settings remain. Full offline regression: 644 passes across both frameworks. Actual scheduled suppression/resumption and adjusted watering durations remain separate validation work.

## Usage and event history

`HistoryTests` adds 40 offline cases per framework for attributed history contracts, date/zone limits, units, missing data, event filtering/details, timestamp semantics and cancellation. `HistoryLiveTests.ReadsUsageAndEventHistoryWithoutChangingConfiguration` is explicit, read-only and nonparallel. It discovers one matching hub/timer, reads zone-1 daily/monthly history, reads a two-record watering-event page and an older page, and logs out. It prints no account identifiers, operator details, credentials or response bodies.

```powershell
$env:RAINPOINT_LIVE_SETTINGS = '<private-account-settings-file>'
dotnet test tests/RainPointClient.Tests -c Release -f net472 --filter 'FullyQualifiedName~HistoryLiveTests.ReadsUsageAndEventHistoryWithoutChangingConfiguration'
dotnet test tests/RainPointClient.Tests -c Release -f net10.0 --filter 'FullyQualifiedName~HistoryLiveTests.ReadsUsageAndEventHistoryWithoutChangingConfiguration'
```

Run the live targets sequentially: two logins to the same account can displace each other. Both runtime checks passed on 24 September 2026. A member-account check also passed after accepting its invitation, and owner-client/member-app concurrent reads worked. Results are recorded under ignored `artifacts/history-live`; full offline results under `artifacts/history-offline`. [History evidence and limits](../docs/HISTORY.md) distinguish the repeatable NUnit checks from the one-off Android app comparison.

## Zone default settings

`ZoneDefaultsTests` adds 33 offline cases per target. `ZoneDefaultsLiveTests` uses explicit NUnit tests and the adapter for preparation and restoration. Preparation writes a private recovery journal before changing zone 1, requires empty schedules on all zones and checks read-back. Restoration refuses to overwrite an unrecognized external change and deletes the journal only after complete original-configuration equality.

```powershell
$env:RAINPOINT_LIVE_SETTINGS = '<private-account-settings-file>'
$env:RAINPOINT_LIVE_DEFAULTS_EDIT = 'zone1'
dotnet test tests/RainPointClient.Tests -c Release -f net10.0 --filter 'FullyQualifiedName~ZoneDefaultsLiveTests.PrepareZone1DefaultsForAppComparison'
# Compare the app's zone settings; no watering is needed. Always finish with restoration.
dotnet test tests/RainPointClient.Tests -c Release -f net472 --filter 'FullyQualifiedName~ZoneDefaultsLiveTests.RestoreZone1Defaults'
```

Use the same private account file for both phases. If preparation fails after writing, retain its journal and deliberately run restoration; do not repeat preparation. Preparation and app comparison passed on 24 September 2026, followed by exact net472 restoration. Results are under ignored `artifacts/zone-defaults-live`; offline results under `artifacts/zone-defaults-offline`. No recovery journal or changed test defaults remain. See [zone-default semantics and limits](../docs/ZONE-DEFAULTS.md).

## Flow calibration

`FlowCalibrationTests` adds 30 offline cases per target. `FlowCalibrationLiveTests` provides explicit, nonparallel NUnit preparation/restoration tests, discovered by NUnit3TestAdapter. Preparation requires empty schedules on every zone and `RAINPOINT_LIVE_CALIBRATION_EDIT=zone1`. It chooses 1% (2% if already 1%), proves exact reversibility, saves a private `.calibration-comparison.json` recovery journal beside the account file, writes only zone 1's calibration and verifies read-back. No valve command is sent.

```powershell
$env:RAINPOINT_LIVE_SETTINGS = '<private-account-settings-file>'
$env:RAINPOINT_LIVE_CALIBRATION_EDIT = 'zone1'
dotnet test tests/RainPointClient.Tests -c Release -f net10.0 --filter 'FullyQualifiedName=RainPointClient.Tests.FlowCalibrationLiveTests.PrepareZone1CalibrationForAppComparison'
# Compare the app's zone-1 settings without saving anything.
dotnet test tests/RainPointClient.Tests -c Release -f net472 --filter 'FullyQualifiedName=RainPointClient.Tests.FlowCalibrationLiveTests.RestoreZone1Calibration'
```

Use the same private account file in both phases and always run restoration after preparation, even if the outcome is uncertain. Cleanup refuses unexpected configuration changes and deletes the journal only after exact original-configuration equality. Do not delete a journal to allow another preparation. Same-account logins can displace the app, so run phases sequentially. These fixtures neither launch nor depend on BlueStacks.

On 24 September 2026, net10.0 preparation, app comparison at 1%, and exact net472 restoration passed. The journal was removed. Live results are under ignored `artifacts/flow-calibration-live`. Offline results are under `artifacts/flow-calibration-offline`: the first net472 full run had one observer-expiry timeout; its seven-case fixture and complete 413-case suite passed on recheck. Other targets passed initially, giving 850 offline passes at that checkpoint. The original failure remains recorded and its cause is unresolved. See [calibration semantics and limits](../docs/FLOW-CALIBRATION.md).

## Windows zone-settings editor

`DashboardSettingsTests` now has 41 offline cases on net472 and net10.0. `SettingsWindowTests` has eleven actual WPF control/binding cases on net472 and net10.0-windows. Both use NUnit and NUnit3TestAdapter with simulated HTTP responses; they never load private account files, start the emulator or command live hardware.

Coverage includes default-duration/misting/calibration input limits, explicit app defaults versus missing records, reads and writes in all three zones, exact preservation through the client, fresh read-back, stale snapshots, uncertain writes without retry, failed or mismatched read-back, selection changes, cancellation and field/button bindings. The WPF tests attach rendered settings-page images for each editor.

```powershell
dotnet test tests/RainPointClient.Tests -c Release --filter 'FullyQualifiedName~DashboardSettingsTests'
dotnet test tests/RainPointClient.Desktop.Tests -c Release --filter 'FullyQualifiedName~SettingsWindowTests'
dotnet test RainPointClient.slnx -c Release --filter 'TestCategory!=Live'
```

On 24 September 2026 the full offline suite passed: 442 library/dashboard and 17 Windows cases on each applicable framework, 918 total. Results are retained under ignored `artifacts/desktop-settings-offline`. This verifies the Windows presentation and typed-client integration, not another live hardware run. See [Windows usage and remaining features](../docs/WINDOWS-APP.md).

## Windows history screens

`DashboardHistoryTests` adds 31 offline cases per library framework. `HistoryWindowTests` adds three actual WPF control/binding cases per Windows framework, with rendered screenshot attachments. They use NUnit and NUnit3TestAdapter, simulated responses and an injected dashboard without access to saved real credentials. No emulator, live account, configuration writes or watering commands are involved.

Coverage includes daily/monthly range validation, home-calendar dates, litres and sparse/unknown/zero readings, event type and UTC filters, separate cloud/device times, unknown event codes and details, duplicate removal, explicit older-page requests, timestamp ties/no-progress detection, the 500-event limit, failed older reads, filter/device resets, cancellation, actual grid/input bindings, readable column widths and preserving selection when loading another page.

```powershell
dotnet test tests/RainPointClient.Tests -c Release --filter 'FullyQualifiedName~DashboardHistoryTests'
dotnet test tests/RainPointClient.Desktop.Tests -c Release --filter 'FullyQualifiedName~HistoryWindowTests'
dotnet test RainPointClient.slnx -c Release --filter 'TestCategory!=Live'
```

On 24 September 2026, the full suite passed 473 library/dashboard plus 20 Windows cases on each applicable framework (986 total). Final layout and selection-preservation checks also passed on both Windows targets. Results and screenshots are retained under ignored `artifacts/desktop-history-offline`. See [Windows history usage and limits](../docs/WINDOWS-APP.md#usage-and-event-history).

## Windows saved plans and all-zone controls

`DashboardPlanTests` provides 93 offline cases per library framework. All three plan modes and all six supported recurrence types are exercised in each zone. Replacement, enable/disable and deletion verify the complete selected-zone result and preserve other zones. Coverage also includes disabled defaults, six-plan limits, optional litre limits, date/interval/weekday validation, cycle spacing, unsupported existing plans, stale snapshots, uncertain outcomes, read-back failure and cancellation.

`PlanWindowTests` provides twelve actual WPF cases per Windows framework, including create/replace/enable/delete for all three modes in every zone, selection reset and unsupported hourly timing. Screenshot attachments cover every mode and zone. The settings editor also runs all three setting types in every zone. Manual control tests send simulated start/stop requests for each zone and verify correct addressing, disarming on zone changes and stop availability with an invalid start duration.

```powershell
dotnet test tests/RainPointClient.Tests -c Release --filter 'FullyQualifiedName~DashboardPlanTests'
dotnet test tests/RainPointClient.Desktop.Tests -c Release --filter 'FullyQualifiedName~PlanWindowTests'
dotnet test RainPointClient.slnx -c Release --filter 'TestCategory!=Live'
```

On 24 September 2026 the full suite passed **585 library/dashboard and 40 Windows cases per applicable framework, 1,250 passes total**, without skips. Results and screenshots are retained under ignored `artifacts/desktop-plans-offline`. These NUnit/adapter tests use simulated responses and injected dashboards without real credential access. No account, emulator, saved hardware configuration or valve was used. All three zones are product capabilities; physical valve-opening tests were restricted to zone 1 at that time. The owner expanded authorization to short sequential runs on all three zones on 27 September; current constraints are recorded in the [completion ledger](../docs/TODO.md). See [Windows plan usage](../docs/WINDOWS-APP.md#saved-plans).

## Completion checks (24 September 2026)

Offline additions cover seasonal/rain-delay editors in every zone, hub tools, product catalog, bounded session recovery and whole-timer power events returned in a zone query. TLS fixtures contain only public certificates with discarded generation keys; tests no longer require runtime certificate-generation APIs. The trusted, expired, unrelated-root and hostname-mismatch assertions remain active; client-authentication-only certificates are also rejected. A noncritical server-purpose extension avoids the older Mono engine's critical-EKU limitation without ignoring validation errors. Public fixture validity ends in 2120 and the expired leaf ended in 2021.

`CompletionLiveTests.ReadsAllZonesCatalogAndRenewsWithoutDeviceWrites` is explicit, nonparallel and categorized Live. It requires exactly one supported hub/timer on the selected account, reads catalog/firmware and all zones' schedules/settings/usage/event history, triggers one refresh through the recovery worker with an injected near-expiry clock, then verifies authenticated discovery. It sends no device writes or valve commands. This is not natural-expiry endurance, scheduled execution or password-relogin evidence.

```powershell
$env:RAINPOINT_LIVE_SETTINGS = (Resolve-Path .local/rainpoint-test.json).Path
dotnet test tests/RainPointClient.Tests -c Release -f net10.0 --filter "FullyQualifiedName=RainPointClient.Tests.CompletionLiveTests.ReadsAllZonesCatalogAndRenewsWithoutDeviceWrites" --logger trx --results-directory artifacts/completion-live-net10
dotnet test tests/RainPointClient.Tests -c Release -f net472 --filter "FullyQualifiedName=RainPointClient.Tests.CompletionLiveTests.ReadsAllZonesCatalogAndRenewsWithoutDeviceWrites" --logger trx --results-directory artifacts/completion-live-net472
Remove-Item Env:RAINPOINT_LIVE_SETTINGS
```

Both targets passed on 24 September. Earlier timeouts and the history protocol failure remain retained. Run the targets sequentially to avoid account-session displacement. The full offline results and WPF screenshots are retained under `artifacts/completion-offline` and the test output directories. The Android fixture contains explicit live tests only and is excluded by `TestCategory!=Live`.


## Calendar coverage (24 September 2026)

The calendar slice adds 30 pure calculation cases, seven dashboard lifecycle cases and three actual WPF cases per applicable target. All pass. Full offline results are under `artifacts/calendar-offline`; rendered all-zone calendar screenshots accompany Windows results. The same NUnit/TestAdapter workflow runs the expanded fixtures. `CompletionLiveTests` additionally projects a 31-date calendar from each zone's saved snapshot, with no plan writes, enabling or valve commands. Use the existing exact fixture filter with results directories `artifacts/calendar-live-net10` and `artifacts/calendar-live-net472`. Populated official-app comparison and scheduled execution are not implied by an empty live projection.

For the focused read-only calendar check, select `CalendarLiveTests.ProjectsAllZonesWithoutChangingPlans`. It uses the same private settings variable and requires exactly one matching hub/timer. Both targets passed with empty saved-plan lists in all three zones. Unlike the broad completion fixture, it does not require product-catalog, firmware or history endpoints:

```powershell
$env:RAINPOINT_LIVE_SETTINGS = (Resolve-Path .local/rainpoint-test.json).Path
dotnet test tests/RainPointClient.Tests -c Release -f net10.0 --filter "FullyQualifiedName=RainPointClient.Tests.CalendarLiveTests.ProjectsAllZonesWithoutChangingPlans" --logger trx --results-directory artifacts/calendar-only-live-net10
dotnet test tests/RainPointClient.Tests -c Release -f net472 --filter "FullyQualifiedName=RainPointClient.Tests.CalendarLiveTests.ProjectsAllZonesWithoutChangingPlans" --logger trx --results-directory artifacts/calendar-only-live-net472
Remove-Item Env:RAINPOINT_LIVE_SETTINGS
```

Run sequentially to avoid account-session displacement. The earlier broad attempts timed out before calendar checks at the catalog request; their artifacts remain under `artifacts/calendar-live-net10` and `artifacts/calendar-live-net472`.

## Watering-command response feedback

The command-feedback slice adds 34 client protocol cases and five dashboard cases, bringing the portable total to 726 per framework. Actual WPF control checks additionally verify response text for zones 1–3 while retaining monitored readings. Offline results are in `artifacts/command-feedback-offline` and the updated binding checks in `artifacts/command-feedback-bindings`.

`CommandFeedbackLiveTests.StopAlreadyClosedZone1ReportsOptionalFeedback` passed sequentially on net10.0 and net472. This explicit fixture sends a single zone-1 stop after checking a closed baseline, never a start. Both responses decoded all zones as closed, zone-1 last usage 4.7 L, zones 2/3 0 L. It does not prove physical valve movement or volume accuracy. See [command-feedback semantics and validation](../docs/COMMAND-FEEDBACK.md). To run deliberately with the existing private `RAINPOINT_LIVE_SETTINGS`, select the exact method through NUnit/TestAdapter; do not treat this stop-only test as read-only.

## Manual modes and work-state decoding

Current coverage includes 54 new protocol/decoder cases and eight dashboard cases, plus six additional WPF mode/zone combinations. At the manual-mode checkpoint, the full suite passed 788 portable and 56 Windows cases per applicable target (1,688 total). Evidence is retained under `artifacts/manual-modes-corrected`; initial results remain in `artifacts/manual-modes-offline`. The Release build and editorconfig verification pass.

The first live misting run exposed mode-2 feedback being misread as closed. That failed result is retained, including its accepted cleanup and final closed cloud state. Typed work-mode decoding now uses the complete low nibble, keeps unrecognized modes unknown, and displays cyclic activity separately. See [manual-mode evidence](../docs/MANUAL-MODES.md). Live zone-1 repeats must be short, limited in number and separated by intervals; this session uses at least five minutes after the preceding stop. Physical burst timing is not established by decoded cloud work mode.

Corrected misting live validation passed on net472 after more than six minutes between the preceding cleanup stop and the repeat start. Fresh MQTT activity and later idle, accepted cleanup stop and final closed cloud state were observed; physical water delivery remains unconfirmed (reported usage 0 L). See the [full evidence and retained initial failure](../docs/MANUAL-MODES.md). No additional live watering was run in this batch.

## Saved-plan volume limits

At the volume-plan checkpoint, the offline suite passed **890 portable plus 65 WPF cases on each target (1,910 total)**. This includes exact volume encoding, app write boundaries, full-range decoding, seasonal/recurrence guards, every zone/mode/recurrence combination with and without volume, replacement and clearing, and WPF controls. Portable evidence: `artifacts/volume-plans-corrected`; WPF results and screenshots: `artifacts/volume-plans-offline`. Three initial seasonal fixtures lacked firmware metadata; the corrected fixtures pass without relaxing the firmware guard.

`ScheduleWriteLiveTests.DisabledVolumePlanRoundTripRestoresOriginalConfiguration` is an explicit configuration test, not part of the offline suite. Set the existing private `RAINPOINT_LIVE_SETTINGS` and `RAINPOINT_LIVE_SCHEDULE=disabled-zone1`, then select that exact method through NUnit/TestAdapter. It uses one session to create, read and remove a disabled 1.4 L plan in each mode, restoring the complete original configuration after each. It requires an empty zone-1 plan list, refuses an existing recovery journal, never sends a watering command and retains the journal on uncertain cleanup. Run frameworks sequentially, with a gap between logins; rapid re-login was rejected with API code 9993. The independent `RemoveDisabledZone1ComparisonPlan` remains available for reconciliation.

The explicit disabled-volume round trip passed sequentially on net10.0 and net472, covering normal irrigation, cycle-and-soak and misting with a 1.4 L limit. Each plan was disabled, used the remote future date 31 December 2083, and was removed after read-back. Complete original configuration was restored after each mode; the private recovery journal is absent. No valve command was sent, and zones 2/3 settings were preserved. Results are in `artifacts/volume-plans-session-live`.

The initial fixture used separate preparation and cleanup logins. Normal-plan storage succeeded but immediate re-login failed; the independent net472 recovery fixture restored the original configuration. A follow-up isolated the rapid-login rejection as API 9993. The final fixture uses one session for all modes and cleanup, with an independent cleanup timeout and no write replay. Initial failed results remain in `artifacts/volume-plans-live` and `artifacts/volume-plans-corrected-live`, with recovery evidence in `artifacts/volume-plans-cleanup`. These tests prove cloud storage and restoration, not populated vendor-app display, RF delivery, scheduled execution or physical volume cutoff.

## RF channel

The RF-channel checkpoint passed 914 portable plus 67 WPF cases per target (1,962 passes). `artifacts/rf-channel-offline` contains results and screenshots. The explicit idle/no-plan RF change-and-restoration fixture passed on both desktop targets; see [RF test controls, recovery and limits](../docs/RF-CHANNEL.md). No watering was performed.

## Zone profiles and recommendations

See [the API, Windows behavior and live opt-in](../docs/ZONE-PROFILES.md). The profile checkpoint passed 965 library/dashboard and 70 Windows cases on each applicable target (2,070 total). Live profile reads and reversible zone-1 preference checks pass on both targets; no watering is performed. `ScriptedHandlerTests` additionally guards against concurrent fixture requests exchanging queued responses.

## Sensor settings and alarm flags

The sensor checkpoint passed 1,041 portable and 73 WPF cases per applicable framework (2,228 total). Sensor/threshold and alarm tests cover all zones with simulated responses. The explicit `SoilSensorLiveTests.ReadsAllZonesWithoutWrites` uses private `RAINPOINT_LIVE_SETTINGS` and only reads. Real sensor actuation and fault generation are not implied. See [contracts and limits](../docs/SENSORS-AND-ALARMS.md).

## Automatic low-moisture rules

`MoistureRuleTests`, `DashboardMoistureRuleTests` and `MoistureRuleWindowTests` cover the separate sensor-triggered rule, including all zones and disabled Windows drafts. The moisture-rule checkpoint passed 1,084 portable and 76 WPF cases per framework (2,320 passes). No automatic rule was written or enabled on live hardware. See [rule semantics](../docs/MOISTURE-WATERING.md).

## Administration, weather and scene tests

`AdministrationTests`, `DeviceAdministrationTests`, `HomeOptionsTests`, `TimeZoneTests`, `WeatherTests` and `SceneTests` cover typed contracts, unknown preferences, scope, stale/session checks, concurrency and no replay. Dashboard and actual WPF tests exercise the new tabs, including weekday and one-time scene inputs. All fixtures use simulated responses and fictitious credentials.

`AdministrationLiveTests.ReadsHomeMembersAndInvitationsWithoutWrites` is explicit, nonparallel and categorized Live. It requires `RAINPOINT_LIVE_SETTINGS` and reads homes, preferences, catalogs, members, invitations and scenes without configuration or valve writes. The final expanded fixture passed on both targets after connectivity returned, including time-zone, currency and weather-type catalogs. Results: `artifacts/administration-live-final`.

`WeatherLiveTests.ReadsWeatherWithoutConfigurationOrValveWrites` additionally requires `RAINPOINT_WEATHER_ACCESS_KEY` and `RAINPOINT_WEATHER_ACCESS_SECRET` in private process configuration. Select its exact fully qualified test name deliberately and run target frameworks sequentially. Initial sign-in timeouts and a later mixed-number/text schema failure are retained under `artifacts/weather-live`. After the decoder correction, both targets passed with 48 hourly and seven daily entries; no current-observation block was returned. Eight regression cases cover the corrected behavior. Failed attempts are not counted as passes. See [weather validation](../docs/WEATHER.md).

Scene creation/replacement can activate future actions immediately, so no scene-writing live test was run. See [scene validation limits](../docs/SMART-SCENES.md) and the [completion ledger](../docs/TODO.md).

## Account, pairing and scene-history checks — 25 September 2026

New offline coverage checks solar scene windows, typed execution-history pagination/result decoding, email registration/recovery, profile/password changes, all-zone sensor-association cleanup on removal and RF pairing/cancellation. Tests use synthetic accounts and in-process HTTP handlers; they cannot send verification emails, change a real password or remove/pair hardware. Actual WPF tests verify password-box clearing, encrypted saved-account removal and pairing/removal confirmations. See [account scope](../docs/ACCOUNT-ADMINISTRATION.md), [pairing scope](../docs/DEVICE-PAIRING.md) and [scenes](../docs/SMART-SCENES.md).

The existing explicit `AdministrationLiveTests.ReadsHomeMembersAndInvitationsWithoutWrites` now also asserts the sign-in profile and reads one week of scene history. It passed sequentially on net10.0 and net472, with email/nickname present and empty history pages. Results are retained in ignored `artifacts/profile-history-live`. It makes no account, scene or device writes. Select the exact test deliberately with `RAINPOINT_LIVE_SETTINGS` pointing to private settings; normal offline runs exclude category `Live`.


## Explicit session-recovery and observer-duration checks

`SessionRecoveryLiveTests` contains deliberately selected, nonparallel live checks. They require private `RAINPOINT_LIVE_SETTINGS`, use NUnit and the test adapter on net472/net10.0, and send no device-setting or watering commands. Run them one at a time; never run the two frameworks concurrently against one account.

- `AnotherLoginInvalidatesSessionAndOneRecoveryRestoresMonitoring` signs in a second client after a two-minute interval, requires actual rejection of the original session, waits another two minutes to avoid the login throttle, then permits exactly one credential callback. It requires a new MQTT connection and fresh decoded status before passing. It can displace other uses of this account. A second login without rejection does not count as a recovery pass.
- `AnotherLoginWithImmediateCredentialsRestoresMonitoring` uses an immediately returning credential provider, matching the Windows app, and fails on an exhausted recovery attempt. It verifies actual rejection and restored monitoring independently of the first fixture's deliberate provider delay.
- `ObserverCredentialsRenewDuringTwelveMinuteReadOnlyRun` observes twelve real minutes with no clock injection or password fallback. It requires a sustained MQTT connection followed by a second connection, continuing decoded status and a connected final state. It reports whether the original cloud-session expiry was crossed; observer credential rollover alone does not establish natural cloud-session expiry.

Select each exact fully qualified test through `dotnet test` or Test Explorer. Normal offline runs and the default adapter filters exclude `Live`. Logs omit credentials, account identifiers and MQTT payloads. Tests stop and await their workers before logout/disposal; a displaced client's stale session is not logged out after successful recovery. Results belong in ignored `artifacts/session-observer-live` and failed runs must be retained.


## Isolated home and room write validation

`AdministrationWriteLiveTests.TemporaryEmptyHomeAndRoomRoundTrip` requires private `RAINPOINT_LIVE_SETTINGS` and `RAINPOINT_LIVE_ADMIN=temporary-empty-home`. Select the exact test intentionally, one framework at a time. It creates a uniquely named empty home, renames it, creates/renames/deletes a test room, changes and restores display units/date format and time zone, verifies currency and public fixture coordinates, then deletes the temporary home. Existing homes, device assignments, notifications, invitations and hardware are not modified. This is a cloud write test, not an offline fixture.

Before creation it persists a private, account-bound recovery journal next to the settings file. Cleanup checks identity against the pre-test home list, ownership, absence of hardware/automations/additional members and the expected room names. It never deletes a pre-existing home. An uncertain creation is discovered by its exact unique names; creation and edits are never replayed. If the result or cleanup remains uncertain, the journal is retained and the test fails. `AdministrationWriteLiveTests.ReconcileTemporaryEmptyHome` uses that same journal to reconcile only the fixture's empty home. Do not remove the journal manually to bypass a failed cleanup.

Successful cleanup verifies the temporary home is absent and the original home identifiers remain before removing the journal. These checks do not establish membership/invitation delivery, device moves, account-profile writes, scene execution or pairing. Use ignored `artifacts/temporary-home-live` for retained results.


On 25 September 2026, the temporary-home fixture passed on both runtimes with confirmed deletion and journal removal; results are in `artifacts/temporary-home-live`. The real-clock twelve-minute observer checks also passed on both runtimes. Corrected credential-recovery retests are tracked separately in the [session evidence](../docs/SESSION-RECOVERY.md).


On 25 September 2026, both observer-duration and immediate credential-policy fixtures passed on net472/net10.0, as did the isolated home/room/preference round trip with confirmed cleanup. See [session evidence](../docs/SESSION-RECOVERY.md) and [administration evidence](../docs/HOME-ADMINISTRATION.md). These explicit live passes are separate from the offline suite totals.

## Disabled all-zone schedule round trips

`AllZoneScheduleLiveTests.DisabledPlansRoundTripAllZonesModesAndRecurrences` requires private `RAINPOINT_LIVE_SETTINGS` and `RAINPOINT_LIVE_SCHEDULE=disabled-all-zones`. Run the exact test deliberately through NUnit/Test Explorer, one target framework at a time. It requires exactly one matching paired hub/timer, decoded settings and empty plan lists in all three zones. Do not run another account client or configuration editor concurrently.

For each zone and each of the three plan modes it adds one disabled daily plan, then replaces it with odd-date, even-date, selected-weekday and interval-day variants. Once creation is now rejected by the client. It changes start time, duration and optional volume limits too. All variants stay disabled and use 31 December 2083 as their effective date. The current test verifies 45 combinations through typed read-back, compares the complete timer parameter after each write, and deletes the temporary plan before moving to the next mode. It never enables a plan or sends a valve command, including on zones 2/3.

A private `.all-zone-schedules.json` journal beside the settings file records exact hardware identity, zone, original configuration and known fixture states before each write. Replacements update it atomically. Unexpected configuration prevents deletion. A failed or uncertain write/read-back retains the journal and stops the matrix; it does not replay the write or race cleanup against a potentially delayed write. Normal cleanup uses a separate cancellation budget and requires complete original-configuration read-back before deleting the journal.

After an interrupted run, deliberately select `AllZoneScheduleLiveTests.ReconcileJournaledDisabledPlan` with the same private settings and opt-in. It requires unchanged hardware identity and 30 seconds of stable reads, then removes only the exact disabled fixture plan if still present. If state changes or does not match the original/known fixture values, it fails and preserves the journal. Do not remove the journal manually to bypass reconciliation. This bounded cloud check cannot establish RF delivery or rule out arbitrary service delays.

Retain results in ignored `artifacts/all-zone-schedules-live`, including failures. These `Live`/`Configuration` fixtures are excluded from normal offline runs. Passing proves disabled cloud storage, replacement and restoration; it does not prove enabled execution, physical recurrence timing or volume cutoff.


## Zone-1 scheduled execution

`ScheduledZone1LiveTests.DailyZone1PlanReportsStartAndAutomaticStop` requires private `RAINPOINT_LIVE_SETTINGS` and the explicit `RAINPOINT_LIVE_SCHEDULE=daily-zone1-60` opt-in. The failed Once fixture has been retired because Once is no longer writable. It can cause real water flow. Select the exact test deliberately on one runtime at a time. It requires reported owner or administrator access, the known Europe/London home, unambiguous local time, empty plans in all zones, current-month seasonal adjustment of 100%, no active zone-1 rain delay, a connected MQTT observer and all zones reporting closed. Moisture rules must be explicitly inactive; a missing rule is accepted only with decoded sensor settings showing no assigned sensor. It changes none of those prerequisites to force a run.

The fixture schedules one normal 60-second zone-1 occurrence with daily recurrence six to seven minutes ahead, first saving a disabled plan and verifying it before enabling once. It sends no manual start. The lead time also spaces sequential runs; never run it concurrently with another client signed into the same account or with any other configuration writer. A distinct, already shared account may be used for read-only app inspection after concurrent sessions have been verified. A private `.scheduled-zone1.json` journal is persisted before any plan write, with hardware identity and exact original/disabled/enabled states. It never commands zones 2/3. No credential or private configuration values are printed.

Success requires a new normal-mode MQTT report near the scheduled time and a later idle report consistent with one minute, both before cleanup sends Stop. Cleanup deletes only the exact fixture plan, requires original-configuration read-back, sends a zone-1 Stop with a separate budget even if plan deletion failed, and requires a final closed cloud state. Only then is the journal removed. A failed run preserves its result; uncertain cleanup preserves the journal. `ScheduledZone1LiveTests.ReconcileScheduledZone1Plan` removes only the journaled fixture plan and sends Stop without creating or enabling anything. Do not bypass the journal by deleting it manually.

A successful telemetry check does not measure physical water volume, prove valve motion, validate every recurrence or establish seasonal/rain-delay behavior. Retain results under ignored `artifacts/scheduled-zone1-live`; this fixture is excluded from normal offline runs.

## Concurrent shared accounts

`SharedAccountLiveTests.TwoAccountsKeepSharedHubSessionsAndObservers` requires two private files: `RAINPOINT_LIVE_SETTINGS` and `RAINPOINT_LIVE_SECONDARY_SETTINGS`. The emails must differ, and both accounts must already have access to the same home, hub and timer. This fixture never registers an account, sends an invitation, changes membership/settings or waters a zone.

It signs in the first account, waits two minutes before the second login, then requires simultaneous authenticated MQTT observers and fresh decoded reads of all three zones. Eight rounds of concurrent reads must pass. It signs out the second account and verifies that the first remains authenticated and connected. Both observers are stopped before their owning clients are disposed. The gap tests concurrent established sessions separately from rapid-login rejection; it is not an automatic login retry or a claim about the service's exact throttle scope.

Decoded push counts are reported separately. A run with zero push events establishes concurrent sessions, connected observers and reads, not delivery of the same changing device event to both accounts. Private identities, tokens, passwords and payloads are omitted. The read-only diagnostics report enabled flags and device/home clock metadata to investigate scheduled execution. Select the exact explicit fixture on one runtime at a time; retain successes and failures in ignored `artifacts/shared-account-live`.


The concurrent shared-account fixture passed on both runtimes on 25 September 2026, including independent logout. Both observers stayed connected; zero changing decoded pushes occurred. Earlier timeout/throttle results are retained separately.


`SharedAccountLiveTests.PairedHomeReportsAccountRole` is a separate explicit, read-only check using `RAINPOINT_LIVE_SETTINGS`. It requires exactly one matching paired hub/timer and explicitly reported home access, then prints owner/role and whether that meets the official app's plan-edit prerequisite. It makes no permission, settings or valve changes. Retain results in ignored `artifacts/account-role-live` and space separate account sign-ins to avoid confusing rapid-login rejection with authorization behavior.


### App-supported daily recurrence comparison

`ScheduledZone1LiveTests.DailyZone1PlanReportsStartAndAutomaticStop` uses the same guarded one-minute zone-1 execution fixture with `RAINPOINT_LIVE_SCHEDULE=daily-zone1-60`. Daily is offered by the timer app and passed the net472 execution check; the earlier failed Once attempts remain retained. Once creation/enabling is now rejected. It observes a single occurrence and removes the recurring plan immediately afterward, restoring the exact original configuration. The normal duration cap remains 60 seconds and no other zone is commanded. An interrupted cleanup can leave a daily recurring plan: retain the journal and reconcile it using `ReconcileScheduledZone1Plan` with the same settings and either scheduled-test opt-in. Do not leave a failed cleanup unresolved or remove its journal manually.

The owner-account Once attempt also failed to report a start, despite a connected observer and fresh polling. That narrows the cause beyond account role. A daily comparison is a separate recurrence investigation, not a retry of an uncertain command. All failures remain retained.


The daily execution passed on net472, with MQTT active/idle reports 59 seconds apart before cleanup Stop, exact plan removal and final closed-state confirmation. This is not a net10.0 execution pass or a physical-volume measurement. See [the retained timeline](../docs/SCHEDULES.md#daily-execution-passed-and-once-writes-restricted--25-september-2026). No additional water run is needed to validate the local Once rejection; its offline checks run on both runtimes.

The updated processor workflow also passed 2,640 local checks and all 1,320 processor cases, with no failures/skips. Test-instance cleanup passed and the reservation was released; the pre-existing package archive was preserved. No production driver deployment or reboot occurred. Details remain with the separate local processor-test repository.


### Reversible account nickname validation

`AccountProfileLiveTests.NicknameRoundTripPreservesOtherProfileFields` requires `RAINPOINT_LIVE_PROFILE=nickname-roundtrip` and `RAINPOINT_LIVE_SETTINGS`. Select the exact explicit test on one target at a time. It saves a private recovery journal before changing the nickname, signs in again to verify the change, restores the original nickname, then signs in again to verify restoration. Sign-ins are spaced by two minutes. Account identity, email, photo and language must remain unchanged. No password change, email or device command is submitted.

Use `AccountProfileLiveTests.ReconcileNickname` with the same settings and opt-in if interrupted. It accepts only the original or exact temporary nickname on the original account; unrelated changes stop recovery rather than being overwritten. Keep its journal until restoration is confirmed. Results belong in ignored `artifacts/nickname-live`.

### Cloud-only room assignment validation

`RoomAssignmentLiveTests.AllZoneAssignmentsRoundTrip` requires `RAINPOINT_LIVE_ROOMS=all-zones` and owner-account `RAINPOINT_LIVE_SETTINGS`. It requires exactly one matching paired hub/timer, three supported zones and no existing room assignment for that hub/timer. It creates a uniquely named temporary room, assigns zones 1, 2 and 3 individually, then together, then clears the assignments. Each step reads back typed assignments and checks that original room metadata is unchanged. Cleanup deletes only the temporary room and confirms the original rooms remain intact. These are cloud room-grouping changes, not device settings or valve commands.

Use `RoomAssignmentLiveTests.ReconcileTemporaryRoom` with the same settings and opt-in after an interruption. An uncertain creation is discovered by its unique journaled name; known identity, original room data and assignment checks precede cleanup. Existing or externally changed rooms are never overwritten. Results belong in ignored `artifacts/room-assignment-live`.

### One explicitly approved scene notification

`SceneNotificationLiveTests.OneApprovedNotificationSceneRoundTrip` accepts one explicitly approved notification recipient per run. The first net10.0 run delivered its email (independently confirmed) and completed scene cleanup, but the full test failed while reading execution history; see [retained evidence](../docs/SMART-SCENES.md#notification-scene-live-observation--25-september-2026). Obtain explicit approval to send the email before setting `RAINPOINT_LIVE_SCENE=one-approved-email`, `RAINPOINT_LIVE_NOTIFICATION_TO` to that recipient, and owner-account `RAINPOINT_LIVE_SETTINGS`. Select the exact test on one runtime only; a second run sends another email, so ensure it is covered by the recipient authorization before running.

The fixture uses the existing scene-capable hub with a notification-only action. It first checks populated creation/detail, replacement and both switch states against dates seven/eight days in the future. It then schedules one occurrence about seven minutes ahead, reads its execution history, and disables/deletes the temporary scene. The message is `RainPoint client notification test. No watering command was sent.` There are no timer actions, valve commands, rain-delay changes or modifications to existing scenes. Cloud success is separate from actual inbox delivery, which must be confirmed independently.

A durable private journal is written before creation and updated with the discovered scene identity. `SceneNotificationLiveTests.ReconcileNotificationScene` uses the same settings/opt-in to remove only the exact recorded notification scene; it does not schedule another message. Never discard the journal while cleanup remains uncertain. Retain results under ignored `artifacts/scene-notification-live`.

Nickname and cloud-only all-zone room assignment checks passed on both runtimes on 25 September 2026, including confirmed restoration and journal removal. The initial net472 room check timed out during sign-in before any write; its failure remains retained. Focused offline account, scene and device-administration checks passed 97 cases per runtime. That nickname/room slice added explicit live fixtures only and left the then-current 1,320-case portable suite unchanged. The subsequent notification-history fix adds four portable cases; all `Live` fixtures remain excluded from processor execution.

`SceneHistoryLiveTests.ReadsRecordedNotificationSceneHistory` is a separate explicit read-only check using `RAINPOINT_LIVE_SETTINGS` and a private `RAINPOINT_LIVE_HISTORY_BOOKMARK` containing the recorded home, scene and UTC due time. It can inspect the scene before or after deletion, sends no message and makes no configuration changes. Empty history is inconclusive, not a populated-history pass. Earlier checks that assumed deleted-scene retention failed and remain retained. The cloud controls retention; do not use deletion as a guarantee that history will still be available.

### Notification history decoding correction — 25 September 2026

The separately approved net472 follow-up also delivered its email and completed populated CRUD/switch checks and cleanup. Its private history capture identified the decoding failure: a successful notification action omits `addr`. `DeviceAddress` is now nullable, with omitted/null/explicit-zero coverage and continued rejection of a missing result. Both original live results remain failures, and populated server pagination remains unverified. Both email deliveries were independently confirmed; neither test contained a watering action.

`SceneHistoryLiveTests.ReplaysCapturedNotificationHistoryWithoutNetwork` accepts `RAINPOINT_LIVE_HISTORY_CAPTURE`, the path to a private diagnostic capture containing HTTP status and body. Select this explicit test separately on each target. It uses an in-memory handler and synthetic login, requires no account credentials and performs no network calls or writes. It is in the excluded `Live` category because it needs a private local evidence file, rather than a portable processor fixture. The real captured response passed on both runtimes; results remain in ignored `artifacts/notification-history-replay`. Four sanitized portable regression cases are included in the normal 1,324-case suite.

The updated local processor workflow passed 2,648 local cases and all 1,324 processor cases, with zero failures/skips. All workflow stages passed, including test-instance removal and package cleanup policy, and the reservation was released. No production driver deployment or reboot occurred. Results are retained privately under ignored `artifacts/notification-address-processor`. The solution Release build passed with zero warnings/errors; the edited C# files also pass the solution whitespace-format check. No further notification or watering action was used to validate the fix.

### Bounded cycle-and-soak observation

`Zone1LiveTests.BoundedZone1CycleAndSoakReportsMqttTransitions` requires private `RAINPOINT_LIVE_SETTINGS` and `RAINPOINT_LIVE_ZONE1=cycle150`. It sends exactly one zone-1 cycle start (five watering minutes, one-minute bursts/pauses) and stops after 150 seconds. A baseline requires zone 1 idle, no saved schedules or active moisture rules, and a connected MQTT observer. Zones 2/3 are not commanded and must retain their initial reported modes. This permits the owner's existing zone-3 tap use.

The fixture requires fresh mode-3, mode-7, resumed mode-3 and post-stop idle feedback. Reported burst/pause intervals must each be 45–85 seconds. A new start is never retried; cleanup uses an independent cancellation budget. Private `RAINPOINT_LIVE_PROGRESS` may name an optional sanitized local progress file so a watching operator can follow the timing; file errors do not prevent cleanup. Physical water delivery needs a separate observation. Keep at least five minutes between a confirmed stop and the next start, and keep the number of runs limited.

### Bounded saved-plan volume cutoff

`ScheduledZone1LiveTests.VolumeLimitedZone1PlanStopsBeforeDuration` uses `RAINPOINT_LIVE_SCHEDULE=volume-zone1-180`. It creates one selected-weekday zone-1 occurrence with a one-litre limit and three-minute duration cap, seven minutes ahead. It uses the existing exact-identity recovery journal and removes the recurring plan, restores original configuration and stops zone 1 afterward. The same `ReconcileScheduledZone1Plan` accepts this opt-in for interrupted cleanup. No other zone is commanded or changed.

The volume check requires automatic idle well before the time cap and completion usage consistent with one litre, allowing reported valve/feedback delay. This is a device-reported cutoff check, not independent volumetric calibration. It must not be counted as a live pass until it runs successfully with water available. Do not run live watering tests while the owner is checking the supply valve.
The soaking-state correction passed 1,330 portable cases and 87 WPF cases per runtime (2,834 desktop passes), plus 2,660 local and 1,330 processor checks in the separate workflow. All workflow stages passed and the reservation was released. Release build: zero warnings/errors; edited files pass the whitespace check. Results remain in ignored artifacts/soaking-offline, artifacts/soaking-wpf and artifacts/soaking-processor-stable. The earlier source-guarded workflow error remains in artifacts/soaking-processor; no processor test result was produced by that attempt.

A further read-only net10 history check after spaced sign-in returned no records for the second deleted notification scene, in both its narrow window and wider scene/home queries. The test is inconclusive, not passed; it sent no email or watering command. The earlier login timeout is retained separately under artifacts/scene-history-readonly.
The net10.0 volume/selected-weekday execution check passed on 27 September: reported watering ended after 19 seconds at 1.1 L, before cleanup and the three-minute cap. Exact original settings were restored and the recovery journal removed. See [dated evidence and limits](../docs/SCHEDULES.md#selected-weekday-volume-limit-execution--27-september-2026).


### Remaining bounded execution fixtures

These explicit NUnit fixtures use the existing private settings, exact configuration journal, independent cleanup timeout and final idle verification. Run sequentially, keep at least five minutes between watering runs, and never discard an unresolved journal.

| ScheduledZone1LiveTests method | RAINPOINT_LIVE_SCHEDULE | Check |
| --- | --- | --- |
| OddDaySeasonalPlanReachesBothAccounts | odd-season-shared | Actual odd local date, 120 seconds at 50%, identical changing MQTT events for two already shared accounts. Also requires RAINPOINT_LIVE_SECONDARY_SETTINGS. |
| EvenDayZone1PlanReportsStartAndAutomaticStop | even-zone1-60 | Actual even local date, one 60-second occurrence. Does not change the clock. |
| IntervalPlanSkipsRainDelayThenResumes | interval-rain-resume | Interval-day plan remains idle through its first rain-delayed window, then runs for one minute after clearing the delay and moving that same plan to a later time. Does not establish natural delay-expiry behavior. |

Each removes the temporary plan and restores all original seasonal/rain-delay settings. The shared `ReconcileScheduledZone1Plan` accepts each opt-in. The odd-day/seasonal/shared-event check passed net472 on 27 September; see the dated schedule evidence for its limits. Other prepared fixture names do not imply a live pass.

`SceneNotificationLiveTests.TwoApprovedNotificationsVerifyPagination` requires `RAINPOINT_LIVE_SCENE=two-approved-emails`, owner settings and an explicitly authorized `RAINPOINT_LIVE_NOTIFICATION_TO`. It schedules exactly two fixed test emails two minutes apart, checks both successful action results, traverses single-record pages and deletes the temporary scene. There is no device action. Existing recipient authorization may cover multiple development runs; do not solicit duplicate approval when the owner has already authorized that scope. The fixture retains private history captures to diagnose a server paging mismatch, while public test output contains no account/device identifiers. `ReconcileNotificationScene` accepts this opt-in and only cleans up.


## Completion validation — 27–28 September 2026

The notification-pagination fixture now has a passing net10.0 run after correcting the server page offset. The owner authorizes required development/support messages to the configured support test mailbox; fixture selection and recipient remain explicit. See [scene evidence](../docs/SMART-SCENES.md).

`CalendarLiveTests.ReportedHomeRulesMatchObservedLondonCalendar` reads only the unique paired kit's home and validates its reported offset/transition table against the observed populated official-app interval calendar. It sends no writes. Use the normal private `RAINPOINT_LIVE_SETTINGS` opt-in. Earlier single-home-selection failure and the corrected passing result are retained.

`AdministrationWriteLiveTests.TemporaryHomeMembershipRoundTrip` uses `RAINPOINT_LIVE_ADMIN=temporary-empty-home` plus both private settings paths (`RAINPOINT_LIVE_SETTINGS`, `RAINPOINT_LIVE_SECONDARY_SETTINGS`). Only the dedicated support account is invited. The fixture creates an isolated empty home, accepts its invitation, verifies administrator/member roles and removes that member. Cleanup verifies the absence of hardware, unexpected members and scenes before deleting only the recorded temporary home; original home identities are preserved. The existing explicit `ReconcileTemporaryEmptyHome` uses the same journal if interrupted. An uncertain membership removal is not replayed.

`AccountPasswordLiveTests.TestAccountPasswordRoundTrip` requires `RAINPOINT_LIVE_PASSWORD=support-account-roundtrip` and the dedicated test account's private settings. It records sensitive recovery values beside those ignored settings before changing anything, verifies the temporary password by fresh sign-in, restores the original and verifies it. Sign-ins are spaced by two minutes. Never attach its private journal or run it against the owner account. `ReconcileTestAccountPassword` is the explicit interrupted-run recovery path; uncertain restoration is not replayed. The settings file itself is not rewritten.

`RAINPOINT_LIVE_REQUIRE_FLOW=1` strengthens the bounded manual cycle/misting fixtures to require fresh completion plus nonzero final reported usage. Keep at least five minutes between watering runs. This verifies functional delivery feedback, not independent flow-meter calibration.

The separate processor workflow passed 2,704 local plus 1,352 processor cases on 28 September. Every stage passed and its reservation was released. A pre-existing package path was preserved during cleanup. The client has no processor packaging dependency; those files remain in their separate local repository.

Final account-only password validation passed on the dedicated support account, including fresh authentication with both the temporary and restored original password. All temporary home, scene, schedule and password recovery journals were removed after verification. The final release solution build had zero warnings/errors and EditorConfig verification passed. BlueStacks was signed out, closed and released. All work remains local.
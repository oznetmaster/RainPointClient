# RainPoint upstream capability parity

## Scope and acceptance

The initial parity target is the hardware available for testing: **HWG023WBRF / HWG023WBRF-V2 hub and HTV345FRF timer**. All three zones are supported and offline-tested; short live valve-opening tests on all three zones were authorized on 27 September 2026; see [current test constraints](TODO.md) and [all-zone results](ALL-ZONE-LIVE-TEST.md). Other device families are deferred, not counted as implemented. Parity means equivalent useful client behavior with typed public APIs, not copying raw JSON interfaces, display conventions or unverified assumptions.

The pinned sources and their commits are in [PROTOCOL-SOURCES.md](PROTOCOL-SOURCES.md). This inventory compares their actual client implementations and decoder behavior. It does not treat every feature of the vendor app or Home Assistant as an upstream library capability.

The expanded vendor-app target is tracked in [ANDROID-APP-AUDIT.md](ANDROID-APP-AUDIT.md). It includes capabilities absent from the Python wrappers. A signed-in, read-only inventory of RainPoint Home 1.19.1065 was recorded on 24 September 2026. It identifies additional app capabilities and explicitly separates observed UI, documented behavior and untested operations. BlueStacks was used only for that owner-authorized, one-off evaluation; it is not a project workflow dependency. The Android Studio emulator startup issue remains unresolved.

## Current hardware capabilities

| Capability | Client status | Evidence / remaining work |
| --- | --- | --- |
| Login, home and device discovery | Implemented | Offline and live desktop checks on both target frameworks. |
| Zone default watering duration and misting on/off times | Typed reads/writes implemented | App comparison and exact restoration verified using the invited member account; net10.0 preparation, net472 restoration. No watering. See [zone defaults](ZONE-DEFAULTS.md). |
| Zone flow calibration | Typed reads/writes implemented | Signed -20..20%; zone-1 1% app comparison and exact net472 restoration verified. Physical volume accuracy remains unverified. See [calibration](FLOW-CALIBRATION.md). |
| Saved RF timer schedules | Typed reads and normal/cycle-and-soak/misting plan editing with optional volume limits implemented | Disabled normal, cycle-and-soak and misting plan creation/readback/app comparison/deletion verified, with exact restoration. Disabled 1.4 L variants of all three modes also passed storage/restoration on both targets. Disabled replacements passed in all zones/modes on both runtimes. One daily zone-1 execution passed net472. Once creation/enabling was removed after failed execution checks; existing records remain readable and removable. One selected-weekday zone-1 occurrence with a 1.0 L limit also passed net10.0: idle after 19 seconds at reported 1.1 L, with exact cleanup. Odd-day/seasonal, even-day and interval/rain-delay executions now have device-reported evidence and restoration; the even-day fixture needed separate cleanup recovery after a read timeout. Other mode/limit combinations and independent volume calibration remain unverified. See [schedules](SCHEDULES.md). |
| Calendar and next-start projections | Implemented | Pure typed local-date projections with bounded search; six supported recurrence types, seasonal display and explicit rain-delay status. See [calendar](CALENDAR.md). Populated interval-calendar comparison, actual home rules and the autumn DST correction are verified; execution evidence is separate. |
| Seasonal adjustment / zone rain delay | Typed reads/writes implemented | Zone-1 settings write/readback/app comparison and exact restoration passed. Odd-day 120-second configuration at 50% produced 59 reported seconds and 3.2 L; interval occurrence was suppressed by rain delay and ran after explicit clearing/rescheduling. See [settings evidence and limits](PLAN-SETTINGS.md). |
| Manual zone start/stop | Implemented | Zone 1 physical start, automatic stop and explicit stop observed. Zones 2 and 3 also passed one-minute live feedback checks; zone 2 reported 2.8 L and zone 3 reported 0 L with its downstream manual tap closed. |
| Manual misting/cycle-and-soak | Implemented | Typed mode-specific timing, firmware gating and all-zone Windows controls; corrected zone-1 misting MQTT transitions passed live on net472 and net10.0. Cycle-and-soak also passed with fresh watering/pause/resumption/idle reports and 5 L; the later one-minute misting run reported automatic idle and 1.1 L. Independent physical burst timing is not measured. See [manual modes](MANUAL-MODES.md). |
| Normal irrigation duration validation | Implemented | 60..43200 whole seconds for this model; bounded by its manual, not a generic timer-family guess. |
| Last cloud zone state and configured duration | Implemented | Typed nullable readings; cloud and app can lag physical state. |
| Last usage in litres | Implemented | Two upstream decoders use /10; 14 counts matched the owner's 1.4 L display. No independent volume calibration. |
| Timer RF signal | Implemented | Live reading obtained. |
| Battery condition / low indication | Implemented | 1 normal, 2 low, other codes unknown; mapping from related HTV evidence. Only normal condition observed on this timer. No percentage. |
| Device-local report and zone event times | Implemented | Packed local time with unspecified timezone; no fabricated UTC offset. Normal report time observed live; active event-time interpretation remains reference-derived. |
| Per-zone alarm code | Nullable leak, water-shortage and freeze flags; unknown bits retained | Vendor app status mapping established; physical fault generation not tested. See [alarms](SENSORS-AND-ALARMS.md). |
| Hub connection and Wi-Fi signal | Implemented | One batch request returns hub plus supported timer readings; live cloud connection and Wi-Fi signal obtained. |
| Firmware version and available-update information | Implemented, read-only | Live hub installed 1.1.1041 and timer 130, no update offered at observation. No firmware installation operation. |
| Automatic time-broadcast setting | Typed read/write implemented | Offline tested including fresh pre-write read and preservation of unrelated settings. This hub returned no readable setting; no live write attempted. |
| One-shot time broadcast | Typed operation implemented | Offline request contract tested; not invoked on hardware in this phase. |
| Refresh-token exchange | Explicit operation implemented | Documented /auth/basic/app/token/refresh endpoint passed a live refresh followed by an authenticated read. Rotation and required expiry covered offline. |
| Logout | Implemented | Live logout completed; local session clears even on remote failure. |
| Automatic session renewal/re-login and notifications | Implemented | Opt-in worker, typed state events, cooldown, one caller-authorized password login per worker and no command replay. Server invalidation/recovery and twelve-minute real-clock MQTT credential-renewal checks passed on both targets; natural cloud-session expiry remains unverified. See [session recovery](SESSION-RECOVERY.md). |
| MQTT observer credentials, connection, push updates | Implemented; full net10.0 cycle verified | After correcting mixed numeric metadata, a full MQTT open/closed cycle passed on net10.0 and stop-only feedback passed on net472. The earlier failed cycle is retained. Twelve-minute real-clock MQTT credential-renewal checks passed on both runtimes; natural cloud-session expiry remains unverified. See [MQTT evidence](MQTT-FEEDBACK.md). |
| Long-running polling / push reconciliation | Implemented; bounded endurance checked | Timestamp/revision ordering, retained timer source/receipt times, reconnect and cancellation covered offline. Twelve-minute real-clock runs and simultaneous distinct-account observers passed on both runtimes. Multi-day stability and natural cloud-session expiry remain unverified. |
| Product-catalog metadata | Implemented | Typed immutable model/model-code variants and data-point metadata; 104 variants read live on both targets. See [catalog](PRODUCT-CATALOG.md). |
| Hub RF channel and additional settings | Typed channel read/write implemented | Vendor native contract traced; channel 1→2→1 read-back/restoration passed both desktop targets with idle zones and no plans. Independent RF reception on channel 2 remains unverified. See [RF channel](RF-CHANNEL.md). |
| Control-response status | Implemented | Typed optional observations from object/string responses, separate acknowledgement and timestamp; all-zone offline and Windows binding coverage; zone-1 stop-only response decoded live on both targets. See [semantics and evidence](COMMAND-FEEDBACK.md). Legacy non-framed state remains unsupported. |

## Not established as upstream capability for this kit

- The broad integration uses Home Assistant scheduling rather than creating the app's watering plans. Its schedule-related sensors and cycle/end-time readings are not schedule CRUD APIs.
- Manual misting/cycle-and-soak now follows the traced vendor-app command contract; it exceeds the normal-control scope of the inspected Python valve wrappers. Physical timing remains a separate validation item.
- Daily totals, live flow rate and cumulative meters are decoded for other products (notably dedicated flow meters). That is not evidence that the HTV345FRF exposes those same datapoints.
- Generic raw-payload escape hatches, Home Assistant dashboards/entities/automations, anonymous telemetry and diagnostic payload dumps are not public client API requirements.

## Deferred device families

The references also cover one/two/four-zone timers, hub-paired Bluetooth valves and DP control, multi-station controllers, soil/moisture sensors, rain gauges/detectors, temperature/humidity/CO2/pool sensors, flow meters and weather/display hubs. Discovery alone does not establish functional support for those devices. Their decoders, model variants, units, settings and control paths need separate implementation and fixtures when the scope expands.

## Test workflow

Offline NUnit tests run on **net472 and net10.0**, using NUnit3TestAdapter. Repeatable live and emulator validation uses the established NUnit/test-adapter workflow, with explicit private targets and retained results. Exploratory probes and the explicitly authorized one-off vendor-app inventory provide development observations only; they are not workflow gates. BlueStacks must not become a dependency of that workflow.

Repeatable comparison with the official Android app requires a compatible Android Studio runtime. The Windows application tests use simulated responses and are not independent protocol evidence.

Current offline validation: **1,352 library/dashboard cases and 87 Windows app cases pass on each applicable target (2,878 total)**. Current counts and retained results are in the [test README](../tests/README.md). During the earlier calibration slice, one existing net472 observer-expiry test timed out; its fixture and full suite passed on recheck. The Windows settings, history and saved-plan slices passed the full offline suite on both targets; all results are retained. The later schedule and settings slices account for the increase from the original 177-case library baseline. The [Windows reference app](WINDOWS-APP.md) now includes default-duration, misting, calibration, seasonal and rain-delay editors, hub tools and session recovery, usage/event history and saved-plan management in all three zones, tested with simulated responses on 24 September 2026; it is not independent vendor-app or new live-hardware evidence. MQTT NUnit checks ran on both targets, including a corrected full zone-1 cycle on net10.0 and stop-only feedback on net472; repeatable Android Studio emulator comparison remains pending. All-zone read-only settings/history, catalog, firmware and one renewal-path check now pass on both desktop targets. The separate one-off app inventory does not satisfy those validation items. Earlier net472 hardware start/stop and read-only evidence remains recorded separately.

## Remaining work

See [the maintained ledger](TODO.md). Full vendor-app parity is not claimed.

## Vendor-app lifecycle follow-up — 25 September 2026

The client and Windows app now add [account operations](ACCOUNT-ADMINISTRATION.md), [supported RF pairing/removal](DEVICE-PAIRING.md), solar effective scene windows and typed execution-history pages. These extend the original upstream-focused scope. Profile/history reads passed live on both targets; histories were empty. Initial hub provisioning, additional device families and the documented hardware checks remain separate limitations. See the [completion ledger](TODO.md) for the current distinction between implementation and verification.
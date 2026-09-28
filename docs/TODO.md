# RainPoint completion ledger

Updated 28 September 2026. All three HTV345FRF zones are supported and tested offline. The owner now authorizes short live tests on all three zones. Use sequential runs, keep each below ten minutes, leave at least five minutes between watering runs, and preserve non-selected zones. This supersedes the previous zone-1-only restriction and supply-readiness hold. These are test constraints, not product limitations.

The implementation and validation work possible with the currently paired hub/timer is complete. The remaining unchecked entries are grouped under **Deferred validation and scope extensions**, with their concrete requirements. Independent physical observation/calibration is not an acceptance gate for the verified device-reported behavior. No release or remote submission is part of this completion pass.

## Completed implementation and desktop checks

- [x] Typed client, net472/net10 and explicit System.Text.Json attributes.
- [x] Discovery, metadata, normal manual start/stop, status and MQTT feedback.
- [x] Typed optional command-response observations and separate Windows feedback, with object/string compatibility and no operation replay. See [response semantics](COMMAND-FEEDBACK.md).
- [x] Typed manual misting/cycle-and-soak commands and Windows controls for all zones, with firmware and timing validation. See [manual modes](MANUAL-MODES.md).
- [x] Normal, cycle-and-soak and misting plans, all supported recurrence forms, read/create/replace/enable/delete, all zones.
- [x] Optional water-volume limits in all three saved-plan modes, with all-zone client and Windows coverage; disabled zone-1 storage/restoration passed both desktop targets. See [limits](SCHEDULES.md#optional-volume-limits).
- [x] Disabled-plan creation, replacement, the five currently writable recurrences plus the historical Once storage case and deletion in all three zones and modes passed live on both runtimes (54 combinations per run). Full original configuration restored after every mode; no plan enabled.
- [x] Default duration/misting intervals, flow calibration, monthly seasonal adjustment and per-zone rain delay.
- [x] Usage/event history, including timer-wide power events in zone queries.
- [x] Typed calendar/next-start projections and Windows Calendar tab for all three zones; six decoded recurrence projections, five writable choices, seasonal display and explicit rain-delay status. See [calendar limits](CALENDAR.md).
- [x] RF-channel writes and Windows control; channels 1–3, fresh-state guards, change/read-back/restoration passed both desktop targets. See [RF channel](RF-CHANNEL.md).
- [x] Firmware checks, time broadcast and its readable automatic setting, reported RF channel, typed product catalog.
- [x] Session renewal, typed state notifications, bounded optional saved-credential recovery and no operation replay.
- [x] Windows controls for these implemented features; encrypted saved credentials and automatic startup sign-in.
- [x] NUnit and NUnit3TestAdapter coverage on both desktop targets: 1,352 library/dashboard plus 87 WPF cases each (2,878 passes).
- [x] Read-only hardware checks on both targets for all zones' settings/history, catalog/firmware and one renewal path.

- [x] All-zone profiles and watering recommendations, guarded writes and disabled Windows plan drafts; zone-1 preference restoration passed both targets. See [profiles](ZONE-PROFILES.md).

- [x] Soil sensor association, moisture-stop settings and known timer alarm flags, with all-zone Windows controls and offline coverage. See [scope and limits](SENSORS-AND-ALARMS.md).

- [x] Typed automatic low-moisture watering rules and all-zone Windows editor, with offline validation on both targets. See [behavior and limits](MOISTURE-WATERING.md).

## Hardware validation and documented limits

Available validation hardware, confirmed 25 September: one already paired HWG023WBRF hub and HTV345FRF timer. There is no soil sensor or spare unpaired hub. Sensor-dependent checks and initial provisioning remain unverified; the working hub will remain paired. This does not restrict three-zone client support or offline coverage.

- [x] Live server-side invalidation followed by exactly one credential recovery, a new MQTT connection and fresh decoded status, on both net472 and net10.0. No watering or device-setting writes. See [session evidence](SESSION-RECOVERY.md).
- [x] Twelve-minute real-clock MQTT runs on both runtimes, with renewed connections, continuing fresh readings and a connected final state. No device writes.
- [x] Corrected live credential recovery with the Windows-style immediately returning provider on both runtimes: the production cooldown avoids the reproduced rapid-login rejection before its one credential attempt. Earlier throttle and timeout failures remain retained.
- [x] One daily normal zone-1 occurrence passed net472 scheduled execution: active/automatic-idle MQTT reports 59 seconds apart, matching app plan details/next start, exact plan removal and final closed status. Once writes/enabling were removed after two failed execution checks; safe reads/disable/delete remain.
- [x] Populated official-app interval-calendar comparison, including rain delay, next start, the autumn clock-change dates and full restoration. Fixed UTC-day interval projection and verified the actual home transition data; see [calendar evidence](CALENDAR.md).
- [x] Interval-day execution and rain-delay suppression followed by explicit clearing/rescheduling: 59 reported seconds, 3.3 L and complete restoration. Daily/weekday and odd-day/50% seasonal execution also passed; see [schedule evidence](SCHEDULES.md).
- [x] Even-day execution on 28 September: 58.936 seconds and 3.3 L reported. The fixture failed on a cleanup read timeout; separate cleanup-only recovery passed with complete restoration. Both results retained in [schedule evidence](SCHEDULES.md).
- [x] Bounded zone-1 cycle-and-soak MQTT sequence on net472: active cycle, one-minute soaking pause, resumed cycle and cleanup idle. Mode 7 is app-verified soaking and now decoded/displayed explicitly for all zones.
- [x] Cycle-and-soak with fresh watering/pause/resumption/idle reports, one-minute phase intervals and 5 L reported on net10.0. The earlier zero-flow run remains retained. See [manual modes](MANUAL-MODES.md).
- [x] One-minute misting passed net10.0 with fresh automatic idle, 1.1 L reported and acknowledged cleanup. Individual physical burst timing is optional independent corroboration. See [manual modes](MANUAL-MODES.md).
- [x] Short manual normal-watering MQTT start/automatic-idle reports for zone 2 on net472 and zone 3 on net10.0, with independent cleanup and unchanged other-zone modes. Reported last usage: zone 2, 2.8 L; zone 3, 0 L. The owner subsequently confirmed that zone 3 feeds a tap with its manual valve closed, consistent with zero usage. The owner could not watch; see [all-zone evidence](ALL-ZONE-LIVE-TEST.md).
- Device-reported integration acceptance is complete for all zones. Independent visual timing is optional corroboration, unavailable while the owner cannot watch; it is not an unfinished software task. Zone 3 has a closed downstream manual tap. See [all-zone evidence](ALL-ZONE-LIVE-TEST.md).
- [x] One selected-weekday normal zone-1 plan with a 1.0 L limit and 180-second cap passed net10.0: fresh active/idle reports 19 seconds apart, last usage 1.1 L, before cleanup. Temporary plan removed, complete original configuration restored, final idle confirmed and journal removed. This supports device-reported cutoff for that combination; independent physical measurement and other combinations remain unverified. See [volume execution evidence](SCHEDULES.md#selected-weekday-volume-limit-execution--27-september-2026).
- Independent flow-meter calibration remains outside the available setup. The multiple sprinkler outlets prevent a practical collected-volume measurement. Nonzero fresh usage is accepted for functional delivery evidence, without claiming calibrated accuracy.

## Additional vendor-app implementation and verification

- [x] Notification preferences and historical event/alarm categories, separate from timer status flags.
- [x] Home/room/member/invitation management, hub/device rename and all-zone room assignment. See [administration](HOME-ADMINISTRATION.md).
- [x] Units, date formats, currency and IANA time-zone preferences, with preservation of unknown fields.
- [x] Typed current/hourly/daily weather and Windows display with separately supplied signing access. See [weather](WEATHER.md).
- [x] Supported Smart Scene CRUD, weather/time conditions, notification/rain-delay actions, effective periods and Windows editor. See [exact scene scope](SMART-SCENES.md).
- [x] Live hourly/daily weather and final currency/weather-type/time-zone catalog reads on both targets, after connectivity recovery and the weather field-type correction. Current conditions were omitted by the service and remain unknown; earlier failed results are retained.
- [x] Live temporary empty-home and room creation/rename/deletion, units/date format, time-zone change/restoration, currency and fixture-location writes on both runtimes. Temporary homes were removed and original home identities retained.
- [x] Two already shared accounts maintained simultaneous authenticated MQTT connections and all-zone reads on both runtimes; logging out one preserved the other. The later net472 odd-day/seasonal run delivered identical fresh active/idle events to both accounts simultaneously; see [schedule evidence](SCHEDULES.md#odd-day-execution-seasonal-duration-and-simultaneous-accounts--27-september-2026).
- [x] Cloud room assignment for each real timer zone separately, all three together and empty assignments, on both runtimes. Temporary room removed and every original room/assignment unchanged. An initial net472 sign-in timeout occurred before any write and remains retained.
- [x] Populated scene create/read/replace, both switch states and deletion observed on net10.0 and net472. Each separately approved notification email was independently confirmed received. Both temporary scenes and recovery journals were removed, with no watering action. Both full live fixtures failed during history decoding; their results remain failures.
- [x] Captured populated notification history decoded through the corrected client on both runtimes without network access. Notification results can omit the device address; it is now nullable, with four portable regression cases. See [scene evidence](SMART-SCENES.md#notification-scene-live-observation--25-september-2026).
- [x] Live scene-history pagination with two successful notification events, distinct single-record pages and an empty final page. Fixed zero-based public to one-based server translation; cleanup verified. See [scene evidence](SMART-SCENES.md).
- [x] Isolated test-home invitation/acceptance, administrator/member roles and removal passed net10.0. Temporary home and invitation removed, both accounts' original home identities retained and journal removed. See [administration evidence](HOME-ADMINISTRATION.md).
- [x] Typed email registration/verification/recovery, profile observations and nickname/photo/password changes, with Windows controls and saved-credential invalidation. [Scope and validation](ACCOUNT-ADMINISTRATION.md).
- [x] Supported RF child pairing/cancellation and guarded hub/child removal, with all-zone association cleanup, Windows controls and offline tests. [Scope and validation](DEVICE-PAIRING.md).
- [x] Daytime/nighttime solar effective windows and typed paged scene execution history, with Windows filters/action results. Profile and empty scene-history reads passed live on both targets.
- [x] Account nickname write/read-back/restoration on both runtimes, with original identity, email, photo and language preserved; recovery journals removed.
- [x] Dedicated support-account password change, temporary-password sign-in, original-password restoration and final sign-in passed net10.0. Private recovery journal removed; owner account unchanged. See [account evidence](ACCOUNT-ADMINISTRATION.md).
- [x] Bytecode-verified scene action outcomes alongside original numeric codes, with unknown-code preservation.

## Deferred validation and scope extensions

These entries remain explicitly unverified. They require different hardware, longer elapsed time, a replacement image, a compatible vendor-app runtime, or the separately deferred local-protocol investigation. They are not being marked passed to close the current paired-kit implementation work.

- [ ] Automatic low-moisture rule storage and execution with real sensor hardware.
- [ ] Sensor assignment and moisture-stop behavior with real sensor hardware; fault generation for alarm confirmation.
- [ ] Prolonged MQTT operation across natural cloud-session expiry. The service reported about 60 days for a newly issued session on 25 September; short observer-credential renewal and injected-clock refresh checks cannot establish that lifetime.
- Photo replacement requires an already uploaded replacement HTTPS image and a restorable original; none was supplied for this check. Image upload is explicitly outside the current API. Offline photo contracts and Windows controls are complete.
- RF pairing/removal remain unverified because the only available kit is already paired and in use. Testing removal/re-pairing requires a spare device or a separately agreed interruption; implemented offline contracts are complete.
- [ ] Initial hub Wi-Fi/Bluetooth provisioning: exact-model setup handshake remains unverified. Needs an unpaired test hub or a separately approved reset/reprovision session; the configured hub was not reset.
- [ ] Device-specific scene conditions/actions for additional families. The current timer explicitly advertises no scene-action support.
- [ ] Repeatable official-app comparison in Android Studio: current app startup remains incompatible with the configured emulator. BlueStacks remains a one-off inspection tool only.
- [ ] Local-protocol/TLS handshake investigation deferred at the owner's request; no router change or hub reboot was performed in this completion pass.

Other product families are deferred until supported by scope, protocol fixtures and hardware evidence. A catalog entry is not support. The client is not declared feature-complete against every vendor-app screen; the [app inventory](ANDROID-APP-AUDIT.md) and [upstream comparison](UPSTREAM-PARITY.md) retain those distinctions. Completed implementation must not turn unchecked hardware or unknown wire contracts into presumed passes.

The enabled once-only zone-1 execution checks on 25 September, using the member account and then confirmed owner/administrator access, both failed to observe a scheduled start; cleanup restored the original settings and confirmed closed status. [Schedule evidence](SCHEDULES.md#first-enabled-execution-check--25-september-2026) retains the failure and investigation. A subsequent daily execution passed. Once is now outside the supported write surface; other execution limits remain recorded above.
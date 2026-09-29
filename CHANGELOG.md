# Changelog

## [1.1.0] - 2026-09-29

- Expose assigned zone names and typed home-configuration change notifications, scoped to the signed-in account and monitored home.

- Add an opt-in monitor mode that pauses routine status polling after MQTT connects and a catch-up read succeeds. Startup, reconnection, polling fallback and manual refresh remain supported.
- Expose synchronized live-update availability separately from physical device freshness.

- Register account MQTT credentials for home-configuration notifications while retaining compatibility with temporary status observers.
- Update development tests to NUnit 5.

## [1.0.1] - 2026-09-28

Documentation updates. No functional changes.

## [1.0.0] - 2026-09-28

Initial release of the RainPoint Home / Smart+ cloud client for .NET Framework 4.7.2 and .NET 10, written in C# 14.

### Added

- Typed authentication, discovery, refresh/logout and bounded optional session recovery.
- HWG023WBRF-family hub and HTV345FRF three-zone status/control, normal watering, misting and cycle-and-soak.
- MQTT push observations, polling fallback and explicit command-response semantics.
- Saved-plan CRUD, five writable recurrence forms, volume limits, rain delay, seasonal adjustment and typed calendar projections.
- Per-zone defaults, flow calibration, usage/event history, profiles and recommendations.
- Home/room/member/invitation and account administration, supported Smart Scenes/history, weather, metadata and RF lifecycle APIs.
- Windows workbench for both supported runtime generations, encrypted credential storage and optional startup sign-in.
- NUnit/test-adapter workflows, documentation-site generation, gated package/release automation and local release-asset validation.

### Corrected during pre-release validation

- Translate public zero-based scene-history pages to the service's one-based pages.
- Use reported home timezone transitions for vendor interval-calendar dates at daylight-saving boundaries.
- Decode cycle-and-soak pause separately from idle and allow notification history without a device address.
- Reject creation/enabling of unsupported Once plans, retain unknown protocol values and keep uncertain writes from being replayed.

### Validation and limits

The current desktop baseline is 2,878 passing tests: 1,352 portable and 87 WPF cases per target. Dated live evidence and retained failures are linked from the [validation ledger](docs/TODO.md). The cloud client does not require local-protocol research to be complete. Additional hardware families, sensor behavior and natural long-term session expiry remain outside the completed validation scope.

See [publication notes](release-notes/v1.0.0.md) and the [publication procedure](PUBLISHING.md).
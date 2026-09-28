# Device pairing and removal

`StartDevicePairingAsync` starts RF discovery on a discovered HWG023WBRF/HWG023WBRF-V2 hub for one catalog model: HTV345FRF, HCS005FRF or HCS021FRF. It requires a fresh `RainPointHomeDetails` and hub observation, verifies the current topology and catalog entry, and returns the server's search duration. Put the new device in its documented pairing mode and rediscover afterwards. A successful request is not proof that a device paired.

`CancelDevicePairingAsync` explicitly cancels the hub's search. It remains available after an uncertain start. Cancellation does not undo devices already paired. The Windows Pairing tab provides catalog selection, start/cancel, discovery and removal controls; a load does not start pairing. Pairing start and removal require confirmation in the UI. Closing the app does not automatically cancel the hub's search window.

`RemoveHubAsync` removes a hub and its children from the home. `RemoveDeviceAsync` removes a supported child and updates matching soil-sensor associations across all three zones of the remaining supported timers in the same request. It preserves thresholds, schedules, seasonal settings, unknown trailing settings and style values. It rejects unknown sibling device families, unreadable relationships or changed topology. It does not guess how to clear associations on unsupported products.

These writes consume one fresh home observation and are never automatically retried. Reload home/device state after success or uncertainty. The freshness check is not an atomic server transaction. Removal is not a watering-stop command; do not assume it stops an active valve. Removed devices need pairing/setup to restore access.

## Verification and limits

Offline tests on net472 and net10.0 check search/cancel wire contracts, current catalog/model matching, every zone's association cleanup, unrelated-field preservation, hub/child deletion, stale topology, unsupported relationships and uncertain-write behavior. Dashboard and WPF tests verify explicit controls, cancellation after uncertainty and confirmation before changes. No real device was paired, reset or removed during this implementation.

Initial Wi-Fi/Bluetooth provisioning of a new hub remains outside these APIs. The inspected app has several setup transports, including broadcast/multicast and Bluetooth paths, followed by cloud registration. Their presence does not establish which complete handshake the available hub accepts. Establishing the exact setup route needs an unpaired test hub or a separately approved reset/reprovision session; ordinary RF child pairing does not prove it. Existing hardware was kept configured. This feature is cloud-mediated RF pairing, not local/offline hub control.

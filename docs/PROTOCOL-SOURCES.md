# RainPoint protocol source assessment

Inspected on 23 September 2026 for RainPoint Home / Smart+, specifically HWG023WBRF + HTV345FRF. The account app was confirmed by the owner. Sources describe an unofficial cloud API, not a manufacturer-supported SDK contract.

| Source | Inspected commit | Role and limits |
| --- | --- | --- |
| [funkadelic/ha-rainpoint](https://github.com/funkadelic/ha-rainpoint) | `3b1492c62b70e6c194348bbfe68f58cc5920b568` | Primary reference: RainPoint appCode 2; HTTP authentication/control; structural binary record decoding; product catalog with HTV345FRF model code 37 and three CTL_WATER ports; detailed decoder/session tests. Its device list marks this timer as capture-derived rather than hardware-verified. |
| [brettmeyerowitz/homeassistant-homgar](https://github.com/brettmeyerowitz/homeassistant-homgar) | `0897690ea542bd61e4a58baf70b7962f53bfa0df` | Cross-check for broader device support, app identifiers, cloud requests and duration units. Includes MQTT and a refresh endpoint, but its refresh path assumes a seven-day expiry, so that behavior is not adopted without validation. |
| [Remboooo/homgarapi](https://github.com/Remboooo/homgarapi) | `47841c3e686cabc97be6271dcb2d979bb59b82e6` | Original standalone library, useful for discovery and older status conventions. Its documented proof-of-concept scope lacks the newer control/model coverage needed here. |
| [macher91/homgar-homeassistant](https://github.com/macher91/homgar-homeassistant) | `52786d05f6f048ca7d390abe1b2301a726e90001` | Additional MQTT and multi-zone reference. Useful later for weather/sensor devices; not the primary starting point for this timer. |
| [rathga/rainpoint-ha](https://github.com/rathga/rainpoint-ha) | `1ce749a99a0b6f273784a8caa3dca920d504133e` | Focused REST-only example with related two-zone hardware. Useful cross-check, but narrower device/test coverage and different duration policy. |

All inspected repositories carry MIT licenses. Attribution and permission notices are retained in [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).

## Implemented wire contract

- Host: `region3.homgarus.com`; appCode `2`; `auth` header on authenticated calls.
- `POST /auth/basic/app/login`: attributed email/country/device ID model; lowercase MD5 password digest as required by the observed protocol. The digest is only sent over HTTPS and is not a local password-storage mechanism.
- `GET /app/member/appHome/list`: `hid`, `homeName`.
- `GET /app/device/getDeviceByHid?hid=...`: parent `mid`, `deviceName`, `productKey`, paired `subDevices` with RF `addr` and model.
- `POST /app/device/multipleDeviceStatus`: typed `devices` addressing list; select the returned hub by `mid` and read its `status` list. `D02` denotes RF address 2, not cloud database ID 2. This is the normal polling endpoint used by the main references. The earlier single-device GET path was compared during diagnosis but is no longer the client default.
- `POST /app/device/controlWorkMode`: parent addressing plus RF address, one-based `port`, `mode` 1/0, duration in seconds for this RF timer and empty `param`. Code 4 is a distinct already-requested/transitioning outcome only on this endpoint.

No generic send-payload method is exposed. [Typed command-response observations](COMMAND-FEEDBACK.md) retain supported optional status separately from acknowledgement. [Manual misting/cycle-and-soak](MANUAL-MODES.md) use modes 2/3 and typed duration/interval inputs with mode-specific units. Cloud acceptance is not treated as confirmed valve movement.

`11#`/`01#` status values encode records consisting of a datapoint ID, structural type header and little-endian value. Zone N uses ID `0x18+N`/field 30 for work mode (low nibble: idle 0, normal 1, misting 2, cycle-and-soak watering 3 and soaking pause 7; other unknown codes remain unknown), `0x24+N`/field 19 for duration, and `0x28+N`/field 15 for last usage in tenths of a litre on the supported HTV345FRF (see the owner comparison below). ID `0x17`/field 32 is signed RSSI; ID `0x18`/field 31 is a battery condition code. The decoder handles compact and extended headers structurally, skips unknown complete records and rejects truncated frames as a whole. Missing fields are nullable.

## Deliberate differences and unresolved details

- No inferred battery percentage. Water-usage factors differ between references; for HTV345FRF, the client uses 0.1 L per count based on the owner comparison below. It does not copy the primary reference's speculative 1/500 gallon factor or extend this conversion to other models.
- No ASCII state interpretation: implementations differ, and treating every nonzero status as open can misread latched state flags. Unsupported formats are explicit.
- No automatic retry of watering commands. A transport error can happen after the device accepts a request.
- Normal manual watering is limited to 60..43200 whole seconds. Page 21 (PDF page 22) of the exact manufacturer manual specifies one minute to 12 hours. The one-second range belongs to misting, a separate mode. Earlier 10-/30-second normal-mode tests showed cloud transitions but the owner saw no water; those are not successful hardware tests.
- Explicit login, token refresh and logout are available. Refresh uses the verified /auth/basic/app/token/refresh contract and requires the server-provided expiry; no guessed lifetime is used. Optional automatic re-login is limited by the caller-supplied [recovery policy](SESSION-RECOVERY.md). Server throttling blocks immediate repeat logins.
- Missing readings do not imply closed valves or prove an offline hub.

The manufacturer's [manual listing for the exact HTV345FRF + HWG023WBRF kit](https://service.rainpointonline.com/hc/en-us/articles/16833887472911-Five-Language-User-Manual-EN-DE-FR-ES-IT) corroborates the hardware pairing; it is not API documentation.

## Hardware evidence and remaining validation

On 23 September 2026, the read-only probe passed on net472 and net10.0 against the owner account. The cloud returned HWG023WBRF-V2 and HTV345FRF, and the binary decoder produced three closed zones, RSSI -65 dBm and zero configured durations. No watering commands were sent. Credentials, account names and device identifiers are not recorded here.

Remaining evidence: further app comparisons over different usage amounts, independent physical volume measurement if accuracy is required, and session behavior. Zones 2 and 3 have not been actuated; only zone 1 is currently authorized for hardware tests. Additional models require their own validation before enabling decoding or actuation.

Hardware follow-up: normal irrigation below one minute was rejected at the client boundary after checking the exact manufacturer manual. The owner visually confirmed a 60-second .NET 10 run starting and automatically stopping, and a net472 start/explicit-stop sequence. Both cloud read endpoints can be stale, and the owner confirmed the official app shares this lag. Adopting batch polling is alignment with the primary references, not a claim to have fixed upstream state latency. See [the full test record](ZONE1-LIVE-TEST.md).

## HTV345FRF water-usage comparison

On 23 September 2026 the owner reported the app's zone 1 **last usage as 1.4 L**. A subsequent read-only request at 16:32:42 UTC returned **14 counts**, closed state and zero configured duration; its cloud data-change time was 16:31:01 UTC. This supports 0.1 L per count for the supported timer, exposed as nullable decimal `LastWaterUsageLitres`. The underlying count remains available for diagnostics. Missing or invalid-width usage remains unknown, distinct from a reported zero.

This is a single rounded app comparison without a unique watering-run identifier; it does not independently prove volume accuracy or establish calibration for other models. No new watering command was sent for this comparison. Last usage is not instantaneous flow or an accumulating meter. Daily/monthly totals and watering-event history were subsequently implemented from the vendor app contract; see [history evidence](HISTORY.md).

### Reference cross-check for feedback

Use reference implementations as protocol evidence, with their assumptions and model coverage kept explicit:

- `brettmeyerowitz/homeassistant-homgar` at the commit listed above: `custom_components/homgar/decoder.py`, `_dec_last_usage` calls `_vol`, which divides by 10 for litres. This supports the selected conversion independently of the app observation.
- `rathga/homgarapi` at `f1f7d643a0771f6d6a8c89c14732f64613009d1a` on `hwg023-control-and-model288`: `homgarapi/devices.py`, `ZonePortStatus.last_usage_dl` documents 0.1 L units for related timer models. This is corroborating evidence, not a claim that all those models are supported here.
- `funkadelic/ha-rainpoint` at the commit listed above: `api/decoders.py` instead uses 1/500 gallon/count based on one app comparison on related hardware and explicitly acknowledges alternative factors. That conversion is not adopted for this timer.
- The primary reference decodes packed zone event times as local wall-clock end times without timezone information; the rathga comment instead calls the event value a device-relative start. The C# API preserves this uncertainty with neutral event-time naming and local DateTime values whose Kind is Unspecified.
- Battery is a condition flag. The primary reference maps its healthy flag to 100 for display while acknowledging it has no measured charge-level mapping. The client does not expose that display convention as measured percentage.
- Alarm fields are identified, but the primary reference says captured normal-operation frames do not establish fault-code meanings. Unknown codes must not be assigned invented labels.
- Flow-rate and cumulative-usage decoders exist for other product datapoints. They are not proof that HTV345FRF reports those datapoints. No dedicated HTV345FRF watering-history endpoint was identified in those inspected Python client API paths; Home Assistant's own sensor history is not the manufacturer's history service. Subsequent native Android and vendor chart inspection established the separate [history endpoints](HISTORY.md), now implemented and tested.

The references guide implementation; hardware comparisons resolve relevant conflicts. Neither is treated as infallible, and unknown readings remain unknown.

## Vendor-app schedule evidence

The later [schedule reading and editing implementation](SCHEDULES.md) uses the common RF timer parameter and plan layout inspected in RainPoint Home 1.19.1065. That document records package provenance, layout, typed API boundaries, offline fixtures and the limits of live validation. The Python integrations' Home Assistant schedules are not substituted for vendor saved plans.

The volume-limit follow-up inspected common RF model 541 and the plan screen from the same package. All three modes use a 16-bit little-endian decilitre word; litre entry clamps to 0.3..6000. Plan help specifies volume-or-duration termination. The client follows these contracts while retaining full-range decoding, independent duration guards and explicit unverified physical enforcement. See [volume-limit evidence](SCHEDULES.md#optional-volume-limits).

Later vendor-app inspection established the timer alarm bit meanings and sensor-setting layout; see [sensor and alarm evidence](SENSORS-AND-ALARMS.md). For the separate reflashed-gateway research lead and the limits of router redirection, see [local control](LOCAL-CONTROL-RESEARCH.md).
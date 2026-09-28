# Hub RF receive channel

`SetRfChannelAsync(hub, channel, cancellationToken)` supports channels 1, 2 and 3 on HWG023WBRF / HWG023WBRF-V2. Supply a newly discovered hub whose current channel is known. The method reads again, checks identity and expected channel, and writes only the requested channel. It does not rewrite timer settings or plans. A snapshot permits one write attempt; after an uncertain outcome, rediscover and reconcile. No command is automatically replayed. Requesting the already reported channel performs a fresh check without a write.

The vendor app's native gateway settings use a three-choice picker and submit attributed `mid` and `recich` fields to `/app/device/main/update`. This was traced through `GatewaySettingsFragment`, its channel callback, the picker and the HTTP interface in RainPoint Home 1.19.1065. Package provenance is in [protocol sources](PROTOCOL-SOURCES.md). Proprietary source is not distributed. Cloud acceptance and matching read-back do not confirm RF reception, and changing a channel can interrupt paired-device communication. The fresh-read guard is not an atomic server-side compare-and-swap.

The Windows **Hub and firmware** tab provides a channel selector and save control after loading hub settings. Unknown channels, unchanged selections, busy operations and failed/mismatched writes cannot be saved again without the appropriate reload. Selection/account changes clear the draft.

Offline tests on net472/net10 cover all channels, exact two-field requests, invalid/unknown channels, changed identity, stale channel, no-op, cancellation, uncertain failures and snapshot reuse. Dashboard and actual WPF tests cover validation, read-back, failed-write gating and selection changes. The full suite passes 914 portable and 67 WPF cases per framework (1,962 total); artifacts are under `artifacts/rf-channel-offline`.

The explicit `RfChannelLiveTests.ChangesAndRestoresHubChannel` passed sequentially on net10.0 and net472 on 24 September 2026. Each run required one paired supported timer, all three zones reporting idle and empty plan lists. It changed cloud channel 1 to 2, read it back and restored channel 1, leaving timer configuration unchanged. No watering command was sent. Both recovery journals were removed after successful restoration. Results are under `artifacts/rf-channel-live`. This verifies the cloud write/restoration contract, not independent RF reception on channel 2.

Live tests require private `RAINPOINT_LIVE_SETTINGS` and `RAINPOINT_LIVE_RF=change-and-restore`. They journal identity and original/target channels before writing, restore in the same session with a separate timeout, and refuse to overwrite an unexpected channel. `RestoreJournaledChannel` is an explicit recovery entry point. Run through NUnit/TestAdapter, sequentially between frameworks with a gap between logins.

## RF research, 25 September 2026

The RF link is in the 433 MHz family. The [FCC filing for 2AWDBHWG023WRF](https://fccid.io/2AWDBHWG023WRF) lists a 433.92 MHz transmitter; its model documentation includes HWG023WBRF-V2. This transmitter listing does not establish the receiver tuning or map the three app settings. The [manufacturer manual for the HWG023WBRF kit](https://service.rainpointonline.com/hc/en-us/article_attachments/16776664689423), printed page 18, also identifies the timer-to-hub link as 433 MHz.

More specific evidence comes from the [rtl_433 Bresser/HomGar garden decoder at commit 308fd14e618bcb2acb947ae2c53ec7590cdff2d1](https://github.com/merbanan/rtl_433/blob/308fd14e618bcb2acb947ae2c53ec7590cdff2d1/src/devices/bresser_garden.c). Its author reports measurements on 23 July 2026, cross-checked using two SDR receivers. These concern related HWS388WRF-V7 / HTV103FRF hardware, not an independently verified HWG023WBRF-V2 / HTV345FRF combination. The [hardware research discussion](https://github.com/merbanan/rtl_433/pull/3621) and [decoder follow-up](https://github.com/merbanan/rtl_433/pull/3634) give the provenance.

| RF path on that reference hardware | Channel 1 | Channel 2 | Channel 3 |
| --- | --- | --- | --- |
| Timer to hub | approximately 433.17 MHz | approximately 434.68 MHz | approximately 433.24 MHz |
| Hub to timer | approximately 433.69 MHz | approximately 433.70 MHz | approximately 433.69 MHz |

These are measured FSK center frequencies with stated uncertainty of roughly +/-40 kHz, not manufacturer specifications for the supported kit. The decoder reports that the selector changes the timer-to-hub uplink while the downlink remains fixed. This explains how all three settings can belong to the nominal 433 MHz band while still selecting different receive frequencies. Do not hard-code this mapping into the client without captures from the supported hardware.

The same source identifies an RF configuration message (type 0x20, field 0x04) carrying the new channel, followed by a timer acknowledgement (0xa0). This provides evidence for coordinated switching of an already paired timer rather than a mandatory reset/re-pair procedure on the reference hardware. Automatic following and recovery from a missed change remain unverified for HTV345FRF. The inspected vendor-app callback submits the hub update; it does not itself implement a timer re-pair flow.

A different uplink frequency could avoid narrowband interference affecting timer feedback. That is an engineering inference, not a demonstrated range improvement on this kit; it would not remove interference on a fixed downlink. The manufacturer HIS019WRF [manual, Device Settings](https://fcc.report/FCC-ID/2AWDBHIS019WRF/6767484.pdf) presents Receive Channel as a connection-troubleshooting setting for sub-devices, but that manual concerns a related display hub.

The inspected Python references do not establish RF switching: funkadelic/ha-rainpoint rejects channel writes as unsupported, while brettmeyerowitz/homeassistant-homgar only changes its local selector state. Their channel UI is not hardware evidence.

To verify the supported kit, capture RF before and after a controlled channel change, correlate the configuration message/acknowledgement and fresh timer packets on the new frequency, and verify continued hub reception. Fresh device-originated feedback would demonstrate continued communication; direct RF capture would establish the frequency mapping. Cloud channel read-back alone proves neither. No channel change, valve command or pairing operation was performed during this research. The rtl_433 implementation is GPL-licensed; it was inspected as a protocol reference, and no implementation code was copied into this client.

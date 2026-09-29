# One-, two- and three-zone RF timer compatibility

Version 1.2.0 recognizes HTV145FRF (one zone), HTV245FRF (two zones) and HTV345FRF (three zones). Discovery remains available for other models, but timer operations require a recognized model. These additions have offline protocol-fixture coverage; only the owned HWG023WBRF-family hub with HTV345FRF has project hardware validation.

## App and protocol evidence

The previously inspected RainPoint Home 1.19.1065 app uses a shared timer UI driven by `portNumber`, passing a one-based port to its control and plan pages. Its common parameter model reads a single-zone configuration directly and separates multi-zone configurations with `|`; the per-zone plan reader is shared. Both legacy comma-separated plan records and newer slash-separated plan records use that common path. These observations establish shared app structure, not successful execution on unowned hardware.

Status framing has a material difference: HTV145FRF reference captures use `10#`, whose records have a structural field header without a datapoint ID. HTV245FRF and HTV345FRF use the existing datapoint-ID-prefixed format. The client now handles both structurally, rejects truncated records and keeps missing values unknown.

References at the revisions recorded in [protocol sources](PROTOCOL-SOURCES.md):

- [Single-zone captured open/closed frames](https://github.com/funkadelic/ha-rainpoint/blob/3b1492c62b70e6c194348bbfe68f58cc5920b568/tests/payload_samples.py), used to check state, duration, usage, battery, RF signal and report time.
- [Two-zone captured status](https://github.com/brettmeyerowitz/homeassistant-homgar/blob/0897690ea542bd61e4a58baf70b7962f53bfa0df/scripts/test_decoders.py), used to check both zones without inventing a third.
- The vendor app's shared control endpoint sends the hub identity, RF address and selected port; the ordinary start/stop request contract is unchanged.

## Implemented scope and limits

The added models participate in status reads, MQTT updates, normal timed start/stop, zone names and saved-plan reads. Normal control retains the conservative 60-second to 12-hour range. Usage uses the reference 0.1-litre scale; it has not been independently measured on the added models. Unknown/legacy ASCII status formats remain explicit unsupported readings.

Manual misting/cycle commands and advanced configuration writes retain their HTV345FRF gates. Sharing a plan container does not establish all firmware feature limits. Pairing/removal and sensor association support are unchanged. This update does not claim full feature parity or hardware-tested support for HTV145FRF/HTV245FRF.

Offline NUnit coverage includes the two captured single-zone states, a two-zone frame, malformed compact records, MQTT discovery, zone-bound command rejection and shared per-zone plan containers on net472 and net10.0.
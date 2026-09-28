# Attribution and upstream references

Copyright © 2026 Neil Colvin. RainPointClient is licensed under the [MIT License](LICENSE).

RainPointClient is an independent C# implementation for RainPoint Home / Smart+ devices. This repository uses the following public projects as upstream references for protocol behavior, device support expectations, compatibility validation and documentation cross-checking.

## Upstream behavioral and compatibility references

| Project | Reviewed revision | Contribution to the protocol research |
| --- | --- | --- |
| [funkadelic/ha-rainpoint](https://github.com/funkadelic/ha-rainpoint) | `3b1492c62b70e6c194348bbfe68f58cc5920b568` | Primary reference for RainPoint app identity, authentication, discovery, control, structural status decoding and model metadata. Copyright (c) 2025 Brett Meyerowitz; (c) 2026 Norman Yee. |
| [brettmeyerowitz/homeassistant-homgar](https://github.com/brettmeyerowitz/homeassistant-homgar) | `0897690ea542bd61e4a58baf70b7962f53bfa0df` | Cross-checks for cloud requests, app identifiers, duration units, device families and MQTT. Copyright (c) 2025 Brett Meyerowitz. |
| [Remboooo/homgarapi](https://github.com/Remboooo/homgarapi) | `47841c3e686cabc97be6271dcb2d979bb59b82e6` | Earlier standalone discovery and status conventions. Copyright (c) 2023 Rembrand van Lakwijk. |
| [macher91/homgar-homeassistant](https://github.com/macher91/homgar-homeassistant) | `52786d05f6f048ca7d390abe1b2301a726e90001` | Additional MQTT, multi-zone and sensor reference material. Copyright (c) 2023 Rembrand van Lakwijk. |
| [rathga/rainpoint-ha](https://github.com/rathga/rainpoint-ha) | `1ce749a99a0b6f273784a8caa3dca920d504133e` | Related two-zone REST behavior and water-usage cross-checks. Copyright (c) 2026 Richard Davies. |

The reviewed projects carry MIT licenses. Their copyright and permission texts are preserved in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The upstream projects retain their own copyright and license terms. RainPointClient's independently written source is licensed under the MIT License in this repository.

Authentication, discovery and control research informs `src/RainPointClient/RainPointCloudClient.cs` and its partial-class files. Status and push research informs `src/RainPointClient/Protocol/TimerDecoder.cs`, `PushDecoder.cs`, `WireModels.cs` and related transport models. MQTT research informs `MqttObserverTransport.cs` and `ObserverModels.cs`. These links identify protocol and compatibility reference material. [The protocol assessment](docs/PROTOCOL-SOURCES.md) records deliberate differences and hardware evidence; [the parity inventory](docs/UPSTREAM-PARITY.md) distinguishes implemented behavior from reference-only capabilities.

## Other reference material

- RainPoint's [manual for the HTV345FRF / HWG023WBRF kit](https://service.rainpointonline.com/hc/en-us/articles/16833887472911-Five-Language-User-Manual-EN-DE-FR-ES-IT), official app behavior and observations of the owned kit informed model limits and additional APIs. This material is referenced for interoperability, not claimed as project-owned content.
- [Technerd-SG/hassio-diivoo2mqtt](https://github.com/Technerd-SG/hassio-diivoo2mqtt) was assessed during separate local-protocol research. It is not a shipped local-control implementation or evidence of local control on the supported RainPoint kit; see [the research notes](docs/LOCAL-CONTROL-RESEARCH.md).

## Distributed dependencies

MQTTnet is copyright (c) .NET Foundation and Contributors and MIT-licensed. Microsoft runtime support libraries retain their own license and third-party notices. Their license texts are included in `licenses/`; the Windows ZIPs include the applicable runtime assemblies and those notices. NuGet resolves package dependencies separately. The public CA certificate in `Protocol/AliyunIoTRoot.pem` is a third-party server-verification trust anchor; the project's source headers do not assert ownership of it.

## Trademarks and disclaimer

RainPoint, HomGar and other product names are trademarks of their respective owners and are used only to describe compatibility. RainPointClient is independent and unofficial; it is not affiliated with, endorsed by, sponsored by or approved by those owners. The software is provided "AS IS", without warranty, under the MIT License. Cloud services and device behavior can change independently of this project. A successful command response does not guarantee valve operation; applications must account for delayed or unavailable feedback.

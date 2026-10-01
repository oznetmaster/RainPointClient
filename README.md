# RainPointClient

Pure **C# 14** client for the **RainPoint Home / Smart+ cloud**, targeting **.NET Framework 4.7.2** and **.NET 10**. Version **1.2.1** provides normal timed control, status/MQTT decoding and saved-plan reads for **HTV145FRF one-zone** and **HTV245FRF two-zone** RF timers alongside **HTV345FRF three-zone** timers on **HWG023WBRF / HWG023WBRF-V2 hubs**. The added models have app/protocol-reference and offline coverage; only HTV345FRF has project hardware validation.

> **Trademarks and disclaimer:** RainPoint, HomGar and other product names are trademarks of their respective owners, used only to describe compatibility. This is an independent, unofficial project, not affiliated with, endorsed by, sponsored by or approved by those owners.

The library uses attributed System.Text.Json transport models and typed public APIs. You can optionally supply your own `HttpClient`.

## Install and documentation

Install version 1.2.1:

```powershell
dotnet add package RainPointClient --version 1.2.1
```

Read the [1.2.1 release notes](https://github.com/oznetmaster/RainPointClient/blob/v1.2.1/release-notes/v1.2.1.md), [changelog](https://github.com/oznetmaster/RainPointClient/blob/v1.2.1/CHANGELOG.md), [feature guides](https://github.com/oznetmaster/RainPointClient/tree/v1.2.1/docs) and [test guide](https://github.com/oznetmaster/RainPointClient/blob/v1.2.1/tests/README.md). The documentation site is [oznetmaster.github.io/RainPointClient](https://oznetmaster.github.io/RainPointClient/). Publication status and setup are tracked in [PUBLISHING.md](https://github.com/oznetmaster/RainPointClient/blob/v1.2.1/PUBLISHING.md).

## Capabilities

The full feature set below applies to HTV345FRF. For HTV145FRF/HTV245FRF, see [implemented scope and evidence](https://github.com/oznetmaster/RainPointClient/blob/v1.2.1/docs/TIMER-VARIANTS.md); advanced writes remain restricted.

- Login, logout, home/hub/device discovery, explicit token refresh and optional bounded session recovery without replaying commands.
- Individual control of **all three zones**: normal irrigation, misting and cycle-and-soak, with duration and firmware validation.
- MQTT push feedback with REST fallback, reconnection and timestamp reconciliation; acknowledgement remains separate from reported state.
- Assigned timer zone names and typed home-configuration change notifications. Applications can subscribe to `RainPointMonitor.ConfigurationChanged` and reread configuration; these notifications do not confirm valve status.
- Typed state, configured duration, last usage in litres, battery condition, RF signal, alarm flags and device-local report times.
- Normal, misting and cycle-and-soak saved plans; daily, selected-weekday, odd-day, even-day and interval recurrence; optional volume limits.
- Calendar and next-start projections, including reported home daylight-saving rules, seasonal duration display and rain-delay status.
- Per-zone duration/misting defaults, flow calibration, monthly seasonal adjustment and rain delay.
- Usage and event history, zone profiles and recommendations, sensor association and moisture-rule models.
- Home, room, member and invitation administration; account profile/password operations; RF pairing/removal APIs.
- Supported Smart Scene conditions/actions and paged execution history; weather forecasts with separately supplied signing access.
- Product metadata, firmware information, hub RF channel and time-broadcast operations.

Detailed contracts, examples and feature-specific evidence are in the [feature guides](https://github.com/oznetmaster/RainPointClient/tree/v1.2.1/docs). Hardware-dependent APIs are distinguished from physically validated behavior.

## Quick start

```csharp
using RainPointClient;
using System;
using System.Linq;

using var client = new RainPointCloudClient();
await client.LoginAsync(email, password, "44", cancellationToken);
var homes = await client.GetHomesAsync(cancellationToken);
var hubs = await client.GetHubsAsync(homes[0].Id, cancellationToken);
var hub = hubs[0];
var timer = hub.Devices.Single(device => device.Model == "HTV345FRF");
var status = await client.GetTimerStatusAsync(hub, timer.Address, cancellationToken);

// Only when the caller explicitly intends to water this zone:
await client.StartWateringAsync(hub, timer.Address, 1,
    TimeSpan.FromMinutes(1), cancellationToken);
await client.StopWateringAsync(hub, timer.Address, 1, cancellationToken);
await client.LogoutAsync(cancellationToken);
```

Select the actual home, hub and timer in production; first-item selection above is illustrative. The `areaCode` is the account's country calling code, not a region ID. A dedicated account invited to the same home allows the official app and client to remain signed in separately; another login to the same account can displace its existing session.

Cloud acceptance does not prove valve movement. Cloud state can lag, while fresh MQTT feedback and updated usage provide better evidence. Last usage is the most recent reported volume, not flow rate or a cumulative meter. No watering operation is automatically retried after an uncertain result. See [feedback semantics](https://github.com/oznetmaster/RainPointClient/blob/v1.2.1/docs/COMMAND-FEEDBACK.md), [MQTT monitoring](https://github.com/oznetmaster/RainPointClient/blob/v1.2.1/docs/MQTT-FEEDBACK.md) and [session recovery](https://github.com/oznetmaster/RainPointClient/blob/v1.2.1/docs/SESSION-RECOVERY.md).

## Windows reference app

The WPF workbench targets net472 and net10.0-windows. It exposes all-zone controls, plans, calendar, settings, history, administration, scenes and weather. Saved credentials are encrypted for the current Windows account; optional startup sign-in never arms or operates valves. The GitHub release workflow prepares framework-dependent ZIPs for both targets. The net472 app requires .NET Framework 4.7.2 or later; the modern app requires the .NET 10 Desktop Runtime. It is separate from the NuGet library package.

See the [Windows app guide](https://github.com/oznetmaster/RainPointClient/blob/v1.2.1/docs/WINDOWS-APP.md).

## Build and test

Use Windows and the stable .NET 10 SDK for the complete WPF solution, plus .NET Framework 4.7.2 or later to run net472 tests. The library itself is cross-platform on .NET 10. Follow the root `.editorconfig`.

```powershell
dotnet build RainPointClient.slnx -c Release
dotnet test tests/RainPointClient.Tests -c Release --no-build --filter "TestCategory!=Live"
dotnet test tests/RainPointClient.Desktop.Tests -c Release --no-build --filter "TestCategory!=Live"
```

The release baseline is **1,380 library/dashboard tests and 87 WPF tests per target: 2,934 desktop passes**. Tests use NUnit 5.0.0, NUnit3TestAdapter 6.3.0, Microsoft.NET.Test.Sdk 18.10.1 and NUnit.Analyzers 4.15.0. These are development dependencies, not package dependencies. Explicit live tests require private opt-in settings and are excluded from hosted workflows. Builds, package creation and documentation generation perform no live device operations.

Short live checks cover all-zone normal feedback, zone-1 cycle/misting delivery feedback, supported schedule recurrences, seasonal scaling, rain-delay suppression/resumption and a volume cutoff. Account, invitation, scene-history and configuration checks restored temporary state. The even-day execution fixture needed separate successful cleanup recovery after a read timeout; its failed result is retained. See the [validation ledger](https://github.com/oznetmaster/RainPointClient/blob/v1.2.1/docs/TODO.md) and [test instructions](https://github.com/oznetmaster/RainPointClient/blob/v1.2.1/tests/README.md).

## Supported scope and limits

This is a **cloud client**. Local gateway interception/replacement is separate research and is not a release prerequisite. Tuya-based products and additional timer/sensor families are outside the verified control scope. Other catalog entries may be discovered without being supported for control.

Cloud calls default to `https://region3.homgarus.com/`; alternative HTTPS origins are configurable. Natural long-term session expiry, sensor-dependent execution, initial hub provisioning and pairing/removal of the in-use kit remain unverified. No independent flow-meter calibration is claimed. Historical Once plans can be read, disabled and deleted; creating/enabling them is rejected after this timer failed execution checks.

## Attributions

This library was developed using upstream reference material for protocol behavior and compatibility from [funkadelic/ha-rainpoint](https://github.com/funkadelic/ha-rainpoint), [brettmeyerowitz/homeassistant-homgar](https://github.com/brettmeyerowitz/homeassistant-homgar), [Remboooo/homgarapi](https://github.com/Remboooo/homgarapi), [macher91/homgar-homeassistant](https://github.com/macher91/homgar-homeassistant) and [rathga/rainpoint-ha](https://github.com/rathga/rainpoint-ha).

See [ATTRIBUTIONS.md](https://github.com/oznetmaster/RainPointClient/blob/v1.2.1/ATTRIBUTIONS.md) for reviewed revisions and their roles, and [THIRD-PARTY-NOTICES.md](https://github.com/oznetmaster/RainPointClient/blob/v1.2.1/THIRD-PARTY-NOTICES.md) for retained upstream copyright and license texts. Dependency licenses and notices are included with the Windows downloads.

## Trademarks and disclaimer

RainPoint, HomGar and other product names belong to their respective owners. Names are used only to identify compatibility. This project is independent and unofficial, and has no affiliation, endorsement, sponsorship or approval from those owners.

The software is provided **AS IS**, without warranty, under the MIT License. Cloud availability, protocol compatibility and uninterrupted operation are not guaranteed. Command acceptance and reported usage do not establish physical valve movement or calibrated volume; applications must handle delayed or unavailable feedback.

## License

Copyright © 2026 Neil Colvin. Licensed under the [MIT License](https://github.com/oznetmaster/RainPointClient/blob/v1.2.1/LICENSE). Third-party components retain their respective copyrights and licenses.
## NUnit 5 test tooling

All maintained NUnit suites use the official NUnit 5.0.0 framework. Async exception assertions are awaited, and discarded-task warnings fail test builds. Processor test packages use CrestronHomeNUnit SDK 2.2.0; workflow and Android suites, where provided, use the released 2.2.0 adapter. Tests remain available in Visual Studio, VS Code and the command line. Live and manual tests still require their documented devices and permissions. This is a test-tooling update; the published product version and runtime behavior are unchanged.

## NUnit 5 test package

Test package **1.0.0** uses **NUnit 5.0.0**. It is independent of the product version. [Download package](https://github.com/oznetmaster/RainPointClient/releases/download/v1.2.1/RainPointClient.ProcessorTests-1.0.0.pkg), [documentation](https://github.com/oznetmaster/RainPointClient/releases/download/v1.2.1/RainPointClient.ProcessorTests-1.0.0-Documentation.zip), [validation](https://github.com/oznetmaster/RainPointClient/releases/download/v1.2.1/RainPointClient.ProcessorTests-1.0.0.validation.json), [exact source revisions](https://github.com/oznetmaster/RainPointClient/releases/download/v1.2.1/RainPointClient.ProcessorTests-1.0.0.sources.json), and [SHA-256 checksums](https://github.com/oznetmaster/RainPointClient/releases/download/v1.2.1/RainPointClient.ProcessorTests-1.0.0-SHA256SUMS.txt) are attached to the existing product release. No product binary or NuGet version changed for this test update.

Validated on 1 October 2026: 1399 offline cases passed in each of two runs from the packaged assembly on Windows. All suite identities were checked against source discovery. Live/manual tests and execution on the processor were not repeated during this migration; earlier hardware results do not certify this new package.

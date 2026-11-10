# RainPointClient 1.2.2

This patch corrects the audited reliability issues while retaining the existing public API and supported frameworks.

- Timer configuration edits serialize the complete revalidation and full-record write across zones and snapshots. A queued edit based on older configuration is rejected before submission; read the timer again before editing. A session change during validation also rejects the edit. Commands are not automatically replayed, and cloud acceptance is not physical-device confirmation.

## Validation

- Offline workstation tests passed in Debug and Release on .NET 10 and .NET Framework 4.7.2: RainPoint net10.0: 1400; RainPoint net472: 1400. No failures or skips.
- 112 selected tests passed in the shared 374-case processor run on Mono 6.12.0.107 (32-bit), with no failures or skips. The merged host's resolved dependency graph was tested; these are not eight independent release-package tests.
- Fake transports and credentials exercised ordering, cancellation, shutdown and failure recovery. No new household-device or cloud control was performed. Pairing storage is unchanged.
- Strict XML, formatting and style checks passed. Package and companion-app preparation is verified separately by the shared release pipeline.

See [the changelog](https://github.com/oznetmaster/RainPointClient/blob/v1.2.2/CHANGELOG.md) and [validation provenance](https://github.com/oznetmaster/RainPointClient/blob/v1.2.2/release-validation/v1.2.2.json).

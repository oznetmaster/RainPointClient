# RainPointClient publication

## Current release

Version **1.0.1** contains documentation updates. No functional changes. Consult the [GitHub release](https://github.com/oznetmaster/RainPointClient/releases/tag/v1.0.1), [workflow runs](https://github.com/oznetmaster/RainPointClient/actions) and [NuGet package](https://www.nuget.org/packages/RainPointClient/1.0.1) for publication status.

## Release configuration

Version **1.0.0** is the initial cloud release. Source and version tags are published from `main` in [oznetmaster/RainPointClient](https://github.com/oznetmaster/RainPointClient); the local remote is `origin` (`https://github.com/oznetmaster/RainPointClient.git`). GitHub Pages uses GitHub Actions. Repository variable `NUGET_USER` is `oznetmaster`, verified against the NuGet profile. The `release` environment accepts only `v*` tags.

The NuGet Trusted Publishing policy `RainPointClient` is active, bound to GitHub owner `oznetmaster` (ID `5798625`) and repository `RainPointClient` (ID `1391996680`), workflow `dotnet-publish.yml`, environment `release`. It permits only publishing the `RainPointClient` package and new versions; unlist/relist access is not granted. The first hosted release run verifies the OIDC credential exchange. Consult the [release](https://github.com/oznetmaster/RainPointClient/releases/tag/v1.0.0), [workflow runs](https://github.com/oznetmaster/RainPointClient/actions) and [NuGet package](https://www.nuget.org/packages/RainPointClient/1.0.0) for publication results.

The release is for the cloud client. Local-protocol research, additional hardware and natural long-term expiry observations are not prerequisites for the supported cloud release. See [scope and evidence](docs/TODO.md).

## Workflow comparison

The local workflows were compared with the released OverkizClient 2.0.0, AppleTVControlLibrary 2.2.6, KasaTapoClient 2.0.1 and TeslaPowerwallLibrary 2.0.0 repositories. The first three are the direct models used during this project; the fourth confirms the independent-library documentation boundary.

| Area | Established pattern | RainPoint setup |
| --- | --- | --- |
| Offline validation | Windows, stable .NET 10, NUnit/adapters, net472 and net10.0, retained TRX | `unit-tests.yml`, including both WPF targets and release-policy checks |
| Release trigger | `v*` tags and manual version input | `dotnet-publish.yml`; both paths check out the existing requested tag |
| Release gates | Exact-commit hosted workflows plus configured App-bound external checks | Shared `Wait-RequiredReleaseChecks.ps1` and its regression suite; both unit and documentation workflows required |
| Hardware override | Manual-only, explicit reason; hosted checks still mandatory | Same policy; an override is recorded as unverified hardware, never a pass |
| NuGet | Stable package version, release build/pack, repository secret, duplicate-safe push | GitHub OIDC through `NuGet/login@v1`; metadata/version/package inspection and all assets prepared before any upload |
| GitHub release | Reviewed notes, package/library assets and reference-app ZIPs | Versioned notes, nupkg, both DLL/XML targets, both Windows ZIPs and SHA-256 manifest |
| Documentation | DocFX API/guides, Pages artifact and deployment environment | Pinned local DocFX 2.75.3 from the Overkiz model; PR build only, deployment only from default branch |
| Post-release | Explicit default-branch validation dispatch | `post-release-validation.yml` dispatches unit and documentation workflows |

Deliberate improvements over older templates: manual publication cannot build an arbitrary branch under a release version; secrets and user inputs enter PowerShell through environment variables; package contents and documentation are checked before upload; manual and tag runs both create/update the matching GitHub release; documentation permissions are limited to the deployment job. NuGet authentication now follows the requested Trusted Publishing approach instead of the older models' stored API key. A duplicate NuGet response cannot prove that remote bytes match a rebuilt package, so the post-publication download check below remains required.

## Repository setup before first publication

1. Repository creation and local `origin` connection are complete. Establish `main` as the remote default branch with the first reviewed source push. If a different owner/name is chosen, update package URLs, README/release-note links and documentation metadata before packing.
2. Review and commit the intended source, docs, scripts and workflows. Exclude `.local`, artifacts, generated API/site files, private captures and credentials. Do not publish private protocol inspection files.
3. **GitHub Pages → Source: GitHub Actions** is configured. Confirm its `github-pages` environment and successful deployment after the first source push. The documentation workflow has the required Pages/OIDC permissions and build dependency. See [GitHub Pages guidance](https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages).
4. The [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) policy is active under NuGet account `oznetmaster`: repository owner `oznetmaster`, repository `RainPointClient`, workflow file `dotnet-publish.yml`, environment `release`. Its exact package pattern is `RainPointClient`, with permission to publish a new package and new versions. GitHub repository variable `NUGET_USER` is set to the verified NuGet profile username `oznetmaster` (not an email address). The release job requests `id-token: write` and uses `NuGet/login@v1` just before pushing; its temporary API key is passed directly between workflow steps. No long-lived `NUGET_API_KEY` secret is required. The release environment permits `v*` tags; manual dispatch must select that existing tag as its workflow ref as well as entering the matching version.
5. If external processor results should gate publication, set repository variable `RELEASE_REQUIRED_CHECKS` to a JSON array of exact check names and trusted positive numeric App IDs. Do not invent an App ID. This repository does not dispatch device tests from hosted workflows; local evidence alone is not a GitHub check on the release commit.
6. Push the reviewed default-branch commit and require successful `unit-tests.yml` and `github-pages.yml` runs for that exact commit. Confirm the Pages site is accessible. Manual workflows must exist on the default branch; see [GitHub manual-dispatch guidance](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow).

## Local preflight

Use PowerShell 7, the stable .NET 10 SDK, .NET Framework 4.7.2 or later, and the .NET 8 runtime for pinned DocFX 2.75.3. The documentation and publication workflows explicitly install SDKs 8 and 10; the client targets remain net472 and net10.0.

From the repository root in PowerShell:

```powershell
./tools/Test-ReleasePreparation.ps1
dotnet test tests/RainPointClient.Tests -c Release --filter "TestCategory!=Live"
dotnet test tests/RainPointClient.Desktop.Tests -c Release --filter "TestCategory!=Live"
./tools/Build-Documentation.ps1
./tools/New-ReleaseAssets.ps1 -Version 1.0.1 -OutputDirectory artifacts/release-preview
```

Choose a new empty output directory for each asset run. The asset script never uploads or tags. Review its NuGet archive, README, versioned notes, XML documentation, framework-dependent Windows ZIPs and SHA-256 manifest. The package must contain only the reviewed stable direct dependencies (MQTTnet 4.3.7.1207 on both targets and System.Text.Json 10.0.12 on net472; .NET 10 uses its built-in System.Text.Json), with no test, desktop, private-settings or capture files. XML API docs are emitted for both targets; missing public-member comments are suppressed separately from other compiler warnings. DocFX reflects the Release net472 assembly to avoid relying on its older source parser for C# 14.

The explicit pack items include the MIT license and upstream notices. NuGet's embedded README uses absolute versioned GitHub links so they work on the package page. See [NuGet package README guidance](https://learn.microsoft.com/en-us/nuget/reference/msbuild-targets#packagereadmefile) and [DocFX assembly metadata guidance](https://dotnet.github.io/docfx/docs/dotnet-api-docs.html).

## Publish after approval

Confirm `Directory.Build.props`, package release-note URL, changelog and `release-notes/v1.0.1.md` match. Review all embedded release documents and the release date before the final commit. Create `v1.0.1` on the validated commit and push that tag, or manually dispatch `dotnet-publish.yml` for the already existing tag. Do not move an existing published tag.

The workflow verifies exact-tag metadata and checks, runs all offline tests, builds the documentation and every asset, validates the nupkg, retains assets, then pushes NuGet and creates the GitHub release from the versioned notes. No RainPoint login or hardware command is permitted in these jobs. A failed pre-publication step prevents upload. NuGet and GitHub publication are separate external operations; a partial failure must be reconciled against the existing tag and immutable package, not silently replaced.

## Verify the public result

After NuGet indexing, download the package into a fresh package cache. Verify package ID/version, target assets, dependency versions, README/license/notes and the repository commit. Compare GitHub asset hashes with `SHA256SUMS.txt`; compare the package payload with the reviewed archive, allowing for NuGet's repository signature. Confirm the GitHub release has the reviewed notes and both Windows ZIPs, and that the API and guide pages are accessible. Record actual hosted run/release URLs and results only after they exist.

NuGet versions are immutable. Correct documentation before upload; a later source or GitHub-note edit does not replace the package's embedded README. A changed package requires a new version. Do not treat `--skip-duplicate` as verification of the existing public package.

## Local preparation validation — 28 September 2026

- Release solution build: passed with zero warnings/errors.
- Offline NUnit: 1,352 library/dashboard tests on net472 and 1,352 on net10.0; 87 WPF tests on net472 and 87 on net10.0-windows. All 2,878 passed, with no skipped tests. TRX files are retained locally under `artifacts/release-tests/`.
- Release policy: 40 inherited policy scenarios and six metadata rejection scenarios passed. The two shared release-check scripts match the OverkizClient model byte-for-byte.
- All four workflow files passed actionlint 1.7.12; the official binary was checked against its published SHA-256 manifest. Local PowerShell scripts passed syntax parsing.
- Clean DocFX metadata and site builds passed with zero warnings/errors. API, release notes, test guide and feature guides were generated; all 130 generated HTML files passed a local resource/link check. Framework references are staged only in ignored documentation output and never shipped as runtime assemblies.
- `artifacts/release-preview-2/` contains the inspected 1.0.0 nupkg, both DLL/XML targets, both Windows ZIPs, release documents and SHA-256 manifest. Package dependency groups match their respective runtimes; both ZIPs include dependency licenses/notices, and all release asset hashes matched the manifest.
- A separate consumer restored this local package into a new empty cache, compiled the README example for both targets with zero warnings/errors, and loaded version 1.0.0.0 successfully on net472 and net10.0. No cloud calls were made. This proves the local candidate, not public NuGet availability.

The remote repository and Pages source are now configured. Hosted runs, exact-tag publication checks, Pages deployment, NuGet upload/indexing and public-download verification are performed during publication. No commit, tag, push or external publication was performed during preparation. Existing live/processor evidence remains in the feature validation ledger; those tests were not rerun for these documentation, packaging and workflow changes.

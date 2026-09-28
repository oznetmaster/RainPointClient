# RainPoint Windows client workbench

The WPF reference app uses the same typed RainPointClient public API as other consumers. It targets **net472 and net10.0-windows**, uses C# 14 and follows the solution's `.editorconfig`. The app and its presentation project are not NuGet packages. No additional runtime packages were introduced for the UI.

## Build and run

Open `RainPointClient.slnx` in Visual Studio and select `RainPointClient.Desktop` as the startup project. Choose either target framework. Or, from the repository root:

```powershell
dotnet run --project src/RainPointClient.Desktop -c Release -f net10.0-windows
```

For .NET Framework:

```powershell
dotnet run --project src/RainPointClient.Desktop -c Release -f net472
```

After a Release build, the executables are:

- `src/RainPointClient.Desktop/bin/Release/net10.0-windows/RainPointClient.Desktop.exe`
- `src/RainPointClient.Desktop/bin/Release/net472/RainPointClient.Desktop.exe`

Run from the complete build directory; the executable depends on the adjacent assemblies and runtime files.

## Use

1. Enter the RainPoint Home / Smart+ account email, password and numeric country calling code (for example `44`). Sign in explicitly. The app clears its password box when sign-in begins. Remember credentials and Sign in automatically at startup are selected by default; uncheck either option if unwanted. Credentials are saved only after authentication succeeds. The app does not import `.local` files. Signing in may displace the phone app's session on the same account.
2. Select the home, hub and supported timer. Refresh status to obtain hub connectivity, Wi-Fi signal, timer RF signal, battery condition, firmware versions and each zone's last reported state and water usage.
3. Optional polling reads status every 15 seconds. Operations do not overlap. A failed poll switches polling off; explicitly refresh or sign in as needed. Session renewal runs automatically when enabled and is also available explicitly. Startup sign-in makes one attempt when enabled; the optional saved-account recovery policy permits at most one later automatic login per worker.
4. To operate the selected timer, choose a zone and enable its controls. The initial duration is one minute; whole minutes from 1 to 720 are accepted. Stop remains available with an invalid start duration. Selecting another zone, timer or hub disarms the controls. All three zones are supported and tested with simulated responses; current live valve-opening tests remain restricted to zone 1.

The app distinguishes command acknowledgement from reported state. Optional [command-response feedback](COMMAND-FEEDBACK.md) appears in the control message, separately from monitored readings. A successful command does not change the displayed state until feedback is read. Cloud feedback can lag physical operation by 30 seconds or more. An uncertain command is never retried automatically. Closing the app or signing out does not stop an active run.

The last successful read time is separate from the cloud data-change timestamp and the timer's local report time. Missing readings display as unknown. Configured duration is not a countdown; last usage is not instantaneous flow or a cumulative meter. Battery is a condition, not a measured percentage. Alarm values have no unverified fault labels.

## Zone settings

The **Zone settings** tab reads default watering duration, misting on/off intervals and flow calibration through the client’s typed APIs. Select a timer and zone, then choose **Load / reset settings**. The last cloud read is displayed separately from the edit fields, with its own timestamp. Switching timer, hub, home or zone clears the previous settings. Status polling does not silently reload or overwrite settings edits.

All three zones can be edited. Choose one setting, edit its value and select **Save selected zone setting**. The write and read-back address the selected zone and preserve the other zones. Changing the setting or reloading discards unsaved edits. Saving sends no watering command and does not edit existing plans.

- Default duration accepts 1–720 whole minutes. Blank explicitly selects the app-default sentinel, displayed as 10 minutes.
- Misting intervals accept 5–3600 whole seconds each. Either field can be blank for its app default: 10 seconds on or 30 seconds off.
- Flow correction accepts whole percentages from -20 to +20. Zero is neutral; blank is invalid. Physical volume accuracy remains unverified.

Missing or unsupported settings are labelled accordingly and cannot be edited. They are distinct from an explicit app-default sentinel. Reads preserve legacy non-minute durations, but the current editor cannot save fractional minutes. The client enforces modern configuration and firmware requirements and rejects stale settings before writing.

After an accepted save, the app reads configuration once and reports whether the selected value matches. A delayed or failed read-back is not shown as a verified save. A failed, uncertain or mismatched operation disables further saves until an explicit reload; it is never automatically repeated. Closing cancels pending work but does not undo a setting already accepted by the cloud. Raw configuration and transport error details are never displayed.

These controls are covered by simulated HTTP and actual WPF binding tests on both frameworks. No live account or valve was used for this Windows UI addition. Earlier independent app comparisons and hardware restoration evidence belong to [zone defaults](ZONE-DEFAULTS.md) and [flow calibration](FLOW-CALIBRATION.md).

## Manual watering modes

The Live status tab offers Normal, Misting and Cycle and soak for zones 1–3. Duration and interval labels change units with the selected mode. Changing modes resets short defaults and disarms controls. Misting/cycle starts require reported timer firmware 120 or newer; normal operation is unchanged. Invalid timing blocks Start while Stop remains independent of timing inputs once controls are enabled. See [limits, units and evidence](MANUAL-MODES.md).

## Usage and event history

The **History** tab provides read-only history for the selected timer and zone (1–3). It does not start polling, save configuration or send valve commands. Changing device or zone clears the displayed history; changing usage or event filters clears that panel so results cannot be mistaken for the new query.

Choose Daily or Monthly water usage and enter inclusive `yyyy-MM-dd` home-calendar dates. Daily queries allow at most 30 days; monthly queries allow at most one year. The initial range uses the computer's current date and preceding 29 days; adjust it if the home's calendar differs. Dates are not converted through the computer's timezone. Partial-month queries can produce partial-month buckets.

Only returned buckets are shown. Missing amounts display Unknown, explicit zero remains zero, and absent days/months are not filled. The summary totals known amounts only and identifies unknown buckets; it does not claim complete consumption for the range. The date range and last successful read time remain visible. A failed new usage read clears old rows and shows a sanitized error.

Events can be filtered by type and optional cloud-time bounds in UTC (`yyyy-MM-dd HH:mm:ss`). Blank bounds are omitted. The event list is scoped to the selected timer/zone, not the entire home. Cloud UTC time and device-reported local time are separate columns. Selecting an event shows litres, seconds, timezone label, numeric work/control/exception codes, online state and operator when available. Unknown event codes remain numeric; uninterpreted details are identified without showing protocol payloads.

**Load / reset events** requests up to 50 events. **Load older** requests one more page using the oldest returned cloud timestamp. Pages are combined by event ID, sorted newest first and retain the selected event. Paging stops on an empty/short page, exhausted bounds, no progress or the 500-event display limit. Use a narrower time range to inspect another window. Timestamp ties and service retention mean paging is not a completeness guarantee. Failed older-page reads retain the existing rows and allow an explicit retry; there is no automatic retry or pagination loop.

The screens and navigation are covered by simulated responses and actual WPF controls on both frameworks. No live account was used for this UI addition. Earlier independent app comparison and live client evidence is recorded in [HISTORY.md](HISTORY.md).

## Saved plans

The **Plans** tab reads and edits saved plans for any of the three zones. Select a timer and zone, then choose **Load / reset plans**. Select an existing row to replace, enable, disable or delete it, or choose **New disabled plan**. New plans start disabled and each zone supports at most six plans. Enabling a plan permits scheduled watering even after the Windows app closes.

The editor supports normal irrigation, cycle and soak, and misting. It shows the appropriate duration, watering and pause units for each mode. Recurrence choices are Every day, Odd days, Even days, Selected weekdays and Every N days. Every N days requires an effective date; selected weekdays require at least one day. Dates and start times use the home's local calendar. All three modes permit an optional 0.3–6000 litre limit in 0.1 L steps; leave it blank for duration only. Input limits and cycle/repeat spacing are checked before saving. See [schedule semantics and limits](SCHEDULES.md).

Existing hourly plans remain viewable and can be enabled, disabled or deleted, but this editor cannot replace them. Unsupported timing is not silently converted into a different recurrence. Changing the selected plan, mode or zone, or reloading, discards unsaved edits. Changing timer, hub or home clears the plan list.

Each write uses the client's fresh-configuration check and preserves other zones. After acceptance, the app reads the selected zone once and compares the complete plan list with the intended change. A stale, uncertain, failed or mismatched operation requires an explicit reload; writes are never automatically retried. Cloud read-back confirms saved configuration, not RF delivery, scheduled execution or conflict-free watering. This UI work used simulated responses only and did not change hardware plans.

## Remaining Windows feature coverage

The Windows workbench should expose supported client capabilities as they are implemented and validated. It is not yet complete. Usage/event history, saved-plan editing with optional volume limits, seasonal adjustment, rain delay, hub tools and session recovery are implemented. Remaining work follows the [completion ledger](TODO.md), including automatic moisture rules and administration. The Windows display shares the client decoder and therefore does not replace independent vendor-app or physical validation.

## Live feedback

After selecting a hub, choose **Start live feedback**. The app connects the MQTT observer and continues polling every 30 seconds as a fallback. Its status line distinguishes connection, reconnection and authentication-required states. Each accepted timer reading shows its source and receipt time separately from the device timestamp. Older polls do not overwrite newer push readings.

**Stop live feedback** ends monitoring. Changing home/hub, signing in/out or closing also stops it and discards queued updates for the previous selection. The separate 15-second refresh option is suspended while monitoring. Session renewal remains explicit in this step. See [MQTT behavior and hardware evidence](MQTT-FEEDBACK.md).

## Saved credentials and startup

After your first successful sign-in with Remember credentials enabled, the app encrypts the email, password, country code and startup preference using Windows DPAPI with CurrentUser scope. It writes only encrypted bytes, including during atomic file replacement, to `%LOCALAPPDATA%\RainPointClient\Desktop\account.dat`. Both framework builds share this format and location. Windows account protection does not protect against other software already running as that same user; the data is not intended to be copied to another account or computer.

On the next launch, the app restores the account. If automatic sign-in is enabled it makes one login attempt, clears the password box and discovers homes. It does not select a timer, arm controls or water automatically. A failed automatic login leaves manual sign-in available and is not retried in a loop. If startup sign-in is disabled, the remembered fields are restored for manual sign-in. A blank password box can reuse the saved password only when the email and country still match the saved account.

- Uncheck Sign in automatically at startup to retain credentials without startup login. The preference is saved immediately for an existing saved account.
- Sign out disables automatic startup sign-in and ends the local session. Saved credentials remain available for a later manual sign-in.
- Uncheck Remember credentials to delete the saved account and disable automatic sign-in.
- Forget saved account deletes the saved file and clears the email/password fields. It leaves any current cloud session running; use Sign out to end that session as well.
- If you enable Remember credentials after signing in, enter the password and sign in once more to save it. Passwords are not retained by the client for later persistence.

Unreadable, corrupted or inaccessible storage produces a short message and does not crash the app or expose secret values. A storage failure is reported separately from authentication success. No plaintext fallback file, log entry or stored session token is created. Test fixtures use isolated temporary stores with fictitious credentials, never the user's real account file.
## Tests

Run the complete local suite using Visual Studio Test Explorer / NUnit3TestAdapter, or:

```powershell
dotnet test RainPointClient.slnx -c Release --logger trx
```

`RainPointClient.Tests` includes offline dashboard behavior tests for both library targets. `RainPointClient.Desktop.Tests` exercises actual WPF controls and bindings on both Windows targets, using simulated HTTP responses and offscreen rendering. It records PNG attachments alongside NUnit results and requires Windows with WPF installed. Discovery and these tests do not log in to a real account or command hardware.

The dependencies are NUnit 4.6.1, NUnit3TestAdapter 6.3.0, Microsoft.NET.Test.Sdk 18.10.1 and NUnit.Analyzers 4.15.0. The Windows test project marks them private. net472 uses Microsoft.NETFramework.ReferenceAssemblies 1.0.3 for compilation.

The Windows tests cover DPAPI storage, startup sign-in, account/device selection, status binding, all-zone start/stop controls, settings editors, history navigation and saved-plan creation/replacement/enablement/deletion through actual WPF controls. Tests include rendered PNG attachments. Dashboard cases cover all three plan modes and five writable recurrence types in every zone, input bounds, unrelated-zone preservation, stale/uncertain writes, read-back failures, selection resets and cancellation.

The complete offline suite passes **1,240 library/dashboard plus 80 Windows cases per framework (2,640 total)**. Latest results and screenshots are retained under ignored `artifacts/completion-final`; earlier results remain in their respective artifact directories. No live credentials, emulator or hardware are used by these tests. The zone-1 restriction applies to current physical valve-opening tests, not supported product functionality.

Dashboard tests cover selected-zone command addressing and disarming on zone changes, no optimistic state, missing feedback, failed reads, unknown command outcomes, operation serialization, selection disarming and cancellation during close.

These tests establish the tested desktop behavior. They do not replace an independent comparison with the official Android app. The Windows app uses the same client decoder, so agreement between its display and the library is not independent protocol evidence. The wider capability gaps remain tracked in [UPSTREAM-PARITY.md](UPSTREAM-PARITY.md).

## Seasonal, rain-delay and hub tools

In Zone settings, select any of the three zones and load a fresh snapshot. Seasonal adjustment presents all twelve months independently (whole percentages, 10–200). Rain delay takes `yyyy-MM-dd HH:mm:ss` in the home's local time; leave it blank to clear it. An unavailable field remains unavailable rather than becoming zero. Saves preserve unrelated configuration, then read the cloud back. A failed or mismatched read-back requires an explicit reload and does not replay the write.

Hub tools reads automatic time-broadcast settings and saves only when the setting was supplied. A one-shot broadcast reports cloud acceptance separately from RF delivery. Hub and timer firmware checks display installed/available versions and notes; no firmware installation is offered. RF receive-channel editing is available; see [RF channel](RF-CHANNEL.md). Load product catalog displays model variants without implying support for additional hardware.

Automatic token renewal is on by default after sign-in; **Allow one reconnect using saved credentials** is a separate, initially off option. This latter choice is not persisted. See [the recovery policy and lifecycle](SESSION-RECOVERY.md). Startup and recovery leave valve controls disarmed.


## Calendar

Choose Calendar, a zone and a home-local date, then Load / refresh calendar. The table displays enabled-plan starts on that date, seasonal calendar duration and rain-delay status. Next start searches from the selected day's midnight across at most 366 days; it is a projection, not a live countdown. Selecting another date uses the same timestamped snapshot without network activity. Reload after external changes. See [calendar semantics and limitations](CALENDAR.md).

RF-channel editing is now available in the Hub and firmware tab; see [behavior and validation](RF-CHANNEL.md). Latest offline results and hub screenshots are in `artifacts/rf-channel-offline`.

## Zone profiles

The Zone profile tab edits each zone’s recommendation preference and planting-area choices. Get recommendation shows the cloud interval and duration. Prepare disabled plan draft opens a local draft for review in Plans; nothing is saved or enabled automatically. See [profile behavior and evidence](ZONE-PROFILES.md).

The Zone settings tab also supports paired-soil-sensor association and moisture stop, and the status table labels known alarm flags. See [sensor and alarm behavior](SENSORS-AND-ALARMS.md).

The automatic low-moisture rule editor is below Zone settings. It uses its own zone selector and explicit load/save, defaults absent rules to disabled, and requires a paired-sensor association to enable. See [automatic watering](MOISTURE-WATERING.md).

## Home management, Weather and Smart Scenes

Home management adds home/room/member/invitation operations, hub/device rename, all-zone room assignments, notification flags, units/date format, currency and IANA time-zone controls. Read fresh details before edits and reload after an attempted save. Confirmation is required for destructive actions and changes that can affect access or schedules. See [administration semantics and live limits](HOME-ADMINISTRATION.md).

Weather is available when the host supplies separate weather-service signing access. It displays nullable current/hourly/daily readings and clears them after failed loads or home changes. See [configuration and live verification limits](WEATHER.md).

Smart Scenes provides listing/details, explicit edit/new drafts, enable/disable/delete and supported weather/time/notification/rain-delay definitions. Weekday and one-time inputs use the home's wall clock. Saving may activate the scene immediately; it is not a disabled watering-plan draft. Unsupported settings and hardware capabilities prevent writes. See [scene coverage and limits](SMART-SCENES.md).

## Account management, pairing and scene-history follow-up

The Account management tab supports [registration/recovery and profile/password operations](ACCOUNT-ADMINISTRATION.md). Password changes clear saved credentials before submission and require explicit sign-in afterwards. The Pairing tab provides [supported RF discovery, cancellation and removal](DEVICE-PAIRING.md), with confirmation before pairing/removal. Neither tab performs changes merely by loading.

Smart Scenes now includes daytime/nighttime effective windows and paged execution history with typed action-result details. UTC history dates are separate from home-local scene schedules. Known reported outcomes have typed labels; unknown result codes remain numeric. The complete offline run on 25 September 2026 passed 1,311 portable cases and 84 WPF cases per target (2,790 total), including the new account/pairing controls. These offline results do not establish live credential changes, pairing/removal or scene execution.


Automatic saved-credential recovery now waits two minutes before retrieving the saved password and attempting its single reconnect. The app reports the recovery worker's `CoolingDown` state during that interval. Turning recovery off or closing the app cancels the wait without reading the password or logging in. This addresses the service's rapid-login rejection; it does not introduce repeated login attempts or replay device commands. See [recovery behavior and evidence](SESSION-RECOVERY.md).


The Once correction adds all-zone guards: existing Once records remain visible with an explanation, cannot be edited or enabled, and can still be disabled/deleted. New drafts do not offer Once. Current offline validation is 1,320 portable plus 87 WPF cases per target (2,814 passes); see [live evidence and scope](SCHEDULES.md#daily-execution-passed-and-once-writes-restricted--25-september-2026).


The live zone table now distinguishes `Reported soaking (paused)` for cycle-and-soak state 7, verified against the RF app and a live pause/resumption sequence. The program remains active during that pause; it is not displayed as completed irrigation. See [cycle feedback evidence](MANUAL-MODES.md#cycle-soaking-feedback--25-september-2026).
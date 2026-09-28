# Smart Scenes

The client provides typed scene listing/detail, creation, complete replacement, enable/disable and deletion. A saved scene may become active immediately. There is no disabled-draft guarantee in the inspected save contract. Disabling a scene does not stop an already running valve. No scene is saved or enabled automatically by loading the Windows app or signing in.

## Supported definitions

- Weather thresholds: rain probability, Celsius temperature, humidity and wind speed in kilometres per hour, with less-than/equal/greater-than comparison.
- One-time home-local date/time, or daily, odd-date, even-date and selected-weekday time triggers. Repeating triggers support a clock time, sunrise or sunset.
- Weather-type conditions selected from the vendor dictionary, including multiple types in the client API.
- Notification actions with member IDs and/or email recipients; and per-zone rain-delay actions, subject to the device's advertised capabilities.
- Any/all condition matching, one to five conditions and actions, maximum daily runs (zero means unlimited), minimum interval, optional effective dates, daily/odd/even/weekday effective recurrence and optional daily clock or daytime/nighttime solar windows.

Use `RainPointSceneCondition` and `RainPointSceneAction` factories in a `RainPointSceneDraft`. The client handles binary encodings internally; no public raw JSON or encoded condition payload is required. Dates and times are home-local (`DateTimeKind.Unspecified`), not converted from the computer's time zone. Clock windows require both boundaries and cannot accompany time-trigger conditions. `SolarPeriod` selects daytime (sunrise to sunset) or nighttime (sunset to sunrise); it requires a home location and cannot be combined with clock boundaries or time-trigger conditions. Mixed unsupported boundary pairs are rejected rather than dropped.

`GetSceneDevicesAsync` combines discovery with the exact model/model-code catalog entry. Writes refresh capabilities and validate executing-hub identity, action targets, member recipients and weather/location requirements. A missing capability remains unknown, not presumed supported. The available HTV345FRF model advertises no scene-action support in its catalog entry, so attempts to target it are rejected even though ordinary manual and scheduled watering are supported. A scene-capable hub alone does not establish timer action support.

## Editing and uncertainty

`GetSceneAsync` verifies that the selected scene belongs to the home. `CreateDraft()` copies understood conditions, actions and effective settings. Unsupported components, disabled components or unsupported effective periods prevent copying rather than silently dropping them. Replacement is a complete definition, not a partial patch. Existing scene writes re-read the definition and consume one session-bound observation; an intervening detected change or competing write prevents submission. This is a client-side guard, not a server transaction.

After any uncertain create, rediscover scenes before attempting another create to avoid duplicates. After replacement, switch or delete, load fresh state. The Windows app exposes explicit New draft and Edit selected scene actions, local condition/action editing and confirmation before cloud writes. It clears the writable basis after an attempt and never displays a save acknowledgement as proof of physical execution or message delivery.

## Coverage and remaining contracts

Offline tests verify independent binary vectors, weekday ordering, signed temperature conversion, date bounds, catalog rejection, recipient scope, all-zone rain-delay addressing, stale/session guards, concurrent writes, one-attempt behavior and actual WPF weekday/one-time controls. Read-only live checks found empty scene lists and the hardware capability flags; no populated scene execution or CRUD was validated live. No notification was sent and no scene was written during these checks.

Device-status conditions and device-specific watering actions remain unimplemented for additional hardware families. Sunrise/sunset time triggers and daytime/nighttime effective windows are implemented. Unsupported vendor scenes can be inspected, switched or deleted explicitly, but cannot be copied into a supported editor definition. Additional device families require their own verified contracts and fixtures. This is not a claim of complete vendor-app scene parity.

## Execution history

`GetSceneHistoryAsync` reads a UTC instant range, with optional scene ID and zero-based pagination. It returns typed entries, trigger timestamps and nested action results, including action ID, hub, optional device address and the vendor result code. `RainPointSceneActionResult.DeviceAddress` is nullable: notification results may omit it, while an explicitly reported zero remains zero. The Windows action table displays an em dash for an unknown address. Missing action detail is null; an explicitly empty list is empty. `Outcome` maps bytecode-established meanings: reported success (`0`), failure (`1`), invalid (`100`), missing device (`101`), datapoint error (`103`), plan conflict (`104–106`), low power (`107`), too frequent (`108`) and timeout (`200`). Four-digit vendor errors remain a general `VendorError`; other unknown codes, including the unresolved specific meaning of `102`, remain `Unknown`. `ResultCode` always retains the original number. These are cloud/app outcomes, not physical confirmation. The nested wire string is deserialized only into attributed internal models.

The Windows Smart Scenes tab provides UTC date filters, selected-scene/all-scene history, next-page navigation and action detail. Changing the home, scene or date filter prevents paging through stale results. Offline tests cover pagination, malformed/ambiguous identities and results, missing versus empty data and WPF selection. Read-only live history queries passed on both targets on 25 September 2026; the returned lists were empty. Those initial empty reads did not validate populated history. Later notification evidence is recorded below; physical device-action execution remains unverified. Result meanings were checked against the original app bytecode because the high-level decompiler inverted several branches; synthetic tests cover known codes and unknown/boundary values.


### Notification scene live observation — 25 September 2026

The net10.0 explicit scene fixture verified creation and populated read-back, full replacement, enable/disable and final deletion using the existing hub. Its only action was one approved test email; it contained no timer or watering action. The recipient independently confirmed the email arrived.

The complete NUnit run failed during `GetSceneHistoryAsync` with a sanitized protocol exception. The fixture's original error handler retained the exception type but omitted its specific sanitized message, so that result cannot establish the cause. Cleanup still disabled/deleted the temporary scene, confirmed the original scene identities remained and removed its recovery journal. The failed TRX is retained under ignored `artifacts/scene-notification-live`.

Subsequent net472 read-only checks found no records, including a wider 24-hour scene query and an unfiltered home query. These observations do not prove whether deletion removed history or whether another reporting issue occurred. Retention after deletion is therefore not guaranteed by the client documentation. At that stage, populated history decoding and paging remained unverified; email delivery does not turn the failed full test into a pass.

The fixture now records the library's sanitized error message and privately captures only history responses before cleanup, never authentication responses or request headers. Any additional email requires its own explicit approval. The diagnostic change itself did not schedule another email.

After separate approval, the net472 fixture performed the same populated CRUD/switch checks and sent one additional email, independently confirmed received. It captured the successful history response privately before cleanup. The response contained one execution and one action with `aid`, `mid` and `rt: 0`, but no `addr`. The client incorrectly required `addr`, causing the retained live test to fail. Cleanup again disabled/deleted the temporary scene, verified the original scene identities and removed the journal.

The corrected attributed model accepts an absent or null device address without inventing zero. Action identity, executor and result remain required. Four new portable regression cases cover omitted/null/zero addresses and a missing required result. The exact captured response also passes through the corrected production client using an in-memory HTTP handler on both net472 and net10.0, with no network access or further email. Captures remain private and ignored; no raw payload is exposed publicly.

The full offline suite passes 1,324 library/dashboard and 87 WPF cases per runtime (2,822 total). Retained results are under `artifacts/notification-address-offline`, `artifacts/notification-address-wpf` and `artifacts/notification-history-replay`. This verifies the decoding fix against an observed response; it does not change either original live failure into a pass or establish server-side pagination. Deleted-scene retention and populated pagination remain unverified. Both approved email allowances have been used.

A later read-only net10 check of the second deleted notification scene also returned no records in narrow, 24-hour scene and home-wide queries. It ended inconclusive. An earlier attempt timed out at sign-in; both results are retained under ignored artifacts/scene-history-readonly. No additional notification was sent. The app's inspected history screen loads older records using page zero and the oldest trigger timestamp as the next end bound; that does not establish the server's numeric next-page semantics.

## Live pagination defect — 27 September 2026

A two-notification net10.0 run obtained two successful action results before cleanup. Against a frozen time window and a page size of one, the service returned the same first record for `pageNum=0` and `pageNum=1`, the second record for `pageNum=2`, and no records for `pageNum=3`. The client's zero-based public page was previously passed through unchanged, causing duplicated first-page results and premature `HasMore=false` on public page 1.

The client now translates public page `n` to server `pageNum=n+1`; public numbering remains zero-based. The largest overflowing page is rejected before network access. Portable regressions check the complete two-record traversal, termination and integer boundary. The original failing live test remains `artifacts/scene-pagination-live/two-emails-pagination_net10.0_20260927232448.trx`; its temporary scene was disabled/deleted and its journal removed. A successful corrected live check is recorded separately when available.


### Corrected live pagination — 27 September 2026

The corrected net10.0 run passed: two successful notification executions were returned as distinct public pages 0 and 1 with page size 1; pages 2 and 3 were empty. `HasMore` was true only on page 0. The public zero-based page number is now translated to the service's one-based page number. The temporary scene was disabled/deleted, original scene identities retained and the recovery journal removed. Result: `artifacts/scene-pagination-live/two-emails-pagination-corrected_net10.0_20260927235045.trx`. The original failed result remains retained. No watering action was included.
# Home, room, member and notification administration

The client exposes typed operations for home creation, rename, location, time zone, display preferences, deletion and leaving; room creation, rename, deletion and device/zone assignments; member listing, invitation, roles, removal and ownership transfer; and invitation acceptance/decline. Hub and child-device renaming are also available. These operations use the RainPoint Home cloud; supported RF pairing and removal are documented in [device pairing](DEVICE-PAIRING.md).

Read `GetHomeAsync` before a change. Its immutable `RainPointHomeDetails` observation belongs to the current session. An update checks a fresh home snapshot and allows one write attempt against the observation. Read again after success, failure or uncertainty. This detects an intervening change before the write; it is not an atomic server-side compare-and-swap and cannot prevent changes after that check. No failed write is automatically replayed. Member changes additionally recheck membership and relevant ownership constraints; the server remains responsible for authorization.

```csharp
var home = await client.GetHomeAsync(homeId, cancellationToken);
await client.RenameHomeAsync(home, "Garden", cancellationToken);
home = await client.GetHomeAsync(homeId, cancellationToken);
```

Room assignments use `RainPointRoomDevice`, identifying a discovered hub, optional child-device ID and optional zone. All three timer zones are supported. Encoded assignment strings remain internal. Recognized legacy assignments are resolved against fresh discovery; unknown or ambiguous assignments prevent replacement rather than being silently lost. Renaming does not re-pair a device or change its hardware identity.

Display preferences include 12/24-hour clock, temperature, length, volume, pressure and eleven known date formats. Unknown unit bits and trailing fields are preserved. An unknown date format remains null and is preserved when another unit changes. These are presentation preferences; typed readings retain their explicitly named canonical units. `GetTimeZonesAsync` returns the vendor IANA zone catalog, and `GetHomeOptionsAsync` returns typed currency and weather-type choices. Time zone and currency writes validate against a fresh catalog. Changing a home's time zone can affect schedules; it does not rewrite client dates into UTC.

`NotificationPreferences` is the account observation returned at sign-in. `SetNotificationPreferencesAsync` changes mobile/email flags while preserving unknown bits. It consumes that observation even if the result is uncertain. Sign in again to obtain a fresh value; cloud acceptance is not read-back or delivery confirmation. This API does not register an operating-system push receiver. Historical event categories are decoded separately from timer status alarm flags.

## Windows app

The Home management tab provides home, room, member, invitation, rename and preference controls, including per-zone room assignment. Destructive, ownership and notification actions require an explicit confirmation. Loading a page never performs a write. After a write attempt the UI requires a reload instead of presenting an optimistic value as confirmed.

## Evidence and limits

The attributed request/response models and bit layouts were traced from the inspected RainPoint Home app. Offline tests cover stale snapshots, session changes, concurrent writes, unknown preferences, all-zone room assignments, request scope and Windows bindings on net472 and .NET 10.

Read-only checks on both runtimes decoded two homes, rooms, members, invitations and sign-in notification flags. Scene lists were empty; this does not validate populated scene details. After transient connectivity failures, the final expanded check passed on both targets, including time zones, currency/weather-type dictionaries and scene capability metadata. Results are retained under ignored `artifacts/administration-live-final`. No administration, notification, invitation, ownership or rename write was performed during this validation. [Account registration/password/profile operations](ACCOUNT-ADMINISTRATION.md) and [RF pairing/removal](DEVICE-PAIRING.md) were subsequently implemented and tested offline. They were not executed live. Initial hub provisioning and OS push registration remain outside this implementation.


### Isolated live write validation — 25 September 2026

The explicit temporary-home fixture passed on net10.0 and net472. It created and renamed an empty home, created/renamed/deleted a room, changed and restored units/date format and time zone, and verified currency and public fixture coordinates. Writes were confined to that temporary home; existing homes, device assignments, invitations and notification settings were not changed.

Cleanup checked identity, ownership and absence of devices, automations and additional members before deletion. Both runs verified the temporary home was absent and the pre-existing home identifiers remained, then removed the private recovery journal. Results: ignored `artifacts/temporary-home-live`. See [test controls and reconciliation](../tests/README.md#isolated-home-and-room-write-validation). Membership changes, real device assignments, notification delivery, account-profile changes and pairing remain unverified.


### Concurrent shared accounts — 25 September 2026

Two distinct existing accounts with access to the same home/hub/timer passed concurrent-session checks on net10.0 and net472. Each run kept two authenticated MQTT observers connected through eight rounds of simultaneous decoded reads covering all three zones. Logging out the second account left the first authenticated and connected. No membership, invitation, device-setting or valve write was made.

The fixture spaces the two logins by two minutes. An earlier immediate second login returned code 9993, and a separate earlier attempt timed out; those failures remain retained. The results establish concurrent established sessions with separate accounts, not unrestricted login frequency or a known scope for the cloud throttle. Both successful runs saw zero changing decoded pushes, so same-event delivery to both accounts remains a separate check. Evidence is in ignored `artifacts/shared-account-live`; [selection instructions](../tests/README.md#concurrent-shared-accounts) require both private settings paths explicitly.


### Real all-zone room assignment validation — 25 September 2026

The explicit cloud-only room fixture passed on net10.0 and net472. Using the already paired timer, it assigned each of zones 1, 2 and 3 separately to a temporary room, then all three together, then cleared the assignments. Every step read back the typed identities and checked the pre-existing rooms and assignments. Cleanup deleted the temporary room, confirmed original room metadata was unchanged and removed the private journal. No valve or timer-setting command was sent.

The first net472 attempt timed out during sign-in before any write. That failed result is retained separately from the later pass. Results are under ignored `artifacts/room-assignment-live`; see [fixture controls](../tests/README.md#cloud-only-room-assignment-validation). Account nickname writes also passed on both runtimes with verified restoration, as recorded in [account administration](ACCOUNT-ADMINISTRATION.md). Invitation/role changes, notification delivery and populated scene execution remain separate checks.

The 27 September odd-day/seasonal execution check also delivered identical fresh active/idle MQTT events to both already-shared accounts simultaneously. This extends the earlier concurrent read-only observation. See [schedule evidence](SCHEDULES.md).

### Isolated invitation and membership validation — 28 September 2026

`TemporaryHomeMembershipRoundTrip` passed on net10.0. It created an empty temporary home, invited the authorized support test account, accepted the matching invitation and verified both administrator and member role changes. It then removed the guest, deleted the temporary home, confirmed the temporary invitation was absent and verified both accounts' original home identities were unchanged. No paired device, real-home membership or ownership was changed. The private journal was removed. Result: `artifacts/membership-live/membership-roundtrip_net10.0_20260928000912.trx`.
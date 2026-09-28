# Account registration, recovery and profile

The client has typed email verification, registration, password reset, signed-in password change, and sign-in profile observations. The Windows Account management tab exposes these operations. Loading the tab sends no email and changes nothing.

## Registration and recovery

While signed out, use `SendEmailVerificationCodeAsync` only when the user requests an email. `VerifyEmailCodeAsync` returns a `RainPointVerifiedEmail` for either registration or password reset. Supply that observation to `RegisterEmailAsync` or `ResetPasswordByEmailAsync`. It belongs to the client that verified it and permits one matching write attempt, including an uncertain attempt. The registration caller must obtain agreement to the vendor's current terms and supply the selected two-letter country code and an installation identifier. An optional reviewed agreement version can be supplied. Registration does not sign in automatically.

The vendor also provides manual-email registration: `GetRegistrationEmailAsync` returns the destination and content the user must email from the registering address. The client does not send that email. `VerifyRegistrationEmailAsync` checks whether it arrived. Returned instructions and verification codes should be treated as private. Public APIs never accept request JSON or expose encoded transport payloads.

Passwords follow the inspected app's 6–20-character account-write rule. The client applies the vendor-required digest internally and sends requests over HTTPS. This is protocol compatibility, not password storage. The library does not persist credentials or log requests. Stop automatic recovery and sign out before anonymous account operations.

## Signed-in changes

`AccountProfile` exposes only the nonsensitive user ID, email, nickname, photo URL and language observed at sign-in. Omitted fields remain null. `SetAccountNicknameAsync` and `SetAccountPhotoAsync` send minimal patches against that observation. Photo changes reference an already uploaded HTTPS image; local image upload is not implemented. After any attempted profile write the observation is invalidated; sign in again to read fresh values. Refresh-token renewal preserves the original observation and is not a profile refresh.

Stop automatic recovery before `ChangePasswordAsync`. Any submitted attempt clears the local session, even if the server outcome is uncertain. No password operation is automatically replayed. The Windows app stops monitoring/recovery and removes saved credentials before submitting a password change or email reset. If it cannot remove the saved file, it does not submit the change. Password and verification-code controls are cleared on submission. Sign in explicitly afterwards; the old password is never silently restored or retried.

## Validation and scope

Offline NUnit coverage runs on net472 and net10.0, including request fields and independent digest vectors, matching verification purpose/client, consumed proofs, session restrictions, failed/uncertain writes, cancellation and profile refresh behavior. Dashboard and real WPF tests cover agreement gating, changing email/purpose, clearing password boxes, saved-credential removal and disabled controls after sign-out.

Read-only live checks on 25 September 2026 decoded the profile on both targets, with email and nickname present. No verification email, registration, password change, profile write or account deletion was performed. Registration/password writes are contract-tested, not live-validated. Phone-number registration/binding, changing an account's bound email, account deletion, image upload and OS push registration are not implemented by these APIs.


### Reversible nickname live validation — 25 September 2026

The explicit nickname round trip passed on net472 and net10.0. Each run wrote a uniquely identified temporary nickname, verified it after a fresh sign-in, restored the original value and verified restoration after another sign-in. Account identity, email, photo and language remained unchanged. Both private recovery journals were removed after successful verification. Sign-ins were spaced by two minutes; no password change, email or device command was sent. Evidence is retained under ignored `artifacts/nickname-live`; see [test controls and reconciliation](../tests/README.md#reversible-account-nickname-validation).

This establishes nickname writes and preservation of the other observed fields. It does not establish photo replacement, password/registration writes or email delivery.

### Dedicated test-account password round trip — 28 September 2026

`TestAccountPasswordRoundTrip` passed on net10.0 using only the dedicated support account. It changed the password once, verified local-session invalidation, authenticated with the temporary password, restored the original and authenticated with the original again. Account identity was checked before restoration. Sign-ins were spaced by two minutes. The private recovery journal was removed only after successful original-password verification; the settings file, owner account and hardware were unchanged. Result: `artifacts/password-live/support-password-roundtrip_net10.0_20260928001811.trx`.

The explicit recovery fixture retains uncertain state instead of replaying a password write. This validates signed-in password changes and restoration; anonymous registration/reset and photo replacement remain separately scoped operations. A photo replacement needs an already uploaded HTTPS image and restorable original.
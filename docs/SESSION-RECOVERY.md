# Session renewal and bounded recovery

`RainPointSessionRecovery` is an optional worker for an already signed-in `RainPointCloudClient`. It checks local session state every ten seconds and refreshes within sixty seconds of the server-supplied expiry. Healthy checks do not contact the cloud. Only one worker can own a client, and each worker can be started once.

```csharp
await client.LoginAsync(email, password, countryCode, cancellationToken);
var recovery = new RainPointSessionRecovery(client);
Task recoveryRun = recovery.RunAsync(cancellationToken);
try
{
    // Read status or run a RainPointMonitor using the same client.
    await UseClientAsync(client, cancellationToken);
}
finally
{
    await recovery.StopAsync();
    await recoveryRun;
}
// Stop any monitor too before logout or disposing the client.
```

Without a credential provider, an expired/rejected session that cannot be refreshed requires explicit sign-in. A caller can provide `Func<CancellationToken, Task<RainPointCredentials?>>` to retrieve credentials for the same account on demand. When credential recovery becomes necessary, the worker first waits two minutes in `CoolingDown`, then requests credentials and makes at most one automatic password-login attempt during its lifetime. It does not retrieve or retain the password during that wait. This avoids consuming the sole attempt immediately after another app login, when the service can reject it with code 9993. Returning null, provider failure or login failure consumes that attempt; another invalidation requires explicit sign-in. This prevents two apps repeatedly displacing each other's sessions. The library does not save credentials or log them. The callback must honor cancellation and return the same account.

Refresh failures use exponential delay from thirty seconds to five minutes, respecting a longer server RetryAfter. Transient refresh failure does not immediately fall back to password login. A successful recovery never replays the operation that failed: reads, configuration changes and watering commands remain the caller's responsibility.

`StateChanged` reports Stopped, Healthy, Renewing, SigningIn, CoolingDown and AuthenticationRequired. Events run on the worker thread; marshal to a UI dispatcher when needed. Observer exceptions cannot stop recovery. Stop and await the worker before account changes, logout or client disposal. Explicit login/logout while a worker owns the client is rejected.

The Windows app enables automatic token renewal after sign-in. Its separate **Allow one reconnect using saved credentials** option is off by default and is not persisted. It only retrieves the DPAPI-protected saved account when enabled, remembered and matching the signed-in account. Turning renewal off, signing out, changing accounts or closing the app stops the worker. Recovery disarms valve controls and clears editable snapshots; it never sends a watering command.

## Validation

Twenty deterministic NUnit cases cover expiry, invalidation, token rotation, one-login policy, cooldown, transient failures, cancellation, lifecycle ownership and no command replay on both net472 and net10.0. The signed-out dashboard test also covers failed worker startup without leaving a false active state.

On 24 September 2026, `CompletionLiveTests` passed on both targets: an injected near-expiry clock triggered one real refresh and authenticated home discovery succeeded afterward. This verifies the renewal path, not natural-expiry endurance or live password-relogin behavior. Earlier network timeouts remain retained separately.


### Actual session invalidation and credential recovery — 25 September 2026

`SessionRecoveryLiveTests.AnotherLoginInvalidatesSessionAndOneRecoveryRestoresMonitoring` passed sequentially on net10.0 and net472. It confirmed that a second login caused the original session to be rejected, then allowed exactly one credential callback after a two-minute gap. A new authenticated MQTT connection, fresh decoded status and home discovery succeeded after recovery; both workers remained running before orderly shutdown. No watering or device-setting writes occurred. Evidence is retained under ignored `artifacts/session-observer-live`.

The fixture does not claim repeated automatic takeovers: the production policy still permits only one credential attempt per worker. It intentionally displaces its own competing test login; do not run it alongside normal app use. Credentials and payloads are omitted from output. See the [live-test instructions](../tests/README.md#explicit-session-recovery-and-observer-duration-checks).

A new cloud session reported about 60 days of remaining lifetime during the subsequent observer check. Natural cloud-session expiry therefore remains separate from these short live checks and the earlier injected-clock refresh. MQTT observer credentials have their own shorter renewal cycle.


### Immediate-login throttle regression

The first test with an immediately returning credential provider reproduced cloud code 9993 after a competing login. Recovery then correctly stopped after its sole failed attempt, but could not reconnect. This failed live result remains retained. The client now waits two minutes before requesting credentials for that attempt. Deterministic tests cover the 119/120-second boundary, cancellation without fetching credentials, and the existing one-attempt/no-command-replay behavior. The delay reduces rapid-login throttling; it cannot guarantee success when another application continues competing for the same account.


### Twelve-minute observer evidence

Both real-clock observer runs passed: net10.0 and net472 each observed two authenticated MQTT connections, a sustained first connection, continuing fresh decoded status and a connected final state over twelve minutes. No clock injection, password fallback or device commands were used. Results are in ignored `artifacts/session-observer-live`. Original cloud-session expiry was not crossed. These successful observer checks remain distinct from the separately verified corrected credential-recovery checks described below.


### Corrected immediate credential policy verified

`AnotherLoginWithImmediateCredentialsRestoresMonitoring` separately passed on both runtimes. Unlike the first delayed-provider check, its callback immediately supplies the saved account, matching the Windows app's credential timing. It requires actual server rejection, exactly one credential callback, a new MQTT connection and fresh decoded status; it fails if recovery exhausts its login attempt. Login diagnostic output contains only HTTP/API result codes, never headers, tokens or response bodies.

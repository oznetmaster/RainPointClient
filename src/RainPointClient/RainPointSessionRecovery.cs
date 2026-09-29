// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace RainPointClient;

/// <summary>Credentials returned on demand by the caller's credential policy. Never logged or persisted by the library.</summary>
public sealed class RainPointCredentials
	{
	/// <summary>
	/// Validates and stores credentials supplied by the caller's account-recovery policy.
	/// </summary>
	/// <param name="email">The account email address.</param>
	/// <param name="password">The account password; it is not persisted or logged by the client.</param>
	/// <param name="areaCode">The account country calling code as digits without a plus sign.</param>
	/// <exception cref="System.ArgumentException">Email is required. Password is required. Area code is required.</exception>
	public RainPointCredentials (string email, string password, string areaCode)
		{
		if (string.IsNullOrWhiteSpace (email))
			throw new ArgumentException ("Email is required.", nameof (email));
		if (string.IsNullOrEmpty (password))
			throw new ArgumentException ("Password is required.", nameof (password));
		if (string.IsNullOrWhiteSpace (areaCode))
			throw new ArgumentException ("Area code is required.", nameof (areaCode));
		Email = email;
		Password = password;
		AreaCode = areaCode;
		}
	/// <summary>
	/// Gets the account email address.
	/// </summary>
	public string Email
		{
		get;
		}
	/// <summary>
	/// Gets the password supplied by the caller; do not log or display this value.
	/// </summary>
	public string Password
		{
		get;
		}
	/// <summary>
	/// Gets the account's country calling code as digits without a plus sign.
	/// </summary>
	public string AreaCode
		{
		get;
		}
	}

/// <summary>
/// Describes the state of the optional session-recovery worker.
/// </summary>
public enum RainPointSessionState
	{
	/// <summary>The session-recovery worker is stopped.</summary>
	Stopped,
	/// <summary>The current authenticated session is usable.</summary>
	Healthy,
	/// <summary>The worker is attempting token refresh.</summary>
	Renewing,
	/// <summary>The worker is attempting an explicitly authorized credential sign-in.</summary>
	SigningIn,
	/// <summary>The worker is waiting before another permitted recovery attempt.</summary>
	CoolingDown,
	/// <summary>The caller must provide or restore account authentication.</summary>
	AuthenticationRequired
	}
/// <summary>
/// Carries a session-recovery state transition.
/// </summary>
/// <param name="state">The new lifecycle state carried by the event.</param>
public sealed class RainPointSessionStateChangedEventArgs (RainPointSessionState state) : EventArgs
	{
	/// <summary>
	/// Gets the lifecycle state carried by this event.
	/// </summary>
	public RainPointSessionState State { get; } = state;
	}

/// <summary>Opt-in session renewal and bounded credential recovery, independent of device commands.</summary>
/// <remarks>
/// Sign in before starting. Stop and await this worker before changing accounts, logging out or disposing the client.
/// Events run on a background thread. A credential provider must return the same account and honor cancellation.
/// A worker waits two minutes before requesting credentials, then permits at most one automatic password login; failure or another invalidation requires explicit sign-in.
/// No failed read, setting write or watering command is ever replayed. The caller owns secure credential storage.
/// </remarks>
public sealed class RainPointSessionRecovery
	{
	private readonly RainPointCloudClient _client;
	private readonly Func<CancellationToken, Task<RainPointCredentials?>>? _credentials;
	private readonly Func<DateTimeOffset> _now;
	private readonly Func<TimeSpan, CancellationToken, Task> _delay;
	private readonly SemaphoreSlim _check = new (1, 1);
	private readonly object _gate = new ();
	private CancellationTokenSource? _stop;
	private Task? _run;
	private bool _loginUsed;
	private bool _credentialCooldownStarted;
	private DateTimeOffset _notBefore;
	private int _failures;
	private int _state;
	/// <summary>
	/// Creates an unstarted recovery worker with an optional caller-controlled credential provider.
	/// </summary>
	/// <param name="client">The caller-owned cloud client; this helper does not dispose it.</param>
	/// <param name="credentialProvider">An optional cancellable provider for the same account's credentials; null permits refresh-token recovery only.</param>
	public RainPointSessionRecovery (RainPointCloudClient client, Func<CancellationToken, Task<RainPointCredentials?>>? credentialProvider = null)
	 : this (client, credentialProvider, () => DateTimeOffset.UtcNow, Task.Delay) { }
	/// <summary>
	/// Creates an unstarted recovery worker with an optional caller-controlled credential provider.
	/// </summary>
	/// <param name="client">The caller-owned cloud client; this helper does not dispose it.</param>
	/// <param name="credentials">A cancellable same-account credential provider, or null to disable credential login.</param>
	/// <param name="now">The UTC clock used for deterministic expiry and cooldown calculations.</param>
	/// <param name="delay">The cancellable delay implementation used by the worker.</param>
	/// <exception cref="System.ArgumentNullException">A required argument is null.</exception>
	internal RainPointSessionRecovery (RainPointCloudClient client, Func<CancellationToken, Task<RainPointCredentials?>>? credentials, Func<DateTimeOffset> now, Func<TimeSpan, CancellationToken, Task> delay)
		{
		_client = client ?? throw new ArgumentNullException (nameof (client));
		_credentials = credentials;
		_now = now;
		_delay = delay;
		}
	/// <summary>
	/// Gets the worker's current session-health and recovery state.
	/// </summary>
	public RainPointSessionState State => (RainPointSessionState)Volatile.Read (ref _state);
	/// <summary>
	/// Occurs on a background thread when session-health or recovery state changes.
	/// </summary>
	public event EventHandler<RainPointSessionStateChangedEventArgs>? StateChanged;
	/// <summary>
	/// Starts the single-use worker after explicit sign-in and runs until cancellation or stop.
	/// </summary>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task representing the operation lifetime.</returns>
	/// <exception cref="System.InvalidOperationException">A session recovery worker can run only once. Sign in before starting session recovery. This client already has a session recovery worker.</exception>
	public Task RunAsync (CancellationToken cancellationToken = default)
		{
		lock (_gate)
			{
			if (_run is not null)
				throw new InvalidOperationException ("A session recovery worker can run only once.");
			if (!_client.HasValidSession)
				throw new InvalidOperationException ("Sign in before starting session recovery.");
			if (!_client.AcquireSessionRecovery ())
				throw new InvalidOperationException ("This client already has a session recovery worker.");
			_stop = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
			_run = Task.Run (() => RunCoreAsync (_stop.Token), CancellationToken.None);
			return _run;
			}
		}
	/// <summary>
	/// Cancels recovery and waits for the worker to finish before the client is reused or disposed.
	/// </summary>
	/// <returns>A task representing the operation lifetime.</returns>
	public async Task StopAsync ()
		{
		Task? run;
		lock (_gate)
			{
			_stop?.Cancel ();
			run = _run;
			}
		if (run is not null)
			await run.ConfigureAwait (false);
		}
	private async Task RunCoreAsync (CancellationToken token)
		{
		try
			{
			while (true)
				{
				token.ThrowIfCancellationRequested ();
				await CheckAsync (token).ConfigureAwait (false);
				await _delay (TimeSpan.FromSeconds (10), token).ConfigureAwait (false);
				}
			}
		catch (OperationCanceledException) when (token.IsCancellationRequested) { }
		finally
			{
			_client.ReleaseSessionRecovery ();
			SetState (RainPointSessionState.Stopped);
			lock (_gate)
				{
				_stop?.Dispose ();
				_stop = null;
				}
			}
		}
	/// <summary>
	/// Performs one serialized recovery check, respecting cooldown and the single credential-login limit.
	/// </summary>
	/// <param name="token">Cancellation for this operation.</param>
	/// <returns>A task representing the operation lifetime.</returns>
	internal async Task CheckAsync (CancellationToken token = default)
		{
		await _check.WaitAsync (token).ConfigureAwait (false);
		try
			{
			token.ThrowIfCancellationRequested ();
			DateTimeOffset now = _now ();
			DateTimeOffset? expiry = _client.SessionExpiresAt;
			if (expiry > now.AddSeconds (60))
				{
				_failures = 0;
				_credentialCooldownStarted = false;
				SetState (RainPointSessionState.Healthy);
				return;
				}
			if (now < _notBefore)
				{
				SetState (RainPointSessionState.CoolingDown);
				return;
				}
			if (_client.CanRefreshSession)
				{
				SetState (RainPointSessionState.Renewing);
				await _client.RefreshSessionAsync (token).ConfigureAwait (false);
				_failures = 0;
				_credentialCooldownStarted = false;
				SetState (RainPointSessionState.Healthy);
				return;
				}
			// Do not displace another app just because a still-valid token cannot be renewed.
			if (expiry > now)
				{
				SetState (RainPointSessionState.Healthy);
				return;
				}
			if (_credentials is null || _loginUsed)
				{
				SetState (RainPointSessionState.AuthenticationRequired);
				return;
				}
			if (!_credentialCooldownStarted)
				{
				// Another app login can invalidate this session immediately. The service throttles an immediate
				// replacement login (9993); wait before consuming the worker's sole credential attempt.
				_credentialCooldownStarted = true;
				_notBefore = now.AddMinutes (2);
				SetState (RainPointSessionState.CoolingDown);
				return;
				}
			_loginUsed = true;
			SetState (RainPointSessionState.SigningIn);
			RainPointCredentials? credentials = await _credentials (token).ConfigureAwait (false);
			token.ThrowIfCancellationRequested ();
			if (credentials is null)
				{
				SetState (RainPointSessionState.AuthenticationRequired);
				return;
				}
			await _client.LoginForRecoveryAsync (credentials, token).ConfigureAwait (false);
			_failures = 0;
			SetState (RainPointSessionState.Healthy);
			}
		catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
		catch (Exception error) when (error is not OutOfMemoryException)
			{
			_failures = Math.Min (_failures + 1, 5);
			TimeSpan delay = TimeSpan.FromSeconds (Math.Min (300, 15 * Math.Pow (2, _failures)));
			if (error is RainPointException { RetryAfter: { } retry } && retry > delay)
				delay = retry;
			var remaining = DateTimeOffset.MaxValue - _now ();
			_notBefore = delay >= remaining ? DateTimeOffset.MaxValue : _now () + delay;
			SetState (_loginUsed && !_client.HasValidSession ? RainPointSessionState.AuthenticationRequired : RainPointSessionState.CoolingDown);
			}
		finally { _check.Release (); }
		}
	private void SetState (RainPointSessionState state)
		{
		if (Interlocked.Exchange (ref _state, (int)state) == (int)state)
			return;
		var args = new RainPointSessionStateChangedEventArgs (state);
		foreach (Delegate handler in StateChanged?.GetInvocationList () ?? Array.Empty<Delegate> ())
			try
				{
				((EventHandler<RainPointSessionStateChangedEventArgs>)handler) (this, args);
				}
			catch (Exception error) when (error is not OutOfMemoryException) { }
		}
	}
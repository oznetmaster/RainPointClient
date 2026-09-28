using System;
using System.Threading;
using System.Threading.Tasks;

namespace RainPointClient;

/// <summary>Credentials returned on demand by the caller's credential policy. Never logged or persisted by the library.</summary>
public sealed class RainPointCredentials
	{
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
	public string Email
		{
		get;
		}
	public string Password
		{
		get;
		}
	public string AreaCode
		{
		get;
		}
	}

public enum RainPointSessionState
	{
	Stopped, Healthy, Renewing, SigningIn, CoolingDown, AuthenticationRequired
	}
public sealed class RainPointSessionStateChangedEventArgs (RainPointSessionState state) : EventArgs
	{
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
	public RainPointSessionRecovery (RainPointCloudClient client, Func<CancellationToken, Task<RainPointCredentials?>>? credentialProvider = null)
	 : this (client, credentialProvider, () => DateTimeOffset.UtcNow, Task.Delay) { }
	internal RainPointSessionRecovery (RainPointCloudClient client, Func<CancellationToken, Task<RainPointCredentials?>>? credentials, Func<DateTimeOffset> now, Func<TimeSpan, CancellationToken, Task> delay)
		{
		_client = client ?? throw new ArgumentNullException (nameof (client));
		_credentials = credentials;
		_now = now;
		_delay = delay;
		}
	public RainPointSessionState State => (RainPointSessionState)Volatile.Read (ref _state);
	public event EventHandler<RainPointSessionStateChangedEventArgs>? StateChanged;
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
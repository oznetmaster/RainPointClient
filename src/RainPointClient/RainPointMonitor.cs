// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using RainPointClient.Protocol;

namespace RainPointClient;

/// <summary>Monitors one discovered hub using MQTT push plus REST polling. Never sends watering commands.</summary>
/// <remarks>
/// Sign in before starting. Events run on background threads; marshal UI updates and keep handlers nonblocking.
/// Stop and await this monitor before changing accounts, logging out, or disposing the client.
/// One monitor may run per client. Separate clients sharing an account can still displace each other's observer.
/// </remarks>
public sealed class RainPointMonitor
	{
	private readonly RainPointCloudClient _client;
	private readonly RainPointHub _hub;
	private readonly TimeSpan _pollInterval;
	private readonly bool _enablePush;
	private readonly bool _pollWhilePushConnected;
	private readonly IObserverTransport _transport;
	private readonly Func<TimeSpan, CancellationToken, Task> _delay;
	private readonly Func<DateTimeOffset> _now;
	private readonly StatusMerger _merger;
	private readonly object _gate = new ();
	private readonly SemaphoreSlim _pollGate = new (1, 1);
	private readonly Random _jitter = new ();
	private CancellationTokenSource? _stop;
	private Task? _run;
	private RainPointStatusUpdate? _current;
	private RainPointMonitorState _state = RainPointMonitorState.Stopped;
	private volatile bool _pushConnected;
	private long _pushGeneration;
	private long _synchronizedPushGeneration = -1;
	private object? _pushSessionIdentity;
	private long _acceptedPushes;
	private long _rejectedPushes;
	private long _configurationRevision = -1;

	/// <summary>
	/// Creates an unstarted observer for a discovered hub; the caller retains ownership of the cloud client.
	/// </summary>
	/// <param name="client">The caller-owned cloud client; this helper does not dispose it.</param>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="options">Monitor settings; null selects defaults where allowed.</param>
	public RainPointMonitor (RainPointCloudClient client, RainPointHub hub, RainPointMonitorOptions? options = null)
		 : this (client, hub, options ?? new RainPointMonitorOptions (), new MqttObserverTransport (), Task.Delay, () => DateTimeOffset.UtcNow) { }

	/// <summary>
	/// Creates an unstarted observer for a discovered hub; the caller retains ownership of the cloud client.
	/// </summary>
	/// <param name="client">The caller-owned cloud client; this helper does not dispose it.</param>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="options">Monitor settings; null selects defaults where allowed.</param>
	/// <param name="transport">The MQTT transport used by the observer.</param>
	/// <param name="delay">The cancellable delay implementation used by the worker.</param>
	/// <param name="now">The UTC clock used for deterministic expiry and cooldown calculations.</param>
	/// <exception cref="System.ArgumentNullException">A required argument is null.</exception>
	/// <exception cref="System.ArgumentException">Use a hub discovered through a home.</exception>
	/// <exception cref="System.ArgumentOutOfRangeException">Polling must be between five seconds and one hour.</exception>
	internal RainPointMonitor (RainPointCloudClient client, RainPointHub hub, RainPointMonitorOptions options,
		 IObserverTransport transport, Func<TimeSpan, CancellationToken, Task> delay, Func<DateTimeOffset> now)
		{
		_client = client ?? throw new ArgumentNullException (nameof (client));
		_hub = hub ?? throw new ArgumentNullException (nameof (hub));
		if (hub.HomeId <= 0 || hub.Id <= 0)
			throw new ArgumentException ("Use a hub discovered through a home.", nameof (hub));
		if (options.PollInterval < TimeSpan.FromSeconds (5) || options.PollInterval > TimeSpan.FromHours (1))
			throw new ArgumentOutOfRangeException (nameof (options), "Polling must be between five seconds and one hour.");
		_pollInterval = options.PollInterval;
		_enablePush = options.EnablePush;
		_pollWhilePushConnected = options.PollWhilePushConnected;
		_transport = transport;
		_delay = delay;
		_now = now;
		_merger = new StatusMerger (hub);
		}

	/// <summary>Raised for a newer configuration revision of this hub's home. Requires an identified login profile. Does not refresh configuration or mark status fresh.</summary>
	public event EventHandler<RainPointConfigurationChange>? ConfigurationChanged;
	/// <summary>
	/// Occurs on a background thread after accepting and merging a hub or timer observation.
	/// </summary>
	public event EventHandler<RainPointStatusUpdate>? StatusReceived;
	/// <summary>
	/// Occurs on a background thread when the monitor's connection lifecycle state changes.
	/// </summary>
	public event EventHandler<RainPointMonitorStateChangedEventArgs>? StateChanged;
	/// <summary>
	/// Gets the latest merged observation, or null before a successful observation is accepted.
	/// </summary>
	public RainPointStatusUpdate? Current
		{
		get
			{
			lock (_gate)
				return _current;
			}
		}
	/// <summary>
	/// Gets the monitor's current connection lifecycle state.
	/// </summary>
	public RainPointMonitorState State
		{
		get
			{
			lock (_gate)
				return _state;
			}
		}
	/// <summary>True after a successful catch-up read on the current MQTT connection and authenticated session. This is observer availability, not a physical-device heartbeat.</summary>
	public bool LiveUpdatesAvailable => _pushConnected && _client.HasValidSession
		&& ReferenceEquals (_pushSessionIdentity, _client.SessionIdentity)
		&& Interlocked.Read (ref _pushGeneration) == Interlocked.Read (ref _synchronizedPushGeneration);
	/// <summary>
	/// Gets the count of accepted status pushes, not a count of physical valve transitions.
	/// </summary>
	public long AcceptedPushCount => Interlocked.Read (ref _acceptedPushes);
	/// <summary>
	/// Gets the count of status pushes rejected by scope, decoding or ordering checks.
	/// </summary>
	public long RejectedPushCount => Interlocked.Read (ref _rejectedPushes);

	/// <summary>Runs until cancelled or stopped. A monitor instance can be started once.</summary>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task representing the operation lifetime.</returns>
	/// <exception cref="System.InvalidOperationException">This monitor has already been started. This client already has a running monitor.</exception>
	public Task RunAsync (CancellationToken cancellationToken = default)
		{
		lock (_gate)
			{
			if (_run is not null)
				throw new InvalidOperationException ("This monitor has already been started.");
			if (!_client.AcquireMonitor ())
				throw new InvalidOperationException ("This client already has a running monitor.");
			_stop = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
			_run = Task.Run (() => RunCoreAsync (_stop.Token), CancellationToken.None);
			return _run;
			}
		}

	/// <summary>
	/// Cancels observation and waits for the running monitor to finish; does not dispose the caller's client.
	/// </summary>
	/// <returns>A task representing the operation lifetime.</returns>
	public async Task StopAsync ()
		{
		Task? running;
		lock (_gate)
			{
			_stop?.Cancel ();
			running = _run;
			}
		if (running is not null)
			await running.ConfigureAwait (false);
		}

	/// <summary>Requests a read now. Polls are serialized; watering commands are never issued or retried.</summary>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task representing the operation lifetime.</returns>
	/// <exception cref="System.InvalidOperationException">Start the monitor before refreshing it.</exception>
	public async Task RefreshAsync (CancellationToken cancellationToken = default)
		{
		CancellationTokenSource refresh;
		lock (_gate)
			{
			if (_stop is null || _stop.IsCancellationRequested)
				throw new InvalidOperationException ("Start the monitor before refreshing it.");
			refresh = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken, _stop.Token);
			}
		using CancellationTokenSource lifetime = refresh;
		cancellationToken = refresh.Token;
		await _pollGate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			long generation = _pushConnected ? Interlocked.Read (ref _pushGeneration) : -1;
			RainPointHubStatus status = await _client.GetHubStatusAsync (_hub, cancellationToken).ConfigureAwait (false);
			RainPointStatusUpdate update;
			lock (_gate)
				{
				cancellationToken.ThrowIfCancellationRequested ();
				update = _merger.ApplyPoll (status, _now ());
				_current = update;
				}
			Raise (StatusReceived, update);
			if (generation >= 0 && _pushConnected && generation == Interlocked.Read (ref _pushGeneration))
				{
				Interlocked.Exchange (ref _synchronizedPushGeneration, generation);
				if (LiveUpdatesAvailable)
					SetState (RainPointMonitorState.PushConnected);
				}
			}
		finally { _pollGate.Release (); }
		}

	private async Task RunCoreAsync (CancellationToken token)
		{
		SetState (RainPointMonitorState.Starting);
		try
			{
			Task poll = PollLoopAsync (token);
			Task push = _enablePush ? PushLoopAsync (token) : Task.Delay (Timeout.Infinite, token);
			await Task.WhenAny (poll, push).ConfigureAwait (false);
			_stop!.Cancel ();
			await Task.WhenAll (poll, push).ConfigureAwait (false);
			}
		catch (OperationCanceledException) when (token.IsCancellationRequested) { }
		finally
			{
			await _pollGate.WaitAsync ().ConfigureAwait (false);
			_pollGate.Release ();
			_client.ReleaseMonitor ();
			SetState (RainPointMonitorState.Stopped);
			lock (_gate)
				{
				_stop?.Dispose ();
				_stop = null;
				}
			}
		}

	private async Task PollLoopAsync (CancellationToken token)
		{
		while (!token.IsCancellationRequested)
			{
			TimeSpan wait = _pollInterval;
			try
				{
				if (!_client.HasValidSession)
					SetState (RainPointMonitorState.AuthenticationRequired);
				else
					{
					if (_pollWhilePushConnected || !LiveUpdatesAvailable)
						await RefreshAsync (token).ConfigureAwait (false);
					SetState (LiveUpdatesAvailable ? RainPointMonitorState.PushConnected : _pushConnected ? RainPointMonitorState.Reconnecting : RainPointMonitorState.Polling);
					}
				}
			catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
			catch (Exception error) when (error is RainPointException or HttpRequestException or OperationCanceledException or InvalidOperationException)
				{
				SetState (_client.HasValidSession ? RainPointMonitorState.Reconnecting : RainPointMonitorState.AuthenticationRequired);
				if (error is RainPointException { RetryAfter: { } retry } && retry > wait)
					wait = retry;
				}
			await _delay (wait, token).ConfigureAwait (false);
			}
		}

	private async Task PushLoopAsync (CancellationToken token)
		{
		int failures = 0;
		while (!token.IsCancellationRequested)
			{
			TimeSpan? retryAfter = null;
			try
				{
				if (!_client.HasValidSession)
					{
					SetState (RainPointMonitorState.AuthenticationRequired);
					await _delay (TimeSpan.FromSeconds (30), token).ConfigureAwait (false);
					continue;
					}
				ObserverCredentials credentials = await _client.GetObserverAsync (_hub, token).ConfigureAwait (false);
				DateTimeOffset renewAt = _now () + RenewalDelay (credentials, _now ());
				using CancellationTokenSource connection = CancellationTokenSource.CreateLinkedTokenSource (token);
				Task transport = _transport.RunAsync (credentials, () =>
				{
					Interlocked.Increment (ref _pushGeneration);
					_pushSessionIdentity = credentials.SessionIdentity;
					_pushConnected = true;
					SetState (_pollWhilePushConnected ? RainPointMonitorState.PushConnected : RainPointMonitorState.Reconnecting);
				}, Receive, connection.Token);
				try
					{
					while (!transport.IsCompleted && _now () < renewAt && ReferenceEquals (credentials.SessionIdentity, _client.SessionIdentity))
						{
						TimeSpan remaining = renewAt - _now ();
						using CancellationTokenSource check = CancellationTokenSource.CreateLinkedTokenSource (token);
						Task tick = _delay (remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining < TimeSpan.FromSeconds (10) ? remaining : TimeSpan.FromSeconds (10), check.Token);
						await Task.WhenAny (transport, tick).ConfigureAwait (false);
						check.Cancel ();
						token.ThrowIfCancellationRequested ();
						}
					}
				finally
					{
					connection.Cancel ();
					try
						{
						await transport.ConfigureAwait (false);
						}
					catch (OperationCanceledException) when (connection.IsCancellationRequested) { }
					_pushConnected = false;
					}
				failures = 0;
				SetState (RainPointMonitorState.Reconnecting);
				}
			catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
			catch (Exception error) when (error is not OutOfMemoryException)
				{
				_pushConnected = false;
				SetState (_client.HasValidSession ? RainPointMonitorState.Reconnecting : RainPointMonitorState.AuthenticationRequired);
				failures = Math.Min (failures + 1, 5);
				retryAfter = (error as RainPointException)?.RetryAfter;
				}
			// Includes clean remote disconnects: a broker that immediately closes must not cause a hot loop.
			double seconds = Math.Min (120, 5 * Math.Pow (2, failures)) * (0.8 + _jitter.NextDouble () * 0.4);
			if (retryAfter.HasValue)
				seconds = Math.Max (seconds, Math.Min (86400, retryAfter.Value.TotalSeconds));
			await _delay (TimeSpan.FromSeconds (seconds), token).ConfigureAwait (false);
			}
		}

	/// <summary>
	/// Calculates the delay before observer credentials should be renewed, using their reported expiry.
	/// </summary>
	/// <param name="credentials">The session-bound observer credentials.</param>
	/// <param name="now">The current instant used for expiry and timestamp checks.</param>
	/// <returns>The bounded delay until observer credentials should be renewed.</returns>
	/// <exception cref="RainPointException">The observer credentials have expired or are too close to expiry.</exception>
	internal static TimeSpan RenewalDelay (ObserverCredentials credentials, DateTimeOffset now)
		{
		// Upstream observed a roughly 570-second observer lifetime. Missing expiry uses that bounded fallback.
		double lifetime = 570;
		if (credentials.ExpiresAt.HasValue)
			{
			DateTimeOffset? expiry = PushDecoder.Timestamp (credentials.ExpiresAt.Value, DateTimeOffset.MaxValue.AddMinutes (-5));
			if (expiry.HasValue)
				lifetime = Math.Min (570, (expiry.Value - now).TotalSeconds);
			}
		if (lifetime <= 1)
			throw new RainPointException ("The observer credentials have expired or are too close to expiry.");
		return TimeSpan.FromSeconds (Math.Max (1, lifetime - Math.Min (60, lifetime / 10)));
		}

	private void Receive (byte[] payload)
		{
		RainPointConfigurationChange? change = PushDecoder.DecodeConfiguration (payload, _hub.HomeId, _client.AccountProfile?.Id);
		if (change != null)
			{
			lock (_gate)
				{
				if (_stop is null || _stop.IsCancellationRequested || change.Revision <= _configurationRevision)
					return;
				_configurationRevision = change.Revision;
				}
			Interlocked.Increment (ref _acceptedPushes);
			Raise (ConfigurationChanged, change);
			return;
			}
		RainPointStatusUpdate? update;
		lock (_gate)
			{
			if (_stop?.IsCancellationRequested == true)
				return;
			PushReading? reading = PushDecoder.Decode (payload, _hub, _now ());
			update = reading is null ? null : _merger.ApplyPush (reading, _now ());
			if (update is not null)
				_current = update;
			}
		if (update is null)
			Interlocked.Increment (ref _rejectedPushes);
		else
			{
			Interlocked.Increment (ref _acceptedPushes);
			Raise (StatusReceived, update);
			}
		}

	private void SetState (RainPointMonitorState state)
		{
		lock (_gate)
			{
			if (_state == state)
				return;
			_state = state;
			}
		Raise (StateChanged, new RainPointMonitorStateChangedEventArgs (state));
		}

	private void Raise<T> (EventHandler<T>? handlers, T args) where T : EventArgs
		{
		if (handlers is null)
			return;
		foreach (EventHandler<T> handler in handlers.GetInvocationList ())
			try
				{
				handler (this, args);
				}
			catch (Exception error) when (error is not OutOfMemoryException) { }
		}
	}
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
	private long _acceptedPushes;
	private long _rejectedPushes;

	public RainPointMonitor (RainPointCloudClient client, RainPointHub hub, RainPointMonitorOptions? options = null)
		 : this (client, hub, options ?? new RainPointMonitorOptions (), new MqttObserverTransport (), Task.Delay, () => DateTimeOffset.UtcNow) { }

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
		_transport = transport;
		_delay = delay;
		_now = now;
		_merger = new StatusMerger (hub);
		}

	public event EventHandler<RainPointStatusUpdate>? StatusReceived;
	public event EventHandler<RainPointMonitorStateChangedEventArgs>? StateChanged;
	public RainPointStatusUpdate? Current
		{
		get
			{
			lock (_gate)
				return _current;
			}
		}
	public RainPointMonitorState State
		{
		get
			{
			lock (_gate)
				return _state;
			}
		}
	public long AcceptedPushCount => Interlocked.Read (ref _acceptedPushes);
	public long RejectedPushCount => Interlocked.Read (ref _rejectedPushes);

	/// <summary>Runs until cancelled or stopped. A monitor instance can be started once.</summary>
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
			RainPointHubStatus status = await _client.GetHubStatusAsync (_hub, cancellationToken).ConfigureAwait (false);
			RainPointStatusUpdate update;
			lock (_gate)
				{
				cancellationToken.ThrowIfCancellationRequested ();
				update = _merger.ApplyPoll (status, _now ());
				_current = update;
				}
			Raise (StatusReceived, update);
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
					await RefreshAsync (token).ConfigureAwait (false);
					SetState (_pushConnected ? RainPointMonitorState.PushConnected : RainPointMonitorState.Polling);
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
					_pushConnected = true;
					SetState (RainPointMonitorState.PushConnected);
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
// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using RainPointClient.Protocol;

namespace RainPointClient;

public sealed partial class RainPointCloudClient
	{
	private int _sessionRecoveryActive;
	internal bool AcquireSessionRecovery () => Interlocked.CompareExchange (ref _sessionRecoveryActive, 1, 0) == 0;
	internal void ReleaseSessionRecovery () => Volatile.Write (ref _sessionRecoveryActive, 0);
	/// <summary>Local session expiry; a server-side rejection can invalidate a session earlier.</summary>
	public DateTimeOffset? SessionExpiresAt => Volatile.Read (ref _session)?.ExpiresAt;
	public bool HasValidSession => SessionExpiresAt > DateTimeOffset.UtcNow;
	public bool CanRefreshSession => !string.IsNullOrWhiteSpace (Volatile.Read (ref _session)?.RefreshToken);

	/// <summary>Refreshes the session using its refresh token, without retaining or resending the password.</summary>
	/// <remarks>Explicit operation: no watering command is retried. Expiry must be supplied by the server.</remarks>
	public async Task RefreshSessionAsync (CancellationToken cancellationToken = default)
		{
		ThrowIfDisposed ();
		await _loginGate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			Session original = Volatile.Read (ref _session) ?? throw new InvalidOperationException ("There is no session to refresh.");
			if (string.IsNullOrWhiteSpace (original.RefreshToken))
				{
				throw new InvalidOperationException ("The session has no refresh token; sign in again.");
				}
			DateTimeOffset started = DateTimeOffset.UtcNow;
			ApiResult<LoginResponse> response;
			try
				{
				response = await SendAsync<RefreshRequest, ApiResult<LoginResponse>> (HttpMethod.Post, "auth/basic/app/token/refresh",
					 new RefreshRequest { RefreshToken = original.RefreshToken! }, null, cancellationToken).ConfigureAwait (false);
				}
			catch (RainPointException error) when (error.HttpStatus == HttpStatusCode.Unauthorized)
				{
				_ = Interlocked.CompareExchange (ref _session, null, original);
				throw;
				}
			CheckResult (response, original);
			LoginResponse data = RequireData (response);
			if (string.IsNullOrWhiteSpace (data.Token) || data.ExpiresInSeconds <= 0
				 || data.ExpiresInSeconds > (DateTimeOffset.MaxValue - started).TotalSeconds)
				{
				throw new RainPointException ("The refresh response did not contain a usable session and expiry.");
				}
			double margin = Math.Min (60, data.ExpiresInSeconds / 10.0);
			Session refreshed = new (data.Token!, started.AddSeconds (data.ExpiresInSeconds - margin),
				 string.IsNullOrWhiteSpace (data.RefreshToken) ? original.RefreshToken : data.RefreshToken, original.Notifications, original.Profile, original.Observer);
			if (!ReferenceEquals (Interlocked.CompareExchange (ref _session, refreshed, original), original))
				{
				throw new InvalidOperationException ("The session was invalidated while its refresh was pending; sign in again.");
				}
			}
		finally
			{
			_ = _loginGate.Release ();
			}
		}

	/// <summary>Logs out remotely and clears the matching local session even if the request fails.</summary>
	public async Task LogoutAsync (CancellationToken cancellationToken = default)
		{
		if (Volatile.Read (ref _sessionRecoveryActive) != 0)
			throw new InvalidOperationException ("Stop session recovery before signing out.");
		ThrowIfDisposed ();
		await _loginGate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			Session? session = Volatile.Read (ref _session);
			if (session is null)
				{
				return;
				}
			try
				{
				ApiResult response = await SendAsync<EmptyRequest, ApiResult> (HttpMethod.Post, "auth/basic/app/logOut",
					 new EmptyRequest (), session, cancellationToken).ConfigureAwait (false);
				CheckResult (response, session);
				}
			finally
				{
				_ = Interlocked.CompareExchange (ref _session, null, session);
				}
			}
		finally
			{
			_ = _loginGate.Release ();
			}
		}
	}
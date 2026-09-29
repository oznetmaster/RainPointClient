// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using RainPointClient.Protocol;
namespace RainPointClient;

/// <summary>Account notification settings observed at sign-in. This does not register an OS push receiver.</summary>
public sealed class RainPointNotificationPreferences
	{
	/// <summary>
	/// Initializes notification preferences from the supplied typed values.
	/// </summary>
	/// <param name="flags">The reported bit field, including unknown bits that must be preserved on update.</param>
	internal RainPointNotificationPreferences (int flags)
		{
		Flags = flags;
		}
	/// <summary>
	/// Stores the original bit field, including unknown bits.
	/// </summary>
	internal int Flags
		{
		get;
		}
	/// <summary>
	/// Tracks whether this observation has already been used for a write attempt.
	/// </summary>
	internal int Attempted;
	/// <summary>
	/// Gets whether the known mobile-notification flag is enabled.
	/// </summary>
	public bool MobileEnabled => (Flags & 1) != 0;
	/// <summary>
	/// Gets whether the known email-notification flag is enabled.
	/// </summary>
	public bool EmailEnabled => (Flags & 2) != 0;
	/// <summary>
	/// Gets uninterpreted preference bits retained when known notification flags are changed.
	/// </summary>
	public int UnknownFlags => Flags & ~3;
	}
public sealed partial class RainPointCloudClient
	{
	/// <summary>The sign-in observation, or null if omitted or already used for a write. Sign in again to refresh.
	/// Other app sessions may change it. Cloud acceptance does not prove notification delivery.</summary>
	public RainPointNotificationPreferences? NotificationPreferences
		{
		get
			{
			var session = Volatile.Read (ref _session);
			return session?.ExpiresAt > DateTimeOffset.UtcNow && session.Notifications?.Attempted == 0 ? session.Notifications : null;
			}
		}
	/// <summary>Changes the two known notification flags, preserving unknown bits from sign-in. One attempt per observation.</summary>
	/// <param name="expected">An unused, current notification preferences observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="mobileEnabled">Whether the known mobile-notification preference should be enabled.</param>
	/// <param name="emailEnabled">Whether the known email-notification preference should be enabled.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="System.ArgumentNullException">A required argument is null.</exception>
	/// <exception cref="System.InvalidOperationException">Use this session's sign-in notification observation. Sign in again before another notification write.</exception>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task SetNotificationPreferencesAsync (RainPointNotificationPreferences expected, bool mobileEnabled, bool emailEnabled, CancellationToken cancellationToken = default)
		{
		if (expected is null)
			throw new ArgumentNullException (nameof (expected));
		Session session = GetSession ();
		if (!ReferenceEquals (session.Notifications, expected))
			throw new InvalidOperationException ("Use this session's sign-in notification observation.");
		cancellationToken.ThrowIfCancellationRequested ();
		if (Interlocked.CompareExchange (ref expected.Attempted, 1, 0) != 0)
			throw new InvalidOperationException ("Sign in again before another notification write.");
		var result = await SendAsync<NotificationRequest, ApiResult> (HttpMethod.Post, "app/member/user/info/set",
		 new NotificationRequest { Notice = (expected.Flags & ~3) | (mobileEnabled ? 1 : 0) | (emailEnabled ? 2 : 0) }, session, cancellationToken).ConfigureAwait (false);
		CheckResult (result, session);
		}
	private sealed class NotificationRequest
		{
		/// <summary>
		/// Stores the notice protocol field for notification request.
		/// </summary>
		[JsonPropertyName ("notice")]
		public int Notice
			{
			get; set;
			}
		}
	}
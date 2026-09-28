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
	internal RainPointNotificationPreferences (int flags)
		{
		Flags = flags;
		}
	internal int Flags
		{
		get;
		}
	internal int Attempted;
	public bool MobileEnabled => (Flags & 1) != 0;
	public bool EmailEnabled => (Flags & 2) != 0;
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
		[JsonPropertyName ("notice")]
		public int Notice
			{
			get; set;
			}
		}
	}
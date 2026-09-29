// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol/compatibility reference: https://github.com/brettmeyerowitz/homeassistant-homgar
// Additional reference: https://github.com/macher91/homgar-homeassistant
// Independently written C# implementation. See ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace RainPointClient.Protocol
	{
	internal sealed class ObserverRequest
		{
		[JsonPropertyName ("hid")] public string HomeId { get; set; } = string.Empty;
		[JsonPropertyName ("hidList")] public string[] Homes { get; set; } = [];
		[JsonPropertyName ("subscribe")] public HubAddress[] Subscribe { get; set; } = [];
		[JsonPropertyName ("unsubscribe")] public HubAddress[] Unsubscribe { get; set; } = [];
		[JsonPropertyName ("userInfo")] public ObserverUser User { get; set; } = new ();
		}
	internal sealed class ObserverUser
		{
		[JsonPropertyName ("deviceName")] public string DeviceName { get; set; } = string.Empty;
		[JsonPropertyName ("productKey")] public string ProductKey { get; set; } = string.Empty;
		[JsonPropertyName ("deviceType")] public int DeviceType { get; set; } = 1;
		[JsonPropertyName ("notice")]
		public int Notice
			{
			get; set;
			}
		[JsonPropertyName ("pushId")] public string PushId { get; set; } = Guid.NewGuid ().ToString ("N");
		}
	internal sealed class ObserverCredentials
		{
		[JsonPropertyName ("deviceName")] public string DeviceName { get; set; } = string.Empty;
		[JsonPropertyName ("productKey")] public string ProductKey { get; set; } = string.Empty;
		[JsonPropertyName ("deviceSecret")] public string DeviceSecret { get; set; } = string.Empty;
		[JsonPropertyName ("mqttHostUrl")]
		public string? HostUrl
			{
			get; set;
			}
		[JsonPropertyName ("expire")]
		public long? ExpiresAt
			{
			get; set;
			}
		[JsonIgnore]
		internal object? SessionIdentity
			{
			get; set;
			}
		}
	}

namespace RainPointClient
	{
	public sealed partial class RainPointCloudClient
		{
		// Account MQTT credentials remain private and follow the authenticated session across refreshes.
		private static Protocol.ObserverCredentials? ObserverIdentity (Protocol.LoginUser? user) =>
			string.IsNullOrWhiteSpace (user?.DeviceName) || string.IsNullOrWhiteSpace (user?.ProductKey) || string.IsNullOrWhiteSpace (user?.DeviceSecret)
				? null : new Protocol.ObserverCredentials { DeviceName = user!.DeviceName!, ProductKey = user.ProductKey!, DeviceSecret = user.DeviceSecret! };

		private int _monitorActive;
		internal object? SessionIdentity => Volatile.Read (ref _session);
		internal bool AcquireMonitor () => Interlocked.CompareExchange (ref _monitorActive, 1, 0) == 0;
		internal void ReleaseMonitor () => Volatile.Write (ref _monitorActive, 0);

		internal async Task<Protocol.ObserverCredentials> GetObserverAsync (RainPointHub hub, CancellationToken token)
			{
			ValidateHub (hub);
			if (hub.HomeId <= 0)
				throw new ArgumentException ("Discover the hub through its home first.", nameof (hub));
			Session session = GetSession ();
			string home = hub.HomeId.ToString (CultureInfo.InvariantCulture);
			Protocol.ObserverRequest request = new ()
				{
				HomeId = home,
				Homes = [home],
				Subscribe = [new Protocol.HubAddress { Id = hub.Id, DeviceName = hub.DeviceName, ProductKey = hub.ProductKey }],
				User = new Protocol.ObserverUser
					{
					DeviceName = session.Observer?.DeviceName ?? hub.DeviceName,
					ProductKey = session.Observer?.ProductKey ?? hub.ProductKey,
					Notice = session.Notifications?.Flags ?? 0
					}
				};
			Protocol.ApiResult<Protocol.ObserverCredentials> result = await SendAsync<Protocol.ObserverRequest, Protocol.ApiResult<Protocol.ObserverCredentials>> (
				 HttpMethod.Post, "app/device/subscribeStatus", request, session, token).ConfigureAwait (false);
			CheckResult (result, session);
			Protocol.ObserverCredentials response = RequireData (result);
			// An account registration may return just expiry/status versions. Connect with the
			// account identity used in userInfo, rather than a hub's temporary status observer.
			Protocol.ObserverCredentials credentials = session.Observer is null ? response : new Protocol.ObserverCredentials
				{
				DeviceName = session.Observer.DeviceName,
				ProductKey = session.Observer.ProductKey,
				DeviceSecret = session.Observer.DeviceSecret,
				ExpiresAt = response.ExpiresAt
				};
			if (string.IsNullOrWhiteSpace (credentials.DeviceName) || string.IsNullOrWhiteSpace (credentials.ProductKey)
				 || string.IsNullOrWhiteSpace (credentials.DeviceSecret))
				throw new RainPointException ("The cloud omitted required observer credentials.");
			credentials.SessionIdentity = session;
			return credentials;
			}
		}
	}
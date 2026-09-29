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
	/// <summary>
	/// Internal observer request representation or processing contract for the RainPoint protocol.
	/// </summary>
	internal sealed class ObserverRequest
		{
		/// <summary>
		/// Stores the hid protocol field for observer request.
		/// </summary>
		[JsonPropertyName ("hid")] public string HomeId { get; set; } = string.Empty;
		/// <summary>
		/// Stores the hidList protocol field for observer request.
		/// </summary>
		[JsonPropertyName ("hidList")] public string[] Homes { get; set; } = [];
		/// <summary>
		/// Stores the subscribe protocol field for observer request.
		/// </summary>
		[JsonPropertyName ("subscribe")] public HubAddress[] Subscribe { get; set; } = [];
		/// <summary>
		/// Stores the unsubscribe protocol field for observer request.
		/// </summary>
		[JsonPropertyName ("unsubscribe")] public HubAddress[] Unsubscribe { get; set; } = [];
		/// <summary>
		/// Stores the userInfo protocol field for observer request.
		/// </summary>
		[JsonPropertyName ("userInfo")] public ObserverUser User { get; set; } = new ();
		}
	/// <summary>
	/// Internal observer user representation or processing contract for the RainPoint protocol.
	/// </summary>
	internal sealed class ObserverUser
		{
		/// <summary>
		/// Stores the deviceName protocol field for observer user.
		/// </summary>
		[JsonPropertyName ("deviceName")] public string DeviceName { get; set; } = string.Empty;
		/// <summary>
		/// Stores the productKey protocol field for observer user.
		/// </summary>
		[JsonPropertyName ("productKey")] public string ProductKey { get; set; } = string.Empty;
		/// <summary>
		/// Stores the deviceType protocol field for observer user.
		/// </summary>
		[JsonPropertyName ("deviceType")] public int DeviceType { get; set; } = 1;
		/// <summary>
		/// Stores the notice protocol field for observer user.
		/// </summary>
		[JsonPropertyName ("notice")]
		public int Notice
			{
			get; set;
			}
		/// <summary>
		/// Stores the pushId protocol field for observer user.
		/// </summary>
		[JsonPropertyName ("pushId")] public string PushId { get; set; } = Guid.NewGuid ().ToString ("N");
		}
	/// <summary>
	/// Internal observer credentials representation or processing contract for the RainPoint protocol.
	/// </summary>
	internal sealed class ObserverCredentials
		{
		/// <summary>
		/// Stores the deviceName protocol field for observer credentials.
		/// </summary>
		[JsonPropertyName ("deviceName")] public string DeviceName { get; set; } = string.Empty;
		/// <summary>
		/// Stores the productKey protocol field for observer credentials.
		/// </summary>
		[JsonPropertyName ("productKey")] public string ProductKey { get; set; } = string.Empty;
		/// <summary>
		/// Stores the deviceSecret protocol field for observer credentials.
		/// </summary>
		[JsonPropertyName ("deviceSecret")] public string DeviceSecret { get; set; } = string.Empty;
		/// <summary>
		/// Stores the mqttHostUrl protocol field for observer credentials.
		/// </summary>
		[JsonPropertyName ("mqttHostUrl")]
		public string? HostUrl
			{
			get; set;
			}
		/// <summary>
		/// Stores the expire protocol field for observer credentials.
		/// </summary>
		[JsonPropertyName ("expire")]
		public long? ExpiresAt
			{
			get; set;
			}
		/// <summary>
		/// Stores the session identity for observer credentials.
		/// </summary>
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
		/// <summary>
		/// Gets the current session identity token used to reject observations from a replaced session.
		/// </summary>
		internal object? SessionIdentity => Volatile.Read (ref _session);
		/// <summary>
		/// Claims the single monitor slot for this client.
		/// </summary>
		/// <returns>True if this observer claimed the slot; false if another monitor already owns it.</returns>
		internal bool AcquireMonitor () => Interlocked.CompareExchange (ref _monitorActive, 1, 0) == 0;
		/// <summary>
		/// Releases the monitor slot when observation stops.
		/// </summary>
		internal void ReleaseMonitor () => Volatile.Write (ref _monitorActive, 0);

		/// <summary>
		/// Obtains session-bound MQTT observer credentials for the discovered hub's home.
		/// </summary>
		/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
		/// <param name="token">Cancellation for this operation.</param>
		/// <returns>A task containing the typed observer credentials result.</returns>
		/// <exception cref="System.ArgumentException">Discover the hub through its home first.</exception>
		/// <exception cref="RainPointException">The cloud omitted required observer credentials.</exception>
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
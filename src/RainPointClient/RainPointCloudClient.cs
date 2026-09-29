// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol/compatibility reference: https://github.com/funkadelic/ha-rainpoint
// Additional reference: https://github.com/brettmeyerowitz/homeassistant-homgar
// Independently written C# implementation. See ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using RainPointClient.Protocol;

namespace RainPointClient;

/// <summary>A RainPoint Home / Smart+ cloud client. All network operations are explicit.</summary>
/// <remarks>
/// Sign in explicitly before reading or controlling devices. No command is automatically retried.
/// A caller-supplied HttpClient remains owned by the caller; configure its timeout and redirect policy.
/// Finish outstanding operations before disposing this client.
/// </remarks>
public sealed partial class RainPointCloudClient : IDisposable
	{
	private static readonly JsonSerializerOptions _json = new ()
		{
		NumberHandling = JsonNumberHandling.AllowReadingFromString
		};

	private readonly HttpClient _http;
	private readonly bool _ownsHttp;
	private readonly Uri _serviceAddress;
	private readonly SemaphoreSlim _loginGate = new (1, 1);
	private Session? _session;
	private DateTimeOffset _loginNotBefore;
	private bool _disposed;

	public RainPointCloudClient (HttpClient? httpClient = null, Uri? serviceAddress = null)
		{
		_serviceAddress = serviceAddress ?? new Uri ("https://region3.homgarus.com/");
		if (!_serviceAddress.IsAbsoluteUri || _serviceAddress.Scheme != Uri.UriSchemeHttps
			 || !string.IsNullOrEmpty (_serviceAddress.UserInfo) || _serviceAddress.AbsolutePath != "/"
			 || !string.IsNullOrEmpty (_serviceAddress.Query) || !string.IsNullOrEmpty (_serviceAddress.Fragment))
			{
			throw new ArgumentException ("The service address must be an HTTPS origin.", nameof (serviceAddress));
			}

		_ownsHttp = httpClient is null;
		_http = httpClient ?? new HttpClient (new HttpClientHandler { AllowAutoRedirect = false })
			{
			Timeout = TimeSpan.FromSeconds (30),
			MaxResponseContentBufferSize = 4 * 1024 * 1024
			};
		}

	/// <summary>Logs into appCode 2. Area code is the account's country calling code, e.g. "44".</summary>
	/// <remarks>Credentials are not retained. A login can displace an existing app session on the same account.</remarks>
	public Task LoginAsync (string email, string password, string areaCode, CancellationToken cancellationToken = default)
		{
		if (Volatile.Read (ref _sessionRecoveryActive) != 0)
			throw new InvalidOperationException ("Stop session recovery before signing in explicitly.");
		return LoginCoreAsync (email, password, areaCode, cancellationToken);
		}
	internal Task LoginForRecoveryAsync (RainPointCredentials credentials, CancellationToken token) => LoginCoreAsync (credentials.Email, credentials.Password, credentials.AreaCode, token);
	private async Task LoginCoreAsync (string email, string password, string areaCode, CancellationToken cancellationToken)
		{
		ThrowIfDisposed ();
		RequireText (email, nameof (email));
		RequireText (password, nameof (password));
		RequireText (areaCode, nameof (areaCode));
		if (areaCode.Any (c => c is < '0' or > '9'))
			{
			throw new ArgumentException ("Use the numeric country calling code without '+'.", nameof (areaCode));
			}

		await _loginGate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			if (DateTimeOffset.UtcNow < _loginNotBefore)
				{
				throw new RainPointException ("Login is cooling down after a server throttle.",
					 retryAfter: _loginNotBefore - DateTimeOffset.UtcNow);
				}

			// MD5 is required by this upstream wire protocol, not used as password storage.
			LoginRequest request = new ()
				{
				Email = email,
				PasswordHash = Hash (password),
				AreaCode = areaCode,
				DeviceId = Hash (email + areaCode)
				};
			// A failed attempt to switch accounts must not leave the old account active.
			_ = Interlocked.Exchange (ref _session, null);
			DateTimeOffset started = DateTimeOffset.UtcNow;
			ApiResult<LoginResponse> result = await SendAsync<LoginRequest, ApiResult<LoginResponse>> (
				 HttpMethod.Post, "auth/basic/app/login", request, null, cancellationToken).ConfigureAwait (false);
			CheckResult (result, null);
			LoginResponse data = RequireData (result);
			if (string.IsNullOrWhiteSpace (data.Token) || data.ExpiresInSeconds <= 0
				 || data.ExpiresInSeconds > (DateTimeOffset.MaxValue - started).TotalSeconds)
				{
				throw new RainPointException ("The login response did not contain a usable session.");
				}

			// Use local receipt timing rather than trusting a remote wall clock.
			double margin = Math.Min (60, data.ExpiresInSeconds / 10.0);
			_ = Interlocked.Exchange (ref _session, new Session (data.Token!, started.AddSeconds (data.ExpiresInSeconds - margin), data.RefreshToken, data.User?.Notice is >= 0 ? new RainPointNotificationPreferences (data.User.Notice.Value) : null, data.User is null ? null : new RainPointAccountProfile (data.User), ObserverIdentity (data.User)));
			}
		catch (RainPointException error) when (error.RetryAfter.HasValue)
			{
			_loginNotBefore = DateTimeOffset.UtcNow + error.RetryAfter.Value;
			throw;
			}
		finally
			{
			_ = _loginGate.Release ();
			}
		}

	public async Task<IReadOnlyList<RainPointHome>> GetHomesAsync (CancellationToken cancellationToken = default)
		{
		List<RainPointHome> homes = await GetAsync<List<RainPointHome>> (
			 "app/member/appHome/list", cancellationToken).ConfigureAwait (false);
		return homes.Any (home => home is null || home.Id <= 0)
			? throw new RainPointException ("The home list contained an invalid identifier.")
			: (IReadOnlyList<RainPointHome>)homes.AsReadOnly ();
		}

	public async Task<IReadOnlyList<RainPointHub>> GetHubsAsync (long homeId, CancellationToken cancellationToken = default)
		{
		if (homeId <= 0)
			{
			throw new ArgumentOutOfRangeException (nameof (homeId));
			}

		List<RainPointHub> hubs = await GetAsync<List<RainPointHub>> (
			 "app/device/getDeviceByHid?hid=" + homeId.ToString (CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait (false);
		foreach (RainPointHub hub in hubs)
			{
			if (hub is null || hub.Id <= 0 || hub.Devices is null
				 || hub.Devices.Any (device => device is null || device.Address <= 0 || string.IsNullOrWhiteSpace (device.Model))
				 || hub.Devices.Select (device => device.Address).Distinct ().Count () != hub.Devices.Count)
				{
				throw new RainPointException ("The device list contained invalid or ambiguous addressing.");
				}

			hub.Devices = Array.AsReadOnly (hub.Devices.ToArray ());
			hub.HomeId = homeId;
			}

		return hubs.AsReadOnly ();
		}

	/// <summary>Reads a supported RF timer through its discovered parent hub.</summary>
	public async Task<RainPointTimerStatus> GetTimerStatusAsync (RainPointHub hub, int address,
		 CancellationToken cancellationToken = default)
		{
		RainPointDevice timer = GetTimer (hub, address);
		BatchHubStatusResponse? response = await ReadHubStatusAsync (hub, cancellationToken).ConfigureAwait (false);
		return DecodeTimer (timer, response?.Devices ?? []);
		}

	/// <summary>Reads hub connectivity, Wi-Fi signal and all supported timers in one cloud request.</summary>
	public async Task<RainPointHubStatus> GetHubStatusAsync (RainPointHub hub, CancellationToken cancellationToken = default)
		{
		ValidateHub (hub);
		BatchHubStatusResponse? response = await ReadHubStatusAsync (hub, cancellationToken).ConfigureAwait (false);
		List<DeviceStatusResponse> entries = response?.Devices ?? [];
		DeviceStatusResponse? connected = FindStatus (entries, "connected");
		string? state = FindStatus (entries, "state")?.Value;
		int? signal = null;
		string[]? fields = state?.Split (',');
		if (fields?.Length == 2 && int.TryParse (fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rssi) && rssi is >= -127 and <= 0)
			{
			signal = rssi;
			}
		return new RainPointHubStatus (hub.Id, connected?.Value switch
			{
				"1" => true,
				"0" => false,
				_ => null
				}, signal,
			 DecodeTime (connected?.Time), Array.AsReadOnly (hub.Devices.Where (device => device.SupportedZoneCount.HasValue)
			 .Select (device => DecodeTimer (device, entries)).ToArray ()));
		}

	private async Task<BatchHubStatusResponse?> ReadHubStatusAsync (RainPointHub hub, CancellationToken cancellationToken)
		{
		Session session = GetSession ();
		BatchStatusRequest request = new ()
			{
			Devices = [new HubAddress { Id = hub.Id, DeviceName = hub.DeviceName, ProductKey = hub.ProductKey }]
			};
		ApiResult<List<BatchHubStatusResponse>> result = await SendAsync<BatchStatusRequest, ApiResult<List<BatchHubStatusResponse>>> (
			 HttpMethod.Post, "app/device/multipleDeviceStatus", request, session, cancellationToken).ConfigureAwait (false);
		CheckResult (result, session);
		List<BatchHubStatusResponse> hubs = RequireData (result);
		if (hubs.Any (item => item is null || item.Id <= 0 || item.Devices is null))
			{
			throw new RainPointException ("The status response contained an invalid hub record.");
			}

		BatchHubStatusResponse[] matchingHubs = [.. hubs.Where (item => item.Id == hub.Id)];
		if (matchingHubs.Length > 1)
			{
			throw new RainPointException ("The status response contained duplicate hub entries.");
			}

		return matchingHubs.FirstOrDefault ();
		}

	private static DeviceStatusResponse? FindStatus (IReadOnlyList<DeviceStatusResponse> entries, string id)
		{
		DeviceStatusResponse[] matches = [.. entries.Where (item => item is not null && item.Id == id)];
		if (matches.Length > 1)
			{
			throw new RainPointException ("The status response contained duplicate reading entries.");
			}

		return matches.FirstOrDefault ();
		}

	private static RainPointTimerStatus DecodeTimer (RainPointDevice timer, IReadOnlyList<DeviceStatusResponse> entries)
		{
		DeviceStatusResponse? entry = FindStatus (entries, "D" + timer.Address.ToString ("D2", CultureInfo.InvariantCulture));
		return TimerDecoder.Decode (timer.Address, timer.SupportedZoneCount!.Value, entry?.Value, DecodeTime (entry?.Time));
		}

	private static DateTimeOffset? DecodeTime (long? value)
		{
		DateTimeOffset? changed = null;
		if (value is long timestamp && timestamp > 0)
			{
			try
				{
				changed = DateTimeOffset.FromUnixTimeMilliseconds (timestamp);
				}
			catch (ArgumentOutOfRangeException)
				{
				// An unusable optional timestamp must not fabricate a last-seen time.
				}
			}

		return changed;
		}

	/// <summary>Starts normal irrigation for 60..43200 whole seconds, matching the HTV345FRF manual range.</summary>
	public Task<RainPointWateringCommandResult> StartWateringAsync (RainPointHub hub, int address, int zone,
		 TimeSpan duration, CancellationToken cancellationToken = default)
		{
		return duration.Ticks % TimeSpan.TicksPerSecond != 0 || duration.TotalSeconds < 60 || duration.TotalSeconds > 43200
			? throw new ArgumentOutOfRangeException (nameof (duration), "Normal irrigation requires a whole number of seconds between 60 and 43200.")
			: ControlAsync (hub, address, zone, 1, (int)duration.TotalSeconds, cancellationToken);
		}

	public Task<RainPointWateringCommandResult> StopWateringAsync (RainPointHub hub, int address, int zone,
		 CancellationToken cancellationToken = default) => ControlAsync (hub, address, zone, 0, 0, cancellationToken);

	private async Task<RainPointWateringCommandResult> ControlAsync (RainPointHub hub, int address, int zone,
		 int mode, int duration, CancellationToken cancellationToken, string parameter = "")
		{
		RainPointDevice timer = GetTimer (hub, address);
		if (zone < 1 || zone > timer.SupportedZoneCount)
			{
			throw new ArgumentOutOfRangeException (nameof (zone));
			}

		if (mode is 2 or 3 && !timer.SupportsManualCycles)
			throw new NotSupportedException ("Manual misting and cycle-and-soak require HTV345FRF firmware 120 or newer. Refresh discovery if firmware is unknown.");

		Session session = GetSession ();
		ValveCommand command = new ()
			{
			HubId = hub.Id,
			Address = address,
			DeviceName = hub.DeviceName,
			ProductKey = hub.ProductKey,
			Zone = zone,
			Mode = mode,
			Duration = duration,
			Parameter = parameter
			};
		ApiResult<CommandData> result = await SendAsync<ValveCommand, ApiResult<CommandData>> (HttpMethod.Post,
			 "app/device/controlWorkMode", command, session, cancellationToken).ConfigureAwait (false);
		if (result.Code != 4)
			CheckResult (result, session);

		RainPointCommandOutcome outcome = result.Code == 4
			 ? RainPointCommandOutcome.AlreadyInRequestedStateOrTransitioning : RainPointCommandOutcome.Accepted;
		CommandData? data = result.Data;
		RainPointTimerStatus status = data?.Unavailable is TimerReadingAvailability unavailable
			 ? new RainPointTimerStatus (address, unavailable, [])
			 : TimerDecoder.Decode (address, timer.SupportedZoneCount!.Value, data?.Reading?.State, null);
		return new RainPointWateringCommandResult (outcome, zone, status, DecodeTime (data?.Reading?.Timestamp));
		}

	private static RainPointDevice GetTimer (RainPointHub hub, int address)
		{
		if (hub is null)
			{
			throw new ArgumentNullException (nameof (hub));
			}

		if (hub.Id <= 0 || string.IsNullOrWhiteSpace (hub.DeviceName) || string.IsNullOrWhiteSpace (hub.ProductKey))
			{
			throw new ArgumentException ("Use a discovered RF hub with complete addressing.", nameof (hub));
			}

		RainPointDevice? timer = hub.Devices.FirstOrDefault (device => device.Address == address) ?? throw new ArgumentException ("The timer address is not paired with this hub.", nameof (address));

		return !timer.SupportedZoneCount.HasValue
			? throw new NotSupportedException ("This client supports HTV145FRF, HTV245FRF and HTV345FRF RF timers.")
			: timer;
		}

	private async Task<T> GetAsync<T> (string path, CancellationToken cancellationToken) where T : class
		{
		Session session = GetSession ();
		ApiResult<T> result = await SendAsync<LoginRequest, ApiResult<T>> (HttpMethod.Get, path,
			 null, session, cancellationToken).ConfigureAwait (false);
		CheckResult (result, session);
		return RequireData (result);
		}

	private async Task<TResponse> SendAsync<TRequest, TResponse> (HttpMethod method, string path,
		 TRequest? body, Session? session, CancellationToken cancellationToken, long? homeId = null) where TRequest : class where TResponse : class
		{
		ThrowIfDisposed ();
		using HttpRequestMessage request = new (method, new Uri (_serviceAddress, path));
		request.Headers.Add ("appCode", "2");
		request.Headers.Add ("lang", "en");
		if (homeId.HasValue)
			request.Headers.Add ("hid", IdText (homeId.Value));
		request.Headers.UserAgent.ParseAdd ("RainPointClient/0.1");
		if (session is not null)
			{
			request.Headers.Add ("auth", session.Token);
			request.Headers.Add ("version", "1.16.1065");
			request.Headers.Add ("sceneType", "1");
			}

		if (body is not null)
			{
			request.Content = new StringContent (JsonSerializer.Serialize (body, _json), Encoding.UTF8, "application/json");
			}

		using HttpResponseMessage response = await _http.SendAsync (request,
			 HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait (false);
		if (!response.IsSuccessStatusCode)
			{
			if (response.StatusCode == HttpStatusCode.Unauthorized && session is not null)
				{
				_ = Interlocked.CompareExchange (ref _session, null, session);
				}

			TimeSpan? retry = null;
			if ((int)response.StatusCode == 429 || (session is null && response.StatusCode == HttpStatusCode.Forbidden))
				{
				TimeSpan suggested = response.Headers.RetryAfter?.Delta
					 ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.Zero;
				retry = suggested > TimeSpan.FromSeconds (120) ? suggested : TimeSpan.FromSeconds (120);
				}

			throw new RainPointException ("The cloud request failed with HTTP " + (int)response.StatusCode + ".",
				 httpStatus: response.StatusCode, retryAfter: retry);
			}

		try
			{
			using System.IO.Stream stream = await response.Content.ReadAsStreamAsync ().ConfigureAwait (false);
			return await JsonSerializer.DeserializeAsync<TResponse> (stream, _json, cancellationToken).ConfigureAwait (false)
				 ?? throw new RainPointException ("The cloud returned an empty JSON response.");
			}
		catch (JsonException)
			{
			throw new RainPointException ("The cloud response did not match the expected protocol model.");
			}
		}

	private void CheckResult (ApiResult result, Session? session)
		{
		if (result.Code == 0)
			{
			return;
			}

		if (result.Code is 1001 or 1004 && session is not null)
			{
			_ = Interlocked.CompareExchange (ref _session, null, session);
			}

		throw new RainPointException ("The cloud rejected the request with code " + result.Code + ".", result.Code,
			 retryAfter: result.Code == 9993 ? TimeSpan.FromSeconds (120) : null);
		}

	private static T RequireData<T> (ApiResult<T> result) where T : class =>
		 result.Data ?? throw new RainPointException ("The cloud response omitted its required data.");

	private Session GetSession ()
		{
		ThrowIfDisposed ();
		Session? session = Volatile.Read (ref _session);
		return session is null || session.ExpiresAt <= DateTimeOffset.UtcNow
			? throw new InvalidOperationException ("Call LoginAsync before making requests; the session is missing or expired.")
			: session;
		}

	private static void RequireText (string text, string parameter)
		{
		if (string.IsNullOrWhiteSpace (text))
			{
			throw new ArgumentException ("A non-empty value is required.", parameter);
			}
		}

	private static string Hash (string text)
		{
		using MD5 md5 = MD5.Create ();
		return BitConverter.ToString (md5.ComputeHash (Encoding.UTF8.GetBytes (text))).Replace ("-", "").ToLowerInvariant ();
		}

	private void ThrowIfDisposed ()
		{
		if (_disposed)
			{
			throw new ObjectDisposedException (nameof (RainPointCloudClient));
			}
		}

	public void Dispose ()
		{
		if (_disposed)
			{
			return;
			}

		_disposed = true;
		_ = Interlocked.Exchange (ref _session, null);
		_loginGate.Dispose ();
		if (_ownsHttp)
			{
			_http.Dispose ();
			}
		}

	private sealed class Session (string token, DateTimeOffset expiresAt, string? refreshToken = null, RainPointNotificationPreferences? notifications = null, RainPointAccountProfile? profile = null, Protocol.ObserverCredentials? observer = null)
		{
		internal RainPointNotificationPreferences? Notifications { get; } = notifications;
		internal RainPointAccountProfile? Profile { get; } = profile;
		internal Protocol.ObserverCredentials? Observer { get; } = observer;
		internal string Token { get; } = token;
		internal DateTimeOffset ExpiresAt { get; } = expiresAt;
		internal string? RefreshToken { get; } = refreshToken;
		}
	}
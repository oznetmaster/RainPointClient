// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol/compatibility reference: https://github.com/brettmeyerowitz/homeassistant-homgar
// Additional reference: https://github.com/macher91/homgar-homeassistant
// Independently written C# implementation. See ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;

namespace RainPointClient.Protocol;

/// <summary>
/// Internal iobserver transport representation or processing contract for the RainPoint protocol.
/// </summary>
internal interface IObserverTransport
	{
	/// <summary>
	/// Runs one authenticated observer connection and forwards connection and payload callbacks.
	/// </summary>
	/// <param name="credentials">The session-bound observer credentials.</param>
	/// <param name="connected">Callback invoked after a successful MQTT connection.</param>
	/// <param name="received">Callback receiving an MQTT message payload.</param>
	/// <param name="token">Cancellation for this operation.</param>
	/// <returns>A task representing the observer connection lifetime.</returns>
	Task RunAsync (ObserverCredentials credentials, Action connected, Action<byte[]> received, CancellationToken token);
	}

/// <summary>
/// Internal mqtt observer transport representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class MqttObserverTransport : IObserverTransport
	{
	private readonly Func<IMqttClient> _createClient;
	/// <summary>
	/// Initializes mqtt observer transport from the supplied typed values.
	/// </summary>
	/// <param name="createClient">An optional MQTT client factory; null uses the production factory.</param>
	internal MqttObserverTransport (Func<IMqttClient>? createClient = null) => _createClient = createClient ?? (() => new MqttFactory ().CreateMqttClient ());

	/// <summary>
	/// Runs an MQTT observer connection using session-bound credentials and TLS validation.
	/// </summary>
	/// <param name="credentials">The session-bound observer credentials.</param>
	/// <param name="connected">Callback invoked after a successful MQTT connection.</param>
	/// <param name="received">Callback receiving an MQTT message payload.</param>
	/// <param name="token">Cancellation for this operation.</param>
	/// <returns>A task representing the observer connection lifetime.</returns>
	/// <exception cref="RainPointException">The MQTT observer connection was rejected.</exception>
	public async Task RunAsync (ObserverCredentials credentials, Action connected, Action<byte[]> received, CancellationToken token)
		{
		using IMqttClient mqtt = _createClient ();
		using X509Certificate2 root = LoadRoot ();
		TaskCompletionSource<bool> disconnected = new (TaskCreationOptions.RunContinuationsAsynchronously);
		mqtt.DisconnectedAsync += _ => { disconnected.TrySetResult (true); return Task.CompletedTask; };
		string topic = $"/sys/{credentials.ProductKey}/{credentials.DeviceName}/thing/service/property/set";
		mqtt.ApplicationMessageReceivedAsync += message =>
		{
			ArraySegment<byte> payload = message.ApplicationMessage.PayloadSegment;
			if (message.ApplicationMessage.Topic == topic && payload.Count is > 0 and <= 8192 && payload.Array is not null)
				{
				byte[] copy = new byte[payload.Count];
				Array.Copy (payload.Array, payload.Offset, copy, 0, payload.Count);
				received (copy);
				}
			return Task.CompletedTask;
		};
		using CancellationTokenRegistration registration = token.Register (() => disconnected.TrySetCanceled ());
		try
			{
			MqttClientOptions options = BuildOptions (credentials, DateTimeOffset.UtcNow, root);
			MqttClientConnectResult result = await mqtt.ConnectAsync (options, token).ConfigureAwait (false);
			if (result.ResultCode != MqttClientConnectResultCode.Success)
				throw new RainPointException ("The MQTT observer connection was rejected.");
			connected ();
			// This observer receives unsolicited downlinks. SUBSCRIBE is forbidden by the broker policy.
			await disconnected.Task.ConfigureAwait (false);
			}
		finally
			{
			if (mqtt.IsConnected)
				{
				using CancellationTokenSource cleanup = new (TimeSpan.FromSeconds (5));
				try
					{
					await mqtt.DisconnectAsync (new MqttClientDisconnectOptions (), cleanup.Token).ConfigureAwait (false);
					}
				catch (Exception error) when (error is not OutOfMemoryException) { }
				}
			}
		}

	/// <summary>
	/// Builds MQTT connection options from validated observer credentials and the trusted root.
	/// </summary>
	/// <param name="credentials">The session-bound observer credentials.</param>
	/// <param name="now">The current instant used for expiry and timestamp checks.</param>
	/// <param name="root">The trusted IoT root certificate used by observer TLS validation.</param>
	/// <returns>The configured MQTT client options.</returns>
	/// <exception cref="RainPointException">The observer identity is malformed.</exception>
	internal static MqttClientOptions BuildOptions (ObserverCredentials credentials, DateTimeOffset now, X509Certificate2 root)
		{
		string host = BrokerHost (credentials);
		if (credentials.DeviceName.Length > 128 || credentials.ProductKey.Length > 128
			 || credentials.DeviceName.IndexOfAny (['|', ',', '&']) >= 0 || credentials.ProductKey.IndexOfAny (['|', ',', '&']) >= 0)
			throw new RainPointException ("The observer identity is malformed.");
		string stamp = now.ToUnixTimeMilliseconds ().ToString (CultureInfo.InvariantCulture);
		string signed = $"clientId{credentials.DeviceName}deviceName{credentials.DeviceName}productKey{credentials.ProductKey}timestamp{stamp}";
		using HMACSHA1 hmac = new (Encoding.UTF8.GetBytes (credentials.DeviceSecret));
		string password = BitConverter.ToString (hmac.ComputeHash (Encoding.UTF8.GetBytes (signed))).Replace ("-", "").ToLowerInvariant ();
		return new MqttClientOptionsBuilder ()
			 .WithTcpServer (host, 8883)
			 .WithProtocolVersion (MqttProtocolVersion.V311)
			 .WithClientId ($"{credentials.DeviceName}|securemode=2,signmethod=hmacsha1,timestamp={stamp}|")
			 .WithCredentials ($"{credentials.DeviceName}&{credentials.ProductKey}", password)
			 .WithCleanSession ()
			 .WithKeepAlivePeriod (TimeSpan.FromSeconds (30))
			 .WithTimeout (TimeSpan.FromSeconds (30))
			 .WithTlsOptions (tls => tls.UseTls ().WithTargetHost (host).WithSslProtocols (SslProtocols.Tls12)
				  .WithCertificateValidationHandler (args => ValidateCertificate (args.Certificate, args.Chain, args.SslPolicyErrors, root)))
			 .Build ();
		}

	/// <summary>
	/// Validates and extracts the permitted MQTT broker host from observer credentials.
	/// </summary>
	/// <param name="credentials">The session-bound observer credentials.</param>
	/// <returns>The validated broker hostname.</returns>
	/// <exception cref="RainPointException">The cloud returned an unsupported MQTT broker address.</exception>
	internal static string BrokerHost (ObserverCredentials credentials)
		{
		string value = credentials.HostUrl ?? credentials.ProductKey + ".iot-as-mqtt.us-west-1.aliyuncs.com";
		if (!value.Contains ("://"))
			value = "mqtt://" + value;
		if (!Uri.TryCreate (value, UriKind.Absolute, out Uri? uri)
			 || uri.Scheme is not ("mqtt" or "mqtts" or "tcp" or "ssl")
			 || !uri.Host.EndsWith (".aliyuncs.com", StringComparison.OrdinalIgnoreCase)
			 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/")
			throw new RainPointException ("The cloud returned an unsupported MQTT broker address.");
		return uri.DnsSafeHost;
		}

	/// <summary>
	/// Validates broker identity and trust against the embedded IoT root and supplied TLS errors.
	/// </summary>
	/// <param name="certificate">The broker certificate presented by the TLS connection, or null when absent.</param>
	/// <param name="supplied">The chain supplied by TLS validation, or null when absent.</param>
	/// <param name="errors">The platform TLS policy errors to evaluate alongside the trusted IoT root.</param>
	/// <param name="root">The trusted IoT root certificate used by observer TLS validation.</param>
	/// <returns>True only when the certificate passes the configured TLS policy.</returns>
	internal static bool ValidateCertificate (X509Certificate? certificate, X509Chain? supplied,
		 SslPolicyErrors errors, X509Certificate2 root)
		{
		if (certificate is null || (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0)
			return false;
		try
			{
			using X509Certificate2 leaf = new (certificate);
			using X509Chain chain = new ();
			chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
			chain.ChainPolicy.ApplicationPolicy.Add (new Oid ("1.3.6.1.5.5.7.3.1"));
			chain.ChainPolicy.ExtraStore.Add (root);
			if (supplied is not null)
				foreach (X509ChainElement item in supplied.ChainElements)
					chain.ChainPolicy.ExtraStore.Add (item.Certificate);
#if NET10_0_OR_GREATER
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(root);
#else
			chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
#endif
			bool built = chain.Build (leaf);
			return built && chain.ChainElements.Count > 0
				 && chain.ChainElements[chain.ChainElements.Count - 1].Certificate.RawData.SequenceEqual (root.RawData)
				 && chain.ChainElements.Cast<X509ChainElement> ().All (item => AllowsServerAuthentication (item.Certificate))
				 && chain.ChainStatus.All (status => status.Status is X509ChainStatusFlags.NoError or X509ChainStatusFlags.UntrustedRoot);
			}
		catch (CryptographicException) { return false; }
		}

	// Some older chain engines do not enforce ApplicationPolicy. Keep purpose validation explicit.
	private static bool AllowsServerAuthentication (X509Certificate2 certificate)
		{
		X509Extension? extension = certificate.Extensions["2.5.29.37"];
		if (extension is null)
			return true;
		X509EnhancedKeyUsageExtension usage = new (new AsnEncodedData (extension.Oid, extension.RawData), extension.Critical);
		return usage.EnhancedKeyUsages.Cast<Oid> ().Any (oid => oid.Value is "1.3.6.1.5.5.7.3.1" or "2.5.29.37.0");
		}

	/// <summary>
	/// Loads the embedded IoT trust anchor for observer TLS validation.
	/// </summary>
	/// <returns>The loaded root certificate; the caller owns its disposal.</returns>
	/// <exception cref="System.InvalidOperationException">The MQTT trust anchor is missing.</exception>
	internal static X509Certificate2 LoadRoot ()
		{
		using Stream stream = typeof (MqttObserverTransport).Assembly.GetManifestResourceStream ("RainPointClient.Protocol.AliyunIoTRoot.pem")
			 ?? throw new InvalidOperationException ("The MQTT trust anchor is missing.");
		using StreamReader reader = new (stream);
		byte[] der = Convert.FromBase64String (reader.ReadToEnd ().Replace ("-----BEGIN CERTIFICATE-----", "").Replace ("-----END CERTIFICATE-----", ""));
#if NET10_0_OR_GREATER
        return X509CertificateLoader.LoadCertificate(der);
#else
		return new X509Certificate2 (der);
#endif
		}
	}
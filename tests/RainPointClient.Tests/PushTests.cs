// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

using MQTTnet.Client;

using NUnit.Framework;

using RainPointClient.Protocol;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class PushTests
	{
	internal static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds (1800000000000);
	internal static RainPointHub Hub () => new ()
		{
		Id = 236547,
		HomeId = 42,
		Model = "HWG023WBRF-V2",
		DeviceName = "fixture-hub",
		ProductKey = "fixture-key",
		Devices = new[] { new RainPointDevice { Address = 1, Model = "HTV345FRF" } }
		};
	internal static byte[] Frame (string data, long stamp = 1799999990000, string mid = "236547", bool wrapped = true)
		{
		string frame = "#P260731181730000016822282" + mid + "|" + data + "|" + stamp + "|112882164350#";
		return Encoding.UTF8.GetBytes (wrapped ? JsonSerializer.Serialize (new
			{
			method = "thing.service.property.set",
			@params = new
				{
				param = frame
				}
			}) : frame);
		}
	internal static string TimerValue (long time = 1799999990000, string value = "11#19D801299F0E000000") =>
		 "{\"D01\":{\"value\":\"" + value + "\",\"time\":" + time + "}}";

	[Test]
	public void TypedPushDecodesTimerAndUsageWithoutExposingPayload ()
		{
		PushReading reading = PushDecoder.Decode (Frame (TimerValue ()), Hub (), Now)!;
		Assert.That (reading.Timers.Single ().Zones[0].IsOpen, Is.True);
		Assert.That (reading.Timers.Single ().Zones[0].LastWaterUsageLitres, Is.EqualTo (1.4m));
		Assert.That (reading.Timers.Single ().LastDataChange, Is.EqualTo (Now.AddSeconds (-10)));
		}

	[TestCase (true)]
	[TestCase (false)]
	public void BareAndWrappedConnectivityUseExactHubIdentity (bool wrapped)
		{
		PushReading reading = PushDecoder.Decode (Frame ("0", wrapped: wrapped), Hub (), Now)!;
		Assert.That (reading.Connected, Is.False);
		Assert.That (reading.ConnectionChanged, Is.EqualTo (Now.AddSeconds (-10)));
		Assert.That (PushDecoder.Decode (Frame ("1", mid: "136547", wrapped: wrapped), Hub (), Now), Is.Null);
		}

	[TestCase ("{}")]
	[TestCase ("{\"D02\":{\"value\":\"11#19D801\",\"time\":1799999990000}}")]
	[TestCase ("{\"D01\":{\"value\":\"11#19D8\",\"time\":1799999990000}}")]
	[TestCase ("{\"D01\":{\"value\":\"11#19D801\",\"time\":0}}")]
	[TestCase ("{\"D01\":{\"value\":\"11#19D801\",\"time\":999999999999999}}")]
	[TestCase ("{\"D01\":{\"value\":\"11#19D801\",\"time\":true}}")]
	[TestCase ("{\"D01\":null}")]
	[TestCase ("{\"D01\":{\"value\":\"11#19D801\",\"time\":1799999990000},\"D01\":{\"value\":\"11#19D800\",\"time\":1799999990000}}")]
	public void MissingUnknownMalformedAndAmbiguousTimerPushesAreDropped (string data) =>
		 Assert.That (PushDecoder.Decode (Frame (data), Hub (), Now), Is.Null);

	[Test]
	public void OversizeInvalidUtf8AndUnidentifiedFramesAreDropped ()
		{
		Assert.That (PushDecoder.Decode (new byte[8193], Hub (), Now), Is.Null);
		Assert.That (PushDecoder.Decode (new byte[] { 0xff, 0xfe }, Hub (), Now), Is.Null);
		Assert.That (PushDecoder.Decode (Encoding.UTF8.GetBytes ("{\"method\":\"wrong\",\"params\":{\"param\":\"x\"}}"), Hub (), Now), Is.Null);
		Assert.That (PushDecoder.Decode (Frame ("1", mid: "0236547"), Hub (), Now), Is.Null);
		Assert.That (PushDecoder.Decode (Frame ("1", mid: "２36547"), Hub (), Now), Is.Null);
		Assert.That (PushDecoder.Decode (Frame ("1", stamp: 0), Hub (), Now), Is.Null);
		}

	private static RainPointHubStatus Poll (long? stamp, bool? open, TimerReadingAvailability availability = TimerReadingAvailability.Decoded) =>
		 new (Hub ().Id, true, -38, stamp.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds (stamp.Value) : null,
			  new[] { new RainPointTimerStatus(1, availability, new[] { new RainPointZoneStatus(1, open, null, null) },
					 lastDataChange: stamp.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(stamp.Value) : null) });

	[TestCase (1799999980000L)]
	[TestCase (1799999990000L)]
	[TestCase (null)]
	public void OlderEqualAndUntimedPollCannotReplacePush (long? stamp)
		{
		StatusMerger merge = new (Hub ());
		_ = merge.ApplyPush (PushDecoder.Decode (Frame (TimerValue ()), Hub (), Now)!, Now);
		RainPointStatusUpdate update = merge.ApplyPoll (Poll (stamp, false), Now.AddSeconds (1));
		Assert.That (update.Status.Timers.Single ().Zones[0].IsOpen, Is.True);
		Assert.That (update.Timers.Single ().Source, Is.EqualTo (RainPointUpdateSource.Push));
		Assert.That (update.Timers.Single ().ReceivedAt, Is.EqualTo (Now));
		Assert.That (update.LastSuccessfulPollAt, Is.EqualTo (Now.AddSeconds (1)));
		}

	[Test]
	public void NewerPollWinsAndOlderPushDoesNotMoveRevision ()
		{
		StatusMerger merge = new (Hub ());
		_ = merge.ApplyPush (PushDecoder.Decode (Frame (TimerValue ()), Hub (), Now)!, Now);
		RainPointStatusUpdate update = merge.ApplyPoll (Poll (1800000000000, false), Now.AddSeconds (1));
		Assert.That (update.Status.Timers.Single ().Zones[0].IsOpen, Is.False);
		Assert.That (update.Timers.Single ().Source, Is.EqualTo (RainPointUpdateSource.Poll));
		Assert.That (merge.ApplyPush (PushDecoder.Decode (Frame (TimerValue ()), Hub (), Now)!, Now.AddSeconds (2)), Is.Null);
		}

	[Test]
	public void MissingMalformedAndFuturePollsCannotEraseKnownTimer ()
		{
		StatusMerger merge = new (Hub ());
		_ = merge.ApplyPoll (Poll (1799999990000, true), Now);
		_ = merge.ApplyPoll (Poll (1800000000000, null, TimerReadingAvailability.Malformed), Now);
		RainPointStatusUpdate result = merge.ApplyPoll (Poll (1800000600000, false), Now);
		Assert.That (result.Status.Timers.Single ().Zones[0].IsOpen, Is.True);
		Assert.That (result.Timers.Single ().ReceivedAt, Is.EqualTo (Now));
		}

	[Test]
	public void ConnectivityEdgeIsNotOverwrittenByOldPoll ()
		{
		StatusMerger merge = new (Hub ());
		_ = merge.ApplyPush (PushDecoder.Decode (Frame ("0"), Hub (), Now)!, Now);
		Assert.That (merge.ApplyPoll (Poll (1799999980000, false), Now).Status.IsConnected, Is.False);
		}

	[TestCase ("evil.invalid:1883")]
	[TestCase ("mqtt://user:password@fixture.aliyuncs.com")]
	[TestCase ("http://fixture.aliyuncs.com")]
	[TestCase ("mqtt://fixture.aliyuncs.com/path")]
	[TestCase ("mqtt://fixture.aliyuncs.com.evil.invalid")]
	public void ObserverRejectsUnexpectedBrokerAddresses (string host) =>
		 Assert.Throws<RainPointException> (() => MqttObserverTransport.BrokerHost (new ObserverCredentials { HostUrl = host }));

	[Test]
	public void ObserverAlwaysUsesTls8883AndExpectedAliyunIdentity ()
		{
		using X509Certificate2 root = MqttObserverTransport.LoadRoot ();
		MqttClientOptions options = MqttObserverTransport.BuildOptions (new ObserverCredentials
			{
			DeviceName = "device",
			ProductKey = "product",
			DeviceSecret = "secret",
			HostUrl = "fixture.aliyuncs.com:1883"
			}, Now, root);
		MqttClientTcpOptions tcp = (MqttClientTcpOptions)options.ChannelOptions;
		Assert.That (((DnsEndPoint)tcp.RemoteEndpoint).Port, Is.EqualTo (8883));
		Assert.That (tcp.TlsOptions.UseTls, Is.True);
		Assert.That (options.ClientId, Is.EqualTo ("device|securemode=2,signmethod=hmacsha1,timestamp=1800000000000|"));
		Assert.That (options.Credentials.GetUserName (options), Is.EqualTo ("device&product"));
		using HMACSHA1 independent = new (Encoding.UTF8.GetBytes ("secret"));
		string expected = BitConverter.ToString (independent.ComputeHash (Encoding.UTF8.GetBytes ("clientIddevicedeviceNamedeviceproductKeyproducttimestamp1800000000000"))).Replace ("-", "").ToLowerInvariant ();
		Assert.That (Encoding.UTF8.GetString (options.Credentials.GetPassword (options)), Is.EqualTo (expected));
		}

	[TestCase (0, 513)]
	[TestCase (120, 108)]
	[TestCase (3600, 513)]
	public void ObserverExpiryIsAbsoluteAndRenewalIsBounded (int seconds, int expected)
		{
		ObserverCredentials credentials = new ()
			{
			ExpiresAt = seconds == 0 ? null : Now.AddSeconds (seconds).ToUnixTimeMilliseconds ()
			};
		Assert.That (RainPointMonitor.RenewalDelay (credentials, Now), Is.EqualTo (TimeSpan.FromSeconds (expected)));
		}

	[Test]
	public void ExpiredObserverCredentialsAreNotUsed () => Assert.Throws<RainPointException> (() =>
		 RainPointMonitor.RenewalDelay (new ObserverCredentials { ExpiresAt = Now.AddSeconds (-1).ToUnixTimeMilliseconds () }, Now));

	[Test]
	public void TlsRejectsHostnameMismatchEvenWithPinnedRoot ()
		{
		using X509Certificate2 root = MqttObserverTransport.LoadRoot ();
		Assert.That (MqttObserverTransport.ValidateCertificate (root, null, SslPolicyErrors.RemoteCertificateNameMismatch, root), Is.False);
		Assert.That (MqttObserverTransport.ValidateCertificate (null, null, SslPolicyErrors.None, root), Is.False);
		using X509Certificate2 unrelated = TestCertificates.Root ();
		Assert.That (MqttObserverTransport.ValidateCertificate (unrelated, null, SslPolicyErrors.RemoteCertificateChainErrors, root), Is.False);
		}
	}
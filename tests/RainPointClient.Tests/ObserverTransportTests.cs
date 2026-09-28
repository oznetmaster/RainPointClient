// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Diagnostics;
using MQTTnet.Packets;

using NUnit.Framework;

using RainPointClient.Protocol;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class ObserverTransportTests
	{
	[Test]
	public async Task ObserverReceivesOnlyItsTopicAndCancelsWithoutPublishingOrSubscribing ()
		{
		FakeMqtt mqtt = new ();
		MqttObserverTransport transport = new (() => mqtt);
		using CancellationTokenSource cancellation = new ();
		byte[]? received = null;
		bool connected = false;
		Task running = transport.RunAsync (new ObserverCredentials { DeviceName = "observer", ProductKey = "product", DeviceSecret = "fixture" },
			 () => connected = true, bytes => received = bytes, cancellation.Token);
		Assert.That (connected, Is.True);
		await mqtt.Deliver ("/wrong/topic", new byte[] { 1 });
		Assert.That (received, Is.Null);
		const string topic = "/sys/product/observer/thing/service/property/set";
		await mqtt.Deliver (topic, new byte[8193]);
		Assert.That (received, Is.Null);
		byte[] source = [8, 1, 2, 9];
		await mqtt.Deliver (topic, source, 1, 2);
		source[1] = 99;
		Assert.That (received, Is.EqualTo (new byte[] { 1, 2 }), "The callback must own a copy of the payload segment.");
		cancellation.Cancel ();
		try
			{
			await running;
			}
		catch (OperationCanceledException) { }
		Assert.That (mqtt.Disconnected, Is.True);
		Assert.That (mqtt.Disposed, Is.True);
		Assert.That (mqtt.UnexpectedOperations, Is.Zero);
		}

	[TestCase (false)]
	[TestCase (true)]
	public void PrivateRootTrustStillRequiresValidServerCertificate (bool expired)
		{
		using X509Certificate2 root = TestCertificates.Root ();
		using X509Certificate2 leaf = TestCertificates.Leaf (expired);
		Assert.That (MqttObserverTransport.ValidateCertificate (leaf, null, SslPolicyErrors.RemoteCertificateChainErrors, root), Is.EqualTo (!expired), DescribeFixtureChain (leaf, root));
		using X509Certificate2 clientOnly = TestCertificates.ClientOnly ();
		Assert.That (MqttObserverTransport.ValidateCertificate (clientOnly, null, SslPolicyErrors.RemoteCertificateChainErrors, root), Is.False, "Client-authentication-only certificates cannot authenticate the broker.");
		}

	private static string DescribeFixtureChain (X509Certificate2 leaf, X509Certificate2 root)
		{
		using X509Chain chain = new ();
		chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
		chain.ChainPolicy.ApplicationPolicy.Add (new Oid ("1.3.6.1.5.5.7.3.1"));
		chain.ChainPolicy.ExtraStore.Add (root);
		chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
		try
			{
			bool built = chain.Build (leaf);
			return $"Fixture chain: built={built}; elements={chain.ChainElements.Count}; statuses={string.Join (",", chain.ChainStatus.Select (status => status.Status.ToString ()))}; now={DateTime.UtcNow:O}; valid={leaf.NotBefore:O}..{leaf.NotAfter:O}";
			}
		catch (Exception error) { return "Fixture chain exception: " + error.GetType ().Name + ": " + error.Message; }
		}

	private sealed class FakeMqtt : IMqttClient
		{
		public event Func<MqttApplicationMessageReceivedEventArgs, Task>? ApplicationMessageReceivedAsync;
		public event Func<MqttClientConnectedEventArgs, Task> ConnectedAsync { add { } remove { } }
		public event Func<MqttClientConnectingEventArgs, Task> ConnectingAsync { add { } remove { } }
		public event Func<MqttClientDisconnectedEventArgs, Task> DisconnectedAsync { add { } remove { } }
		public event Func<InspectMqttPacketEventArgs, Task> InspectPacketAsync { add { } remove { } }
		public bool IsConnected
			{
			get; private set;
			}
		public MqttClientOptions Options { get; private set; } = null!;
		internal bool Disconnected
			{
			get; private set;
			}
		internal bool Disposed
			{
			get; private set;
			}
		internal int UnexpectedOperations
			{
			get; private set;
			}
		public Task<MqttClientConnectResult> ConnectAsync (MqttClientOptions options, CancellationToken cancellationToken = default)
			{
			Options = options;
			IsConnected = true;
			return Task.FromResult (new MqttClientConnectResult ());
			}
		public Task DisconnectAsync (MqttClientDisconnectOptions options, CancellationToken cancellationToken = default)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			Disconnected = true;
			IsConnected = false;
			return Task.CompletedTask;
			}
		internal Task Deliver (string topic, byte[] payload, int offset = 0, int? count = null) => ApplicationMessageReceivedAsync! (
			 new MqttApplicationMessageReceivedEventArgs ("fixture", new MqttApplicationMessage { Topic = topic, PayloadSegment = new ArraySegment<byte> (payload, offset, count ?? payload.Length) },
				  new MqttPublishPacket (), (_, _) => Task.CompletedTask));
		public void Dispose () => Disposed = true;
		private Exception Unexpected ()
			{
			UnexpectedOperations++;
			return new InvalidOperationException ("Observers must not publish, subscribe or explicitly ping.");
			}
		public Task PingAsync (CancellationToken cancellationToken = default) => throw Unexpected ();
		public Task<MqttClientPublishResult> PublishAsync (MqttApplicationMessage message, CancellationToken cancellationToken = default) => throw Unexpected ();
		public Task SendExtendedAuthenticationExchangeDataAsync (MqttExtendedAuthenticationExchangeData data, CancellationToken cancellationToken = default) => throw Unexpected ();
		public Task<MqttClientSubscribeResult> SubscribeAsync (MqttClientSubscribeOptions options, CancellationToken cancellationToken = default) => throw Unexpected ();
		public Task<MqttClientUnsubscribeResult> UnsubscribeAsync (MqttClientUnsubscribeOptions options, CancellationToken cancellationToken = default) => throw Unexpected ();
		}
	}
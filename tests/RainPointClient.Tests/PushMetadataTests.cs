using System.Linq;

using NUnit.Framework;

using RainPointClient.Protocol;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class PushMetadataTests
	{
	[TestCase ("1")]
	[TestCase ("true")]
	[TestCase ("null")]
	[TestCase ("[1,2]")]
	public void NonTimerMetadataDoesNotDiscardValidTimerFeedback (string metadata)
		{
		string timer = PushTests.TimerValue ();
		string body = timer.Substring (0, timer.Length - 1) + ",\"update\":{\"value\":" + metadata
			 + ",\"time\":1799999990000},\"state\":{\"value\":\"0,-37\",\"time\":1799999990000}}";
		PushReading? reading = PushDecoder.Decode (PushTests.Frame (body), PushTests.Hub (), PushTests.Now);
		Assert.That (reading, Is.Not.Null);
		Assert.That (reading!.Timers.Single ().Zones[0].IsOpen, Is.True);
		}

	[TestCase ("1")]
	[TestCase ("true")]
	public void TimerValuesStillRequireStrings (string invalid) =>
		 Assert.That (PushDecoder.Decode (PushTests.Frame ("{\"D01\":{\"value\":" + invalid + ",\"time\":1799999990000}}"), PushTests.Hub (), PushTests.Now), Is.Null);

	[Test]
	public void TypedDatapointContractUsesDiscoveredAddressRatherThanOneFixedSlot ()
		{
		RainPointHub hub = PushTests.Hub ();
		hub.Devices = new[] { new RainPointDevice { Address = 17, Model = "HTV345FRF" } };
		string data = PushTests.TimerValue ().Replace ("D01", "D17");
		PushReading? reading = PushDecoder.Decode (PushTests.Frame (data), hub, PushTests.Now);
		Assert.That (reading!.Timers.Single ().Address, Is.EqualTo (17));
		}
	}
// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class CommandFeedbackTests
	{
	private ScriptedHandler _handler = null!;
	private HttpClient _http = null!;
	private RainPointCloudClient _client = null!;
	private static RainPointHub Hub () => new ()
		{
		Id = 101,
		DeviceName = "fixture",
		ProductKey = "fixture-key",
		Model = "HWG023WBRF",
		Devices = new[] { new RainPointDevice { Address = 2, Model = "HTV345FRF" } }
		};

	[SetUp]
	public async Task SetUp ()
		{
		_handler = new ScriptedHandler ();
		_http = new HttpClient (_handler, false);
		_client = new RainPointCloudClient (_http);
		_handler.Reply ("""{"code":0,"data":{"token":"fixture-session","tokenExpired":3600}}""");
		await _client.LoginAsync ("test@example.invalid", "password", "44");
		}

	[TearDown]
	public void TearDown ()
		{
		_client.Dispose ();
		_http.Dispose ();
		_handler.Dispose ();
		}

	[TestCase (1, 0, true)]
	[TestCase (2, 0, true)]
	[TestCase (3, 0, true)]
	[TestCase (1, 4, false)]
	[TestCase (2, 4, false)]
	[TestCase (3, 4, false)]
	public async Task ObjectResponseRetainsReportedZoneInsteadOfAssumingRequestedState (int zone, int code, bool start)
		{
		// Stop may return open and start may return closed. Records identify their own zones.
		string state = "11#" + (0x18 + zone).ToString ("X2") + "D8" + (start ? "00" : "01")
			 + (0x28 + zone).ToString ("X2") + "9F0E000000";
		_handler.Reply ("{\"code\":" + code + ",\"data\":{\"state\":\"" + state
			 + "\",\"timestamp\":\"1700000000000\",\"future\":{\"ignored\":true}}}");
		var result = start ? await _client.StartWateringAsync (Hub (), 2, zone, TimeSpan.FromMinutes (1))
			 : await _client.StopWateringAsync (Hub (), 2, zone);
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (result.Outcome, Is.EqualTo (code == 0 ? RainPointCommandOutcome.Accepted : RainPointCommandOutcome.AlreadyInRequestedStateOrTransitioning));
			Assert.That (result.RequestedZone, Is.EqualTo (zone));
			Assert.That (result.Status.Address, Is.EqualTo (2));
			Assert.That (result.Status.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
			Assert.That (result.Status.Zones.Single (item => item.Zone == zone).IsOpen, Is.EqualTo (!start));
			Assert.That (result.Status.Zones.Single (item => item.Zone == zone).LastWaterUsageLitres, Is.EqualTo (1.4m));
			Assert.That (result.Status.Zones.Where (item => item.Zone != zone).All (item => item.IsOpen is null && item.LastWaterUsageLitres is null), Is.True);
			Assert.That (result.ResponseTimestamp, Is.EqualTo (DateTimeOffset.FromUnixTimeMilliseconds (1700000000000)));
			Assert.That (result.Status.LastDataChange, Is.Null);
			Assert.That (_handler.Requests, Has.Count.EqualTo (2), "No replay or follow-up read.");
			}
		}

	[TestCase ("null", TimerReadingAvailability.NotReported)]
	[TestCase ("{}", TimerReadingAvailability.NotReported)]
	[TestCase ("{\"state\":null}", TimerReadingAvailability.NotReported)]
	[TestCase ("\"\"", TimerReadingAvailability.NotReported)]
	[TestCase ("\"01#19D800\"", TimerReadingAvailability.Decoded)]
	[TestCase ("\"11#1BD801\"", TimerReadingAvailability.Decoded)]
	[TestCase ("\"legacy-per-zone-state\"", TimerReadingAvailability.UnsupportedFormat)]
	[TestCase ("[]", TimerReadingAvailability.UnsupportedFormat)]
	[TestCase ("[{},[1]]", TimerReadingAvailability.UnsupportedFormat)]
	[TestCase ("true", TimerReadingAvailability.UnsupportedFormat)]
	[TestCase ("42", TimerReadingAvailability.UnsupportedFormat)]
	[TestCase ("{\"state\":{\"nested\":[1,2]}}", TimerReadingAvailability.Malformed)]
	[TestCase ("{\"state\":false}", TimerReadingAvailability.Malformed)]
	[TestCase ("\"11#19D8\"", TimerReadingAvailability.Malformed)]
	public async Task OptionalResponseShapeNeverFabricatesStateOrLosesAcknowledgement (string data, TimerReadingAvailability expected)
		{
		// Code follows data to exercise reader positioning after skipped optional values.
		_handler.Reply ("{\"data\":" + data + ",\"code\":0}");
		var result = await _client.StopWateringAsync (Hub (), 2, 2);
		Assert.That (result.Outcome, Is.EqualTo (RainPointCommandOutcome.Accepted));
		Assert.That (result.Status.Availability, Is.EqualTo (expected));
		Assert.That (result.ResponseTimestamp, Is.Null);
		if (expected != TimerReadingAvailability.Decoded)
			Assert.That (result.Status.Zones, Is.Empty);
		else
			Assert.That (result.Status.Zones.Single (item => item.Zone == 2).IsOpen, Is.Null);
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[TestCase ("null")]
	[TestCase ("0")]
	[TestCase ("-1")]
	[TestCase ("9223372036854775807")]
	[TestCase ("1.5")]
	[TestCase ("true")]
	[TestCase ("{}")]
	[TestCase ("[1]")]
	[TestCase ("\"invalid\"")]
	public async Task InvalidOptionalTimeDoesNotDiscardValidStatus (string timestamp)
		{
		_handler.Reply ("{\"code\":0,\"data\":{\"timestamp\":" + timestamp + ",\"state\":\"01#19D800\"}}");
		var result = await _client.StopWateringAsync (Hub (), 2, 1);
		Assert.That (result.ResponseTimestamp, Is.Null);
		Assert.That (result.Status.Zones[0].IsOpen, Is.False);
		}

	[Test]
	public async Task NumericTimestampIsAccepted ()
		{
		_handler.Reply ("""{"code":0,"data":{"state":"01#19D800","timestamp":1700000000000}}""");
		Assert.That ((await _client.StopWateringAsync (Hub (), 2, 1)).ResponseTimestamp,
			 Is.EqualTo (DateTimeOffset.FromUnixTimeMilliseconds (1700000000000)));
		}

	[TestCase (1001)]
	[TestCase (1004)]
	public async Task InvalidOptionalDataCannotHideSessionRejection (int code)
		{
		_handler.Reply ("{\"data\":{\"state\":[]},\"code\":" + code + "}");
		var error = await Assert.ThrowsAsync<RainPointException> (async () => await _client.StopWateringAsync (Hub (), 2, 1));
		Assert.That (error!.ApiCode, Is.EqualTo (code));
		Assert.That (_client.HasValidSession, Is.False);
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[TestCase ("{\"code\":0,\"data\":{\"state\":[}}")]
	[TestCase ("{\"data\":null}")]
	public async Task BrokenEnvelopeIsStillAnErrorAndNeverReplayed (string response)
		{
		_handler.Reply (response);
		await Assert.ThrowsAsync<RainPointException> (async () => await _client.StopWateringAsync (Hub (), 2, 1));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}
	}
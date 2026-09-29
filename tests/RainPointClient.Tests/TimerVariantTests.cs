// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE.

using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using RainPointClient.Protocol;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class TimerVariantTests
	{
	// Public upstream captures, used as protocol fixtures; see docs/TIMER-VARIANTS.md.
	private const string SingleOpen = "10#E1C200DC01D82120B7AE44E319ADB0049FA8020000FF0FAE3EE319";
	private const string SingleClosed = "10#E1BC00DC01D80020B700000000AD00009F95110000FF0F5D81D019";
	private const string DualClosed = "11#17E1AE0019D8001AD8001D201E2021B70000000022B70000000018DC0125AD000026AD0000299F000000002A9F00000000FEFF0FF5151519";

	[TestCase (SingleOpen, true, 1200, 68.0, -62)]
	[TestCase (SingleClosed, false, 0, 450.1, -68)]
	public void SingleZoneCapturedStatus (string payload, bool active, int seconds, double litres, int signal)
		{
		var status = TimerDecoder.Decode (2, 1, payload, null);
		Assert.That (status.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
		Assert.That (status.Zones, Has.Count.EqualTo (1));
		var zone = status.Zones.Single ();
		Assert.That (zone.IsOpen, Is.EqualTo (active));
		Assert.That (zone.ConfiguredRunDuration, Is.EqualTo (TimeSpan.FromSeconds (seconds)));
		Assert.That (zone.LastWaterUsageLitres, Is.EqualTo ((decimal)litres));
		Assert.That (status.SignalStrengthDbm, Is.EqualTo (signal));
		Assert.That (status.IsBatteryLow, Is.False);
		Assert.That (status.ReportedAtLocal, Is.Not.Null);
		}

	[Test]
	public void DualZoneCapturedStatusHasNoThirdZone ()
		{
		var status = TimerDecoder.Decode (2, 2, DualClosed, null);
		Assert.That (status.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
		Assert.That (status.Zones.Select (z => z.Zone), Is.EqualTo (new[] { 1, 2 }));
		Assert.That (status.Zones.All (z => z.IsOpen == false && z.LastWaterUsageLitres == 0), Is.True);
		}

	[TestCase ("10#D8", TimerReadingAvailability.Malformed)]
	[TestCase ("10#FF", TimerReadingAvailability.Malformed)]
	[TestCase ("10#D80120", TimerReadingAvailability.Decoded)]
	[TestCase ("10#D801GG", TimerReadingAvailability.Malformed)]
	[TestCase ("10#E1BC", TimerReadingAvailability.Malformed)]
	public void CompactRecordBoundariesAreValidated (string payload, TimerReadingAvailability expected) =>
		Assert.That (TimerDecoder.Decode (2, 1, payload, null).Availability, Is.EqualTo (expected));

	[TestCase (2), TestCase (3)]
	public void CompactStatusCannotBeMisreadAsMultipleZones (int count) =>
		Assert.That (TimerDecoder.Decode (2, count, SingleOpen, null).Availability, Is.EqualTo (TimerReadingAvailability.UnsupportedFormat));

	[TestCase ("HTV145FRF", 1, SingleOpen)]
	[TestCase ("HTV245FRF", 2, DualClosed)]
	[TestCase ("HTV345FRF", 3, "11#19D8001AD8001BD800")]
	public void DiscoveryAndMqttRespectModelZoneCount (string model, int count, string payload)
		{
		var hub = PushTests.Hub ();
		hub.Devices = new[] { new RainPointDevice { Address = 1, Model = model, FirmwareVersion = "130", PortDescriptions = "Lawn|Beds|Tap" } };
		Assert.That (hub.Devices[0].SupportedZoneCount, Is.EqualTo (count));
		Assert.That (hub.Devices[0].ZoneNames, Has.Count.EqualTo (count));
		Assert.That (hub.Devices[0].SupportsManualCycles, Is.EqualTo (count == 3));
		var push = PushDecoder.Decode (PushTests.Frame (PushTests.TimerValue (value: payload)), hub, PushTests.Now);
		Assert.That (push!.Timers.Single ().Zones, Has.Count.EqualTo (count));
		}

	[TestCase ("HTV145FRF", 1), TestCase ("HTV245FRF", 2), TestCase ("HTV345FRF", 3)]
	public async Task ManualCommandsUseOnlyExistingPorts (string model, int count)
		{
		using var handler = new ScriptedHandler ();
		using var http = new HttpClient (handler);
		using var client = new RainPointCloudClient (http);
		handler.Reply ("{\"code\":0,\"data\":{\"token\":\"fixture\",\"tokenExpired\":3600}}");
		await client.LoginAsync ("fixture@example.invalid", "fixture", "44");
		var hub = PushTests.Hub ();
		hub.Devices = new[] { new RainPointDevice { Address = 1, Model = model } };
		for (int zone = 1; zone <= count; zone++)
			{
			handler.Reply ("{\"code\":0}");
			await client.StartWateringAsync (hub, 1, zone, TimeSpan.FromMinutes (1));
			using var body = JsonDocument.Parse (handler.Requests.Last ().Body!);
			Assert.That (body.RootElement.GetProperty ("port").GetInt32 (), Is.EqualTo (zone));
			handler.Reply ("{\"code\":0}");
			await client.StopWateringAsync (hub, 1, zone);
			}
		int sent = handler.Requests.Count;
		await Assert.ThrowsAsync<ArgumentOutOfRangeException> (() => client.StopWateringAsync (hub, 1, count + 1));
		await Assert.ThrowsAsync<ArgumentOutOfRangeException> (() => client.StartWateringAsync (hub, 1, count + 1, TimeSpan.FromMinutes (1)));
		Assert.That (handler.Requests, Has.Count.EqualTo (sent));
		}

	[TestCase (1), TestCase (2), TestCase (3)]
	public void SharedPlanContainerIsReadPerActualZone (int count)
		{
		var device = new RainPointDevice { Address = 1, PortNumber = count,
			Parameter = string.Join ("|", Enumerable.Repeat ("settings,801e4a58020e00380d/,,", count)) };
		for (int zone = 1; zone <= count; zone++)
			{
			var snapshot = ScheduleDecoder.Decode (device, zone);
			Assert.That (snapshot.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
			Assert.That (snapshot.Schedules.Single ().StartTime, Is.EqualTo (new TimeSpan (8, 30, 0)));
			Assert.That (snapshot.Schedules.Single ().Duration, Is.EqualTo (TimeSpan.FromMinutes (10)));
			}
		Assert.That (ScheduleDecoder.Decode (device, count + 1).Availability, Is.EqualTo (TimerReadingAvailability.Malformed));
		device.Parameter += "|settings,/,,";
		Assert.That (ScheduleDecoder.Decode (device, 1).Availability, Is.EqualTo (TimerReadingAvailability.Malformed));
		}
	}
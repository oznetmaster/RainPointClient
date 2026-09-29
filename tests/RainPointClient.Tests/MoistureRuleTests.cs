// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class MoistureRuleTests
	{
	private const string Port = "58020a001e00038000004200fed7aabb,/,1e00000058020000aabb,646464646464646464646464,tail";
	private static readonly string EMPTY = string.Join ("|", Enumerable.Repeat (Port, 3));
	private ScriptedHandler _handler = null!;
	private HttpClient _http = null!;
	private RainPointCloudClient _client = null!;
	private RainPointHub _hub = null!;

	[SetUp]
	public async Task SetUp ()
		{
		_handler = new ScriptedHandler ();
		_http = new HttpClient (_handler, disposeHandler: false);
		_client = new RainPointCloudClient (_http);
		_hub = new RainPointHub
			{
			Id = 101,
			HomeId = 5,
			Model = "HWG023WBRF",
			DeviceName = "hub",
			ProductKey = "product",
			Devices = new[] { new RainPointDevice { Id = 42, Address = 2, Model = "HTV345FRF" } }
			};
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

	private void Discovery (string parameter, string firmware = "130", int id = 42, string sensor = "HCS021FRF")
		{
		_handler.Reply (JsonSerializer.Serialize (new
			{
			code = 0,
			data = new[] { new { mid = 101, model = "HWG023WBRF", deviceName = "hub", productKey = "product",
				subDevices = new object[] { new { sid = id, addr = 2, model = "HTV345FRF", portNumber = 3, softVer = firmware, param = parameter }, new { sid = 43, addr = 3, model = sensor } } } }
			}));
		}

	private async Task<RainPointScheduleSnapshot> Read (string? parameter = null, string firmware = "130", int zone = 1)
		{
		Discovery (parameter ?? EMPTY, firmware);
		return await _client.GetTimerSchedulesAsync (_hub, 2, zone);
		}

	[TestCase (1, false, false)]
	[TestCase (2, false, false)]
	[TestCase (3, false, false)]
	[TestCase (1, true, false)]
	[TestCase (2, true, false)]
	[TestCase (3, true, false)]
	[TestCase (1, false, true)]
	[TestCase (2, false, true)]
	[TestCase (3, false, true)]
	[TestCase (1, true, true)]
	[TestCase (2, true, true)]
	[TestCase (3, true, true)]
	public async Task RuleRoundTripPreservesOtherZonesAndSuffix (int zone, bool misting, bool volumeOnly)
		{
		var before = await Read (zone: zone);
		var rule = new RainPointMoistureWateringRule { Enabled = true, StartBelowMoisturePercent = 40, Mode = misting ? RainPointScheduleMode.Misting : RainPointScheduleMode.Irrigation, Duration = volumeOnly ? null : TimeSpan.FromMinutes (5), WaterLimitLitres = 1.4m, ExcludedFrom = new TimeSpan (22, 0, 0), ExcludedUntil = new TimeSpan (6, 0, 0) };
		Discovery (EMPTY);
		_handler.Reply ("{\"code\":0}");
		await _client.SetTimerMoistureWateringRuleAsync (_hub, before, rule);
		string encoded = JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (_handler.Requests.Last ().Body!)!.Parameter;
		var parts = encoded.Split ('|');
		var original = EMPTY.Split ('|');
		for (int i = 0; i < 3; i++)
			if (i != zone - 1)
				Assert.That (parts[i], Is.EqualTo (original[i]));
		// 40|128; exclusion 22:00..06:00 packs to 0x180b01, plus misting bit23.
		string expected = "a8010b" + (misting ? "98" : "18") + (volumeOnly ? "fca8" : "2c01") + "0e00aabb";
		Assert.That (parts[zone - 1].Split (',')[2], Is.EqualTo (expected));
		Assert.That (parts[zone - 1].Replace (expected, "1e00000058020000aabb"), Is.EqualTo (original[zone - 1]));
		var after = await Read (encoded, zone: zone);
		var saved = after.MoistureWateringRule!;
		Assert.Multiple (() =>
			{
				Assert.That (after.MoistureRuleAvailability, Is.EqualTo (TimerReadingAvailability.Decoded));
				Assert.That (saved.Enabled, Is.True);
				Assert.That (saved.Mode, Is.EqualTo (rule.Mode));
				Assert.That (saved.StartBelowMoisturePercent, Is.EqualTo (40));
				Assert.That (saved.Duration, Is.EqualTo (rule.Duration));
				Assert.That (saved.WaterLimitLitres, Is.EqualTo (1.4m));
				Assert.That (saved.ExcludedFrom, Is.EqualTo (rule.ExcludedFrom));
				Assert.That (saved.ExcludedUntil, Is.EqualTo (rule.ExcludedUntil));
			});
		}
	[TestCase (0)]
	[TestCase (100)]
	[TestCase (-1)]
	public async Task RejectsBadMoisture (int value)
		{
		var before = await Read ();
		var rule = new RainPointMoistureWateringRule { StartBelowMoisturePercent = value };
		await Assert.ThrowsAsync<ArgumentException> (() => _client.SetTimerMoistureWateringRuleAsync (_hub, before, rule));
		}
	[TestCase (0)]
	[TestCase (31)]
	[TestCase (1.5)]
	public async Task RejectsBadDuration (double minutes)
		{
		var before = await Read ();
		var rule = new RainPointMoistureWateringRule { Duration = TimeSpan.FromMinutes (minutes) };
		await Assert.ThrowsAsync<ArgumentException> (() => _client.SetTimerMoistureWateringRuleAsync (_hub, before, rule));
		}
	[TestCase ("0.2")]
	[TestCase ("6000.1")]
	[TestCase ("1.45")]
	public async Task RejectsBadVolume (string value)
		{
		var before = await Read ();
		var rule = new RainPointMoistureWateringRule { WaterLimitLitres = decimal.Parse (value, System.Globalization.CultureInfo.InvariantCulture) };
		await Assert.ThrowsAsync<ArgumentException> (() => _client.SetTimerMoistureWateringRuleAsync (_hub, before, rule));
		}
	[Test]
	public async Task RequiresALimitAndAnAssociatedSensorToEnable ()
		{
		var before = await Read (EMPTY.Replace ("1e000380", "1e000080"));
		await Assert.ThrowsAsync<ArgumentException> (() => _client.SetTimerMoistureWateringRuleAsync (_hub, before, new () { Duration = null }));
		await Assert.ThrowsAsync<InvalidOperationException> (() => _client.SetTimerMoistureWateringRuleAsync (_hub, before, new () { Enabled = true }));
		}
	[TestCase ("1e00")]
	[TestCase ("1e0000005802000z")]
	[TestCase ("ff00000058020000")]
	[TestCase ("1e800f0058020000")]
	public async Task MalformedRulesCannotBeOverwritten (string encoded)
		{
		var before = await Read (EMPTY.Replace ("1e00000058020000aabb", encoded));
		Assert.That (before.MoistureRuleAvailability, Is.EqualTo (TimerReadingAvailability.Malformed));
		await Assert.ThrowsAsync<NotSupportedException> (() => _client.SetTimerMoistureWateringRuleAsync (_hub, before, new ()));
		}
	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public async Task AbsentRuleCanBeCreatedDisabledWithoutSensor (int zone)
		{
		string original = EMPTY.Replace ("1e00000058020000aabb", "").Replace ("1e000380", "1e000080");
		var before = await Read (original, zone: zone);
		Assert.That (before.MoistureRuleAvailability, Is.EqualTo (TimerReadingAvailability.NotReported));
		Discovery (original);
		_handler.Reply ("{\"code\":0}");
		await _client.SetTimerMoistureWateringRuleAsync (_hub, before, new ());
		Assert.That (JsonSerializer.Deserialize<Protocol.TimerParameterRequest> (_handler.Requests.Last ().Body!)!.Parameter.Split ('|')[zone - 1].Split (',')[2], Is.EqualTo ("1e00000058020000"));
		}
	[TestCase (0, 0)]
	[TestCase (0, 1440)]
	[TestCase (0, 1439)]
	public async Task ExclusionMustLeaveEnoughTime (int start, int end)
		{
		var before = await Read ();
		await Assert.ThrowsAsync<ArgumentException> (() => _client.SetTimerMoistureWateringRuleAsync (_hub, before, new () { ExcludedFrom = TimeSpan.FromMinutes (start), ExcludedUntil = TimeSpan.FromMinutes (end) }));
		}
	[Test]
	public async Task StaleRuleDoesNotWrite ()
		{
		var before = await Read ();
		Discovery (EMPTY.Replace ("tail", "external"));
		await Assert.ThrowsAsync<RainPointException> (() => _client.SetTimerMoistureWateringRuleAsync (_hub, before, new ()));
		Assert.That (_handler.Requests.Count (r => r.Path == "/app/device/sub/update"), Is.Zero);
		}
	}
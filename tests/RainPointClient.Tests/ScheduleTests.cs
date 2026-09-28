using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using NUnit.Framework;

using RainPointClient.Protocol;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class ScheduleTests
	{
	private static RainPointScheduleSnapshot Decode (string? parameter, int zone = 1, int? ports = 3)
		 => ScheduleDecoder.Decode (new RainPointDevice { Address = 2, PortNumber = ports, Parameter = parameter }, zone);

	[Test]
	public void NormalPlanDecodesLocalTimeSecondsAndTenthsOfLitres ()
		{
		// Enabled; daily normal watering at 08:30; 600 seconds; 1.4 L; 24 September 2026.
		RainPointScheduleSnapshot result = Decode ("settings,801e4a58020e00380d/,,|settings,/,|settings,/,");
		RainPointSchedule plan = result.Schedules.Single ();
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (result.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
			Assert.That (result.Zone, Is.EqualTo (1));
			Assert.That (result.Address, Is.EqualTo (2));
			Assert.That (plan.Enabled, Is.True);
			Assert.That (plan.Mode, Is.EqualTo (RainPointScheduleMode.Irrigation));
			Assert.That (plan.StartTime, Is.EqualTo (new TimeSpan (8, 30, 0)));
			Assert.That (plan.Duration, Is.EqualTo (TimeSpan.FromMinutes (10)));
			Assert.That (plan.WaterLimitLitres, Is.EqualTo (1.4m));
			Assert.That (plan.EffectiveDate, Is.EqualTo (new DateTime (2026, 9, 24)));
			Assert.That (plan.EffectiveDate!.Value.Kind, Is.EqualTo (DateTimeKind.Unspecified));
			Assert.That (plan.Repeat, Is.EqualTo (RainPointScheduleRepeat.EveryDay));
			Assert.That (plan.Interval, Is.Null);
			Assert.That (plan.CyclePauseTime, Is.Null);
			}
		}

	[TestCase ("80c0c914000000380d05001e00", RainPointScheduleMode.CycleAndSoak, 1200, 300, 1800)]
	[TestCase ("80c08914000000380d05001e00", RainPointScheduleMode.Misting, 20, 5, 30)]
	public void CycleUnitsDependOnMode (string record, RainPointScheduleMode mode, int duration, int water, int pause)
		{
		RainPointSchedule plan = Decode ("settings," + record + "/,,|settings,/,|settings,/,").Schedules.Single ();
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (plan.Mode, Is.EqualTo (mode));
			Assert.That (plan.Duration, Is.EqualTo (TimeSpan.FromSeconds (duration)));
			Assert.That (plan.CycleWateringTime, Is.EqualTo (TimeSpan.FromSeconds (water)));
			Assert.That (plan.CyclePauseTime, Is.EqualTo (TimeSpan.FromSeconds (pause)));
			Assert.That (plan.WaterLimitLitres, Is.Null);
			}
		}

	[Test]
	public void LegacyListsKeepDisabledPlansAndUseSundayAsBitZero ()
		{
		RainPointScheduleSnapshot result = Decode ("settings,|settings,4100603c00,8000487800|settings,", 2);
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (result.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
			Assert.That (result.Schedules, Has.Count.EqualTo (2));
			Assert.That (result.Schedules[0].Enabled, Is.False);
			Assert.That (result.Schedules[0].Repeat, Is.EqualTo (RainPointScheduleRepeat.Weekdays));
			Assert.That (result.Schedules[0].Weekdays, Is.EqualTo (new[] { DayOfWeek.Sunday, DayOfWeek.Saturday }));
			Assert.That (result.Schedules[0].EffectiveDate, Is.Null);
			Assert.That (result.Schedules[1].Index, Is.EqualTo (1));
			}
		}

	[TestCase ("8000403c00", RainPointScheduleRepeat.Once, null)]
	[TestCase ("8000483c00", RainPointScheduleRepeat.EveryDay, null)]
	[TestCase ("8000503c00", RainPointScheduleRepeat.OddDays, null)]
	[TestCase ("8000583c00", RainPointScheduleRepeat.EvenDays, null)]
	[TestCase ("8300683c00", RainPointScheduleRepeat.IntervalDays, 3)]
	[TestCase ("8200703c00", RainPointScheduleRepeat.IntervalHours, 2)]
	public void RepeatRulesAreTyped (string record, RainPointScheduleRepeat repeat, int? interval)
		{
		RainPointSchedule plan = Decode ("settings," + record + "|settings,|settings,").Schedules.Single ();
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (plan.Repeat, Is.EqualTo (repeat));
			Assert.That (plan.Interval, Is.EqualTo (interval));
			}
		}

	[TestCase (null, TimerReadingAvailability.NotReported)]
	[TestCase ("", TimerReadingAvailability.NotReported)]
	[TestCase ("settings|settings|settings", TimerReadingAvailability.NotReported)]
	[TestCase ("settings,|settings,|settings,", TimerReadingAvailability.Decoded)]
	[TestCase ("settings,/,|settings,/,|settings,/,", TimerReadingAvailability.Decoded)]
	[TestCase ("12=8000483c00", TimerReadingAvailability.UnsupportedFormat)]
	[TestCase ("settings,|settings,", TimerReadingAvailability.Malformed)]
	[TestCase ("settings,garbage|settings,|settings,", TimerReadingAvailability.Malformed)]
	[TestCase ("settings,8000483c0000|settings,|settings,", TimerReadingAvailability.UnsupportedFormat)]
	[TestCase ("settings,80zz483c00|settings,|settings,", TimerReadingAvailability.Malformed)]
	[TestCase ("settings,80ff4f3c00|settings,|settings,", TimerReadingAvailability.Malformed)]
	[TestCase ("settings,8000003c00|settings,|settings,", TimerReadingAvailability.UnsupportedFormat)]
	[TestCase ("settings,8000783c00|settings,|settings,", TimerReadingAvailability.UnsupportedFormat)]
	[TestCase ("settings,8000683c00|settings,|settings,", TimerReadingAvailability.Malformed)]
	[TestCase ("settings,8000483c0000005f0c|settings,|settings,", TimerReadingAvailability.Malformed)]
	[TestCase ("settings,8000483c00//8000483c00,,|settings,|settings,", TimerReadingAvailability.Malformed)]
	[TestCase ("settings,8000483c00,bad|settings,|settings,", TimerReadingAvailability.Malformed)]
	public void UnavailableOrUnreadableNeverLooksLikeAnEmptyDecodedList (string? parameter, TimerReadingAvailability expected)
		{
		RainPointScheduleSnapshot result = Decode (parameter);
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (result.Availability, Is.EqualTo (expected));
			Assert.That (result.Schedules, Is.Empty);
			}
		}

	[TestCase (null)]
	[TestCase (1)]
	public void UnknownPortLayoutIsNotGuessed (int? ports)
		{
		Assert.That (Decode ("settings,|settings,|settings,", ports: ports).Availability,
			 Is.EqualTo (TimerReadingAvailability.UnsupportedFormat));
		}

	[Test]
	public void EmptyZoneDoesNotHideAnotherZonesPlans ()
		{
		const string PARAMETER = "settings,/,|settings,8000483c00/,,|settings,/,";
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (Decode (PARAMETER, 1).Schedules, Is.Empty);
			Assert.That (Decode (PARAMETER, 2).Schedules, Has.Count.EqualTo (1));
			Assert.That (Decode (PARAMETER, 3).Schedules, Is.Empty);
			}
		}

	[TestCase (42, true)]
	[TestCase (43, false)]
	public async Task ApiReadsFreshConfigurationAndRejectsRepairedTimer (int freshId, bool success)
		{
		using ScriptedHandler handler = new ();
		using HttpClient http = new (handler, disposeHandler: false);
		using RainPointCloudClient client = new (http);
		handler.Reply ("""{"code":0,"data":{"token":"fixture-session","tokenExpired":3600}}""");
		await client.LoginAsync ("test@example.invalid", "password", "44");
		handler.Reply ($$"""{"code":0,"data":[{"mid":101,"model":"HWG023WBRF","deviceName":"hub","productKey":"product","subDevices":[{"sid":{{freshId}},"addr":2,"model":"HTV345FRF","portNumber":"3","param":"settings,8000483c00/,,|settings,/,|settings,/,"}]}]}""");
		RainPointHub hub = Hub ();
		if (success)
			{
			RainPointScheduleSnapshot result = await client.GetTimerSchedulesAsync (hub, 2, 1);
			Assert.That (result.Schedules, Has.Count.EqualTo (1));
			}
		else
			{
			Assert.ThrowsAsync<RainPointException> (async () => await client.GetTimerSchedulesAsync (hub, 2, 1));
			}
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (handler.Requests.Last ().Method, Is.EqualTo (HttpMethod.Get));
			Assert.That (handler.Requests.Last ().Path, Is.EqualTo ("/app/device/getDeviceByHid?hid=5"));
			Assert.That (handler.Requests.Last ().Body, Is.Null);
			}
		}

	[TestCase (0)]
	[TestCase (4)]
	public void InvalidZoneFailsBeforeAnyNetworkRequest (int zone)
		{
		using ScriptedHandler handler = new ();
		using HttpClient http = new (handler, disposeHandler: false);
		using RainPointCloudClient client = new (http);
		Assert.ThrowsAsync<ArgumentOutOfRangeException> (async () => await client.GetTimerSchedulesAsync (Hub (), 2, zone));
		Assert.That (handler.Requests, Is.Empty);
		}

	private static RainPointHub Hub () => new ()
		{
		Id = 101,
		HomeId = 5,
		Model = "HWG023WBRF",
		DeviceName = "hub",
		ProductKey = "product",
		Devices = new[] { new RainPointDevice { Id = 42, Address = 2, Model = "HTV345FRF", Parameter = "stale", PortNumber = 3 } }
		};
	}
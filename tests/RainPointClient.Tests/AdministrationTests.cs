// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
namespace RainPointClient.Tests;

[TestFixture]
public sealed class AdministrationTests
	{
	private ScriptedHandler _handler = null!;
	private HttpClient _http = null!;
	private RainPointCloudClient _client = null!;
	internal const string HOME = """{"code":0,"data":{"hid":"5","homeName":"Garden","owner":1,"rightCode":1,"unit":"8801tail","currency":7,"lat":55123456,"lon":-4123456,"rooms":[{"rid":"9","hid":"5","roomName":"Bed","devices":"101#201#3"}]}}""";
	[SetUp]
	public async Task Setup ()
		{
		_handler = new ();
		_http = new (_handler, false);
		_client = new (_http);
		_handler.Reply ("""{"code":0,"data":{"token":"fixture","tokenExpired":3600,"user":{"notice":19}}}""");
		await _client.LoginAsync ("fixture@example.invalid", "fixture", "44");
		}
	[TearDown]
	public void Cleanup ()
		{
		_client.Dispose ();
		_http.Dispose ();
		_handler.Dispose ();
		}
	[TestCase (false, false, 16)]
	[TestCase (true, false, 17)]
	[TestCase (false, true, 18)]
	[TestCase (true, true, 19)]
	public async Task NotificationWritePreservesUnknownBitsAndConsumesObservation (bool mobile, bool email, int flags)
		{
		var expected = _client.NotificationPreferences!;
		Assert.That (expected.UnknownFlags, Is.EqualTo (16));
		_handler.Reply ("{\"code\":0}");
		await _client.SetNotificationPreferencesAsync (expected, mobile, email);
		Assert.That (_handler.Requests.Last ().Path, Is.EqualTo ("/app/member/user/info/set"));
		Assert.That (_handler.Requests.Last ().Body, Is.EqualTo ("{\"notice\":" + flags + "}"));
		Assert.That (_client.NotificationPreferences, Is.Null);
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.SetNotificationPreferencesAsync (expected, mobile, email));
		Assert.That (_handler.Requests.Count, Is.EqualTo (2));
		}
	[Test]
	public async Task UnknownNotificationOutcomeCannotReplay ()
		{
		var expected = _client.NotificationPreferences!;
		_handler.Reply ("{\"code\":42}");
		Assert.ThrowsAsync<RainPointException> (async () => await _client.SetNotificationPreferencesAsync (expected, false, false));
		Assert.That (_client.NotificationPreferences, Is.Null);
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.SetNotificationPreferencesAsync (expected, false, false));
		await Task.CompletedTask;
		}
	[TestCase ("")]
	[TestCase (",\"user\":{}")]
	[TestCase (",\"user\":{\"notice\":-1}")]
	public async Task MissingNotificationStateDoesNotInventDefaults (string fields)
		{
		_handler.Reply ("{\"code\":0,\"data\":{\"token\":\"new\",\"tokenExpired\":3600" + fields + "}}");
		await _client.LoginAsync ("other@example.invalid", "fixture", "44");
		Assert.That (_client.NotificationPreferences, Is.Null);
		}
	[Test]
	public async Task OldNotificationObservationCannotCrossAccounts ()
		{
		var expected = _client.NotificationPreferences!;
		_handler.Reply ("""{"code":0,"data":{"token":"other","tokenExpired":3600}}""");
		await _client.LoginAsync ("other@example.invalid", "fixture", "44");
		Assert.That (_client.NotificationPreferences, Is.Null);
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.SetNotificationPreferencesAsync (expected, true, true));
		}
	[Test]
	public async Task HomeReadDecodesCanonicalCoordinatesWithoutExposingPayloads ()
		{
		_handler.Reply (HOME);
		var home = await _client.GetHomeAsync (5);
		Assert.That (home.Latitude, Is.EqualTo (55.123456m));
		Assert.That (home.Longitude, Is.EqualTo (-4.123456m));
		Assert.That (home.Rooms.Single ().Name, Is.EqualTo ("Bed"));
		Assert.That (home.IsOwner, Is.True);
		Assert.That (home.DisplayUnits!.Pressure, Is.EqualTo (RainPointPressureUnit.Pascal));
		}
	[TestCase ("{\"hid\":6}")]
	[TestCase ("{\"hid\":5,\"rooms\":[null]}")]
	[TestCase ("{\"hid\":5,\"rooms\":[{\"rid\":9},{\"rid\":9}]}")]
	[TestCase ("{\"hid\":5,\"rooms\":[{\"rid\":9,\"hid\":6}]}")]
	public void InvalidHomeResponseRejected (string data)
		{
		_handler.Reply ("{\"code\":0,\"data\":" + data + "}");
		Assert.ThrowsAsync<RainPointException> (async () => await _client.GetHomeAsync (5));
		}
	[TestCase (0)]
	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	[TestCase (4)]
	[TestCase (5)]
	public async Task WritesOnlyRequestedFieldsAndConsumesSnapshot (int operation)
		{
		_handler.Reply (HOME);
		var home = await _client.GetHomeAsync (5);
		_handler.Reply (HOME);
		_handler.Reply ("{\"code\":0}");
		string body;
		switch (operation)
			{
			case 0:
				await _client.RenameHomeAsync (home, "New");
				body = "{\"hid\":5,\"homeName\":\"New\"}";
				break;
			case 1:
				await _client.CreateRoomAsync (home, "New");
				body = "{\"hid\":5,\"roomName\":\"New\"}";
				break;
			case 2:
				await _client.RenameRoomAsync (home, 9, "New");
				body = "{\"hid\":5,\"rid\":9,\"roomName\":\"New\"}";
				break;
			case 3:
				await _client.DeleteRoomAsync (home, 9);
				body = "{\"hid\":5,\"rid\":9}";
				break;
			case 4:
				await _client.SetHomeLocationAsync (home, -12.3456789m, 45.6789019m);
				body = "{\"hid\":5,\"lat\":-12345678,\"lon\":45678901,\"updateTempPosition\":false}";
				break;
			default:
				await _client.SetHomeDisplayUnitsAsync (home, new ()
					{
					TwelveHourClock = true,
					Fahrenheit = true,
					ImperialLength = true,
					ImperialVolume = true,
					Pressure = RainPointPressureUnit.MillimetresOfMercury
					});
				body = "{\"hid\":5,\"unit\":\"DF01tail\"}";
				break;
			}
		Assert.That (_handler.Requests.Last ().Body, Is.EqualTo (body));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.RenameHomeAsync (home, "again"));
		Assert.That (_handler.Requests.Count, Is.EqualTo (4));
		}
	[TestCase ("Garden", "Changed")]
	[TestCase ("8801tail", "0001tail")]
	[TestCase ("Bed", "Other")]
	public async Task ConcurrentHomeChangePreventsWrite (string before, string after)
		{
		_handler.Reply (HOME);
		var home = await _client.GetHomeAsync (5);
		_handler.Reply (HOME.Replace (before, after));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.RenameHomeAsync (home, "New"));
		Assert.That (_handler.Requests.Count, Is.EqualTo (3));
		}
	[Test]
	public async Task FailedHomeWriteCannotReplay ()
		{
		_handler.Reply (HOME);
		var home = await _client.GetHomeAsync (5);
		_handler.Reply (HOME);
		_handler.Reply ("{\"code\":42}");
		Assert.ThrowsAsync<RainPointException> (async () => await _client.RenameHomeAsync (home, "New"));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.RenameHomeAsync (home, "New"));
		Assert.That (_handler.Requests.Count, Is.EqualTo (4));
		}
	[Test]
	public async Task WrongRoomAndInvalidLocationNeverWrite ()
		{
		_handler.Reply (HOME);
		var home = await _client.GetHomeAsync (5);
		Assert.Throws<ArgumentException> (() => _client.DeleteRoomAsync (home, 123));
		Assert.Throws<ArgumentOutOfRangeException> (() => _client.SetHomeLocationAsync (home, 91, 0));
		Assert.Throws<ArgumentOutOfRangeException> (() => _client.SetHomeLocationAsync (home, 0, 181));
		Assert.That (_handler.Requests.Count, Is.EqualTo (2));
		}
	[Test]
	public async Task CancelledWriteDoesNotPost ()
		{
		_handler.Reply (HOME);
		var home = await _client.GetHomeAsync (5);
		using var cancelled = new CancellationTokenSource ();
		cancelled.Cancel ();
		Assert.CatchAsync<OperationCanceledException> (async () => await _client.RenameHomeAsync (home, "New", cancelled.Token));
		Assert.That (_handler.Requests.All (r => r.Path != "/app/member/appHome/update"), Is.True);
		}
	[Test]
	public async Task MemberAndInvitationReadsAreTyped ()
		{
		_handler.Reply ("""{"code":0,"data":[{"uid":"10","hid":"5","nickname":"Member","owner":0,"rightCode":9}]}""");
		var member = (await _client.GetMembersAsync (5)).Single ();
		Assert.That (member.Role, Is.Null);
		Assert.That (member.IsOwner, Is.False);
		_handler.Reply ("""{"code":0,"data":[{"id":11,"hid":"5","homeName":"Garden"}]}""");
		Assert.That ((await _client.GetInvitationsAsync ()).Single ().HomeName, Is.EqualTo ("Garden"));
		}
	[TestCase (true)]
	[TestCase (false)]
	public async Task InvitationDecisionIsBounded (bool accept)
		{
		const string invite = """{"code":0,"data":[{"id":11,"hid":"5","homeName":"Garden"}]}""";
		_handler.Reply (invite);
		var expected = (await _client.GetInvitationsAsync ()).Single ();
		_handler.Reply (invite);
		_handler.Reply ("{\"code\":0}");
		await _client.RespondToInvitationAsync (expected, accept);
		Assert.That (_handler.Requests.Last ().Body, Is.EqualTo ("{\"id\":11,\"acceptFlag\":" + (accept ? "1" : "0") + "}"));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.RespondToInvitationAsync (expected, accept));
		}
	[TestCase (128, RainPointEventKind.WaterLeak)]
	[TestCase (129, RainPointEventKind.ExcessiveWaterUsage)]
	[TestCase (133, RainPointEventKind.Freeze)]
	[TestCase (136, RainPointEventKind.WaterShortage)]
	[TestCase (141, RainPointEventKind.ValveFailure)]
	[TestCase (143, RainPointEventKind.LowBattery)]
	[TestCase (10, RainPointEventKind.AlarmCleared)]
	[TestCase (999, RainPointEventKind.Unknown)]
	public void HistoricalAlarmCodesDoNotUseStatusBitFlags (int code, RainPointEventKind kind) => Assert.That (new RainPointEvent { Code = code }.Kind, Is.EqualTo (kind));
	}
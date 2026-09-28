// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using NUnit.Framework;
namespace RainPointClient.Tests;

[TestFixture]
public sealed class DeviceAdministrationTests
	{
	private ScriptedHandler _handler = null!;
	private HttpClient _http = null!;
	private RainPointCloudClient _client = null!;
	private const string HUBS = """{"code":0,"data":[{"mid":101,"name":"Hub","deviceName":"fixture","productKey":"fixture","model":"HWG023WBRF","subDevices":[{"sid":201,"addr":2,"model":"HTV345FRF","portNumber":3,"name":"Timer"}]}]}""";
	private const string MEMBERS = """{"code":0,"data":[{"uid":10,"hid":5,"nickname":"Member","owner":0,"rightCode":0}]}""";
	[SetUp]
	public async Task Setup ()
		{
		_handler = new ();
		_http = new (_handler, false);
		_client = new (_http);
		_handler.Reply ("""{"code":0,"data":{"token":"fixture","tokenExpired":3600}}""");
		await _client.LoginAsync ("fixture@example.invalid", "fixture", "44");
		}
	[TearDown]
	public void Cleanup ()
		{
		_client.Dispose ();
		_http.Dispose ();
		_handler.Dispose ();
		}
	private async Task<RainPointHomeDetails> Home (string? value = null)
		{
		_handler.Reply (value ?? AdministrationTests.HOME);
		return await _client.GetHomeAsync (5);
		}
	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public async Task RoomAssignmentsSupportEveryZoneWithoutValveCommands (int zone)
		{
		var home = await Home ();
		_handler.Reply (HUBS);
		_handler.Reply (AdministrationTests.HOME);
		_handler.Reply ("{\"code\":0}");
		await _client.SetRoomDevicesAsync (home, 9, new[] { new RainPointRoomDevice (101, 201, zone) });
		Assert.That (_handler.Requests.Last ().Path, Is.EqualTo ("/app/member/appHome/room/update"));
		Assert.That (_handler.Requests.Last ().Body, Is.EqualTo ("{\"hid\":5,\"rid\":9,\"devices\":\"101#201#" + zone + "\"}"));
		}
	[TestCase ("101#201#3")]
	[TestCase ("101-2-3")]
	public async Task LegacyAndCurrentAssignmentsResolveToSameDevice (string encoded)
		{
		var home = await Home (AdministrationTests.HOME.Replace ("101#201#3", encoded));
		_handler.Reply (HUBS);
		var device = (await _client.GetRoomDevicesAsync (home, 9)).Single ();
		Assert.That (device.DeviceId, Is.EqualTo (201));
		Assert.That (device.Zone, Is.EqualTo (3));
		}
	[TestCase ("101#201#4")]
	[TestCase ("101#999#1")]
	[TestCase ("102#201#1")]
	[TestCase ("101#201#3,101#201#3")]
	public async Task InvalidOrUnavailableAssignmentsAreNotSilentlyDropped (string encoded)
		{
		var home = await Home (AdministrationTests.HOME.Replace ("101#201#3", encoded));
		_handler.Reply (HUBS);
		Assert.ThrowsAsync<RainPointException> (async () => await _client.GetRoomDevicesAsync (home, 9));
		}
	[TestCase (false)]
	[TestCase (true)]
	public async Task RenameOnlySendsNameAndIdentity (bool child)
		{
		var home = await Home ();
		_handler.Reply (HUBS);
		var hub = (await _client.GetHubsAsync (5)).Single ();
		_handler.Reply (HUBS);
		_handler.Reply (AdministrationTests.HOME);
		_handler.Reply ("{\"code\":0}");
		if (child)
			await _client.RenameDeviceAsync (home, hub, 2, "New");
		else
			await _client.RenameHubAsync (home, hub, "New");
		Assert.That (_handler.Requests.Last ().Body, Is.EqualTo (child ? "{\"mid\":101,\"sid\":201,\"name\":\"New\"}" : "{\"mid\":101,\"name\":\"New\"}"));
		}
	[Test]
	public async Task ReplacementChildPreventsRename ()
		{
		var home = await Home ();
		_handler.Reply (HUBS);
		var hub = (await _client.GetHubsAsync (5)).Single ();
		_handler.Reply (HUBS.Replace ("201", "202"));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.RenameDeviceAsync (home, hub, 2, "New"));
		Assert.That (_handler.Requests.Count, Is.EqualTo (4));
		}
	[TestCase (0)]
	[TestCase (1)]
	[TestCase (2)]
	public async Task MemberOperationsUseObservedMemberAndExactPayload (int operation)
		{
		var home = await Home ();
		_handler.Reply (MEMBERS);
		var member = (await _client.GetMembersAsync (5)).Single ();
		_handler.Reply (MEMBERS);
		_handler.Reply (AdministrationTests.HOME);
		_handler.Reply ("{\"code\":0}");
		string expected;
		if (operation == 0)
			{
			await _client.SetMemberRoleAsync (home, member, RainPointMemberRole.Administrator);
			expected = "{\"hid\":5,\"uid\":10,\"rightCode\":1}";
			}
		else if (operation == 1)
			{
			await _client.RemoveMemberAsync (home, member);
			expected = "{\"hid\":5,\"uid\":10}";
			}
		else
			{
			await _client.TransferHomeOwnershipAsync (home, member);
			expected = "{\"hid\":5,\"targetUid\":10}";
			}
		Assert.That (_handler.Requests.Last ().Body, Is.EqualTo (expected));
		}
	[Test]
	public async Task ChangedMemberBlocksRoleChange ()
		{
		var home = await Home ();
		_handler.Reply (MEMBERS);
		var member = (await _client.GetMembersAsync (5)).Single ();
		_handler.Reply (MEMBERS.Replace ("\"owner\":0", "\"owner\":1"));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.SetMemberRoleAsync (home, member, RainPointMemberRole.Administrator));
		Assert.That (_handler.Requests.Count, Is.EqualTo (4));
		}
	[TestCase (0)]
	[TestCase (1)]
	[TestCase (2)]
	public async Task ExplicitHomeLifecycleRequestsAreNotReplayed (int operation)
		{
		var home = await Home ();
		_handler.Reply (AdministrationTests.HOME);
		_handler.Reply ("{\"code\":0}");
		if (operation == 0)
			await _client.DeleteHomeAsync (home);
		else if (operation == 1)
			await _client.LeaveHomeAsync (home);
		else
			await _client.InviteMemberAsync (home, "member@example.invalid");
		Assert.That (_handler.Requests.Last ().Body, Is.EqualTo (operation == 2 ? "{\"hid\":5,\"email\":\"member@example.invalid\"}" : "{\"hid\":5}"));
		Assert.That (_handler.Requests.Count, Is.EqualTo (4));
		}
	[Test]
	public async Task CreateHomeUsesNamedRoomsAndIanaTimeZone ()
		{
		_handler.Reply ("{\"code\":0,\"data\":{\"hid\":5}}");
		_handler.Reply (AdministrationTests.HOME);
		await _client.CreateHomeAsync ("Garden", "Europe/London", new[] { "Bed" });
		Assert.That (_handler.Requests[1].Body, Is.EqualTo ("{\"homeName\":\"Garden\",\"zoneName\":\"Europe/London\",\"rooms\":[\"Bed\"]}"));
		}
	}
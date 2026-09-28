// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using NUnit.Framework;

using RainPointClient.Desktop.Core;
namespace RainPointClient.Tests;

[TestFixture]
public sealed class DashboardAdministrationTests
	{
	private ScriptedHandler _handler = null!;
	private HttpClient _http = null!;
	private Dashboard _dashboard = null!;
	[SetUp]
	public async Task Setup ()
		{
		_handler = new ();
		_http = new (_handler, false);
		_dashboard = new (new RainPointCloudClient (_http));
		_handler.Reply ("""{"code":0,"data":{"token":"fixture","tokenExpired":3600,"user":{"notice":1}}}""");
		_handler.Reply ("""{"code":0,"data":[{"hid":5,"homeName":"Garden"}]}""");
		await _dashboard.ConnectAsync ("fixture@example.invalid", "fixture", "44");
		_handler.Reply ("{\"code\":0,\"data\":[]}");
		await _dashboard.SelectHomeAsync (_dashboard.Homes.Single ());
		}
	[TearDown]
	public async Task Cleanup ()
		{
		await _dashboard.CloseAsync ();
		_http.Dispose ();
		_handler.Dispose ();
		}
	private async Task Load ()
		{
		_handler.Reply (AdministrationTests.HOME);
		_handler.Reply ("{\"code\":0,\"data\":[]}");
		await _dashboard.LoadAdministrationAsync ();
		}
	[Test]
	public async Task HomeWithoutHubCanBeManagedAndSelectionClearsDraft ()
		{
		Assert.That (_dashboard.CanReadAdministration, Is.True);
		await Load ();
		Assert.That (_dashboard.HomeNameDraft, Is.EqualTo ("Garden"));
		Assert.That (_dashboard.Rooms, Has.Count.EqualTo (1));
		Assert.That (_dashboard.CanWriteAdministration, Is.True);
		await _dashboard.SelectHomeAsync (null);
		Assert.That (_dashboard.CanWriteAdministration, Is.False);
		Assert.That (_dashboard.Rooms, Is.Empty);
		Assert.That (_dashboard.HomeNameDraft, Is.Empty);
		}
	[TestCase (false)]
	[TestCase (true)]
	public async Task AttemptRequiresReloadOnSuccessOrFailure (bool fail)
		{
		await Load ();
		_dashboard.HomeNameDraft = "New";
		_handler.Reply (AdministrationTests.HOME);
		_handler.Reply (fail ? "{\"code\":42}" : "{\"code\":0}");
		await _dashboard.SaveHomeNameAsync ();
		Assert.That (_dashboard.CanWriteAdministration, Is.False);
		int count = _handler.Requests.Count;
		await _dashboard.SaveHomeNameAsync ();
		Assert.That (_handler.Requests.Count, Is.EqualTo (count));
		}
	[Test]
	public async Task FailedReadDoesNotLeaveOldWritableState ()
		{
		await Load ();
		_handler.Reply ("{\"code\":42}");
		await _dashboard.LoadAdministrationAsync ();
		Assert.That (_dashboard.CanWriteAdministration, Is.False);
		Assert.That (_dashboard.Rooms, Is.Empty);
		}
	[Test]
	public async Task InvalidLocationDoesNotConsumeHomeOrContactCloud ()
		{
		await Load ();
		_dashboard.LatitudeDraft = "invalid";
		int count = _handler.Requests.Count;
		await _dashboard.SaveHomeLocationAsync ();
		Assert.That (_handler.Requests.Count, Is.EqualTo (count));
		Assert.That (_dashboard.CanWriteAdministration, Is.True);
		}
	[Test]
	public async Task NotificationObservationIsNotPresentedAsReadBack ()
		{
		_dashboard.LoadNotificationPreferences ();
		Assert.That (_dashboard.MobileNotifications, Is.True);
		Assert.That (_dashboard.EmailNotifications, Is.False);
		_handler.Reply ("{\"code\":0}");
		await _dashboard.SaveNotificationPreferencesAsync ();
		Assert.That (_dashboard.CanWriteNotifications, Is.False);
		Assert.That (_dashboard.NotificationMessage, Does.Contain ("Sign in again"));
		}

	[Test]
	public async Task DateFormatLoadsAndExplicitChangeUsesUnitsSave ()
		{
		await Load ();
		Assert.That (_dashboard.DateFormat, Is.Not.Null);
		_dashboard.DateFormat = RainPointDateFormat.DayDashMonthYear;
		_handler.Reply (AdministrationTests.HOME);
		_handler.Reply ("{\"code\":0}");
		await _dashboard.SaveDisplayUnitsAsync ();
		using var json = System.Text.Json.JsonDocument.Parse (_handler.Requests.Last ().Body!);
		Assert.That (json.RootElement.GetProperty ("unit").GetString ()!.Substring (2, 2), Is.EqualTo ("0A"));
		}
	}
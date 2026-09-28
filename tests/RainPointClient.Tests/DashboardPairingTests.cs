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
public sealed class DashboardPairingTests
	{
	private ScriptedHandler _handler = null!; private HttpClient _http = null!; private Dashboard _dashboard = null!;
	[SetUp]
	public async Task Setup ()
		{
		_handler = new ();
		_http = new (_handler, false);
		_dashboard = new (new RainPointCloudClient (_http));
		_handler.Reply ("""{"code":0,"data":{"token":"fixture","tokenExpired":3600}}""");
		_handler.Reply ("""{"code":0,"data":[{"hid":5,"homeName":"Garden"}]}""");
		await _dashboard.ConnectAsync ("fixture@example.invalid", "fixture", "44");
		_handler.Reply (DeviceLifecycleTests.Hubs ());
		await _dashboard.SelectHomeAsync (_dashboard.Homes.Single ());
		_dashboard.SelectHub (_dashboard.Hubs.Single ());
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
		_handler.Reply (DeviceLifecycleTests.Hubs ());
		_handler.Reply (DeviceLifecycleTests.Catalog);
		await _dashboard.LoadPairingAsync ();
		_dashboard.PairingModel = _dashboard.PairingModels.First ();
		_dashboard.PairingChild = _dashboard.PairedDevices.First ();
		}
	[Test]
	public async Task ReadsAndSelectionChangesDoNotPairOrRemove ()
		{
		await Load ();
		Assert.That (_dashboard.CanStartPairing, Is.True);
		Assert.That (_dashboard.CanRemoveChild, Is.True);
		_dashboard.SelectHub (null);
		Assert.That (_dashboard.CanStartPairing, Is.False);
		Assert.That (_dashboard.PairedDevices, Is.Empty);
		Assert.That (_handler.Requests.Count (r => r.Method == HttpMethod.Post), Is.EqualTo (1));
		}
	[TestCase (true)]
	[TestCase (false)]
	public async Task SearchUncertaintyKeepsExplicitCancellationAvailable (bool success)
		{
		await Load ();
		_handler.Reply (DeviceLifecycleTests.Hubs ());
		_handler.Reply (DeviceLifecycleTests.Catalog);
		_handler.Reply (AdministrationTests.HOME);
		_handler.Reply (success ? "{\"code\":0,\"data\":{\"time\":120}}" : "{\"code\":42}");
		await _dashboard.StartPairingAsync ();
		Assert.That (_dashboard.CanStartPairing, Is.False);
		Assert.That (_dashboard.CanCancelPairing, Is.True);
		_handler.Reply ("{\"code\":0}");
		await _dashboard.CancelPairingAsync ();
		Assert.That (_dashboard.CanCancelPairing, Is.False);
		}
	[TestCase (true)]
	[TestCase (false)]
	public async Task AnyRemovalAttemptClearsDisplayedDevices (bool success)
		{
		await Load ();
		_handler.Reply (DeviceLifecycleTests.Hubs ());
		_handler.Reply (AdministrationTests.HOME);
		_handler.Reply (success ? "{\"code\":0}" : "{\"code\":42}");
		await _dashboard.RemovePairedDeviceAsync (false);
		Assert.That (_dashboard.Hubs, Is.Empty);
		Assert.That (_dashboard.Timers, Is.Empty);
		Assert.That (_dashboard.SelectedHub, Is.Null);
		Assert.That (_dashboard.CanRemoveChild, Is.False);
		}
	}
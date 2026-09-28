using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using RainPointClient.Desktop.Core;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class DashboardTests
	{
	private ScriptedHandler _handler = null!;
	private HttpClient _http = null!;
	private Dashboard _dashboard = null!;
	private const string Hubs = """{"code":0,"data":[{"mid":101,"name":"Garden","deviceName":"fixture","productKey":"fixture-key","model":"HWG023WBRF-V2","subDevices":[{"addr":1,"name":"Timer","model":"HTV345FRF"},{"addr":2,"name":"Other timer","model":"HTV345FRF"}]}]}""";
	private const string Status = """{"code":0,"data":[{"mid":101,"status":[{"id":"connected","value":"1"},{"id":"state","value":"0,-38"},{"id":"D01","value":"11#19D800299F0E000000","time":1700000000000}]}]}""";

	[SetUp]
	public void SetUp ()
		{
		_handler = new ScriptedHandler ();
		_http = new HttpClient (_handler, false);
		_dashboard = new Dashboard (new RainPointCloudClient (_http));
		}

	[TearDown]
	public async Task TearDown ()
		{
		await _dashboard.CloseAsync ();
		_http.Dispose ();
		_handler.Dispose ();
		}

	private async Task SelectTimerAsync (string? firmware = null)
		{
		_handler.Reply ("""{"code":0,"data":{"token":"fixture-session","tokenExpired":3600}}""");
		_handler.Reply ("""{"code":0,"data":[{"hid":42,"homeName":"Test home"}]}""");
		await _dashboard.ConnectAsync ("test@example.invalid", "test-password", "44");
		_handler.Reply (firmware is null ? Hubs : Hubs.Replace ("\"model\":\"HTV345FRF\"", "\"model\":\"HTV345FRF\",\"softVer\":\"" + firmware + "\""));
		await _dashboard.SelectHomeAsync (_dashboard.Homes.Single ());
		_dashboard.SelectHub (_dashboard.Hubs.Single ());
		_dashboard.SelectTimer (_dashboard.Timers.First ());
		}

	[TestCase (1, 1)]
	[TestCase (2, 1)]
	[TestCase (3, 1)]
	[TestCase (1, 2)]
	[TestCase (2, 2)]
	[TestCase (3, 2)]
	public async Task ManualModeUsesSelectedUnitsAndDisarmsOnModeChange (int zone, int mode)
		{
		await SelectTimerAsync ("130");
		_dashboard.ControlZone = zone;
		_dashboard.ControlsArmed = true;
		_dashboard.ManualMode = mode;
		Assert.That (_dashboard.ControlsArmed, Is.False);
		Assert.That (_dashboard.CanEditManualIntervals, Is.True);
		_dashboard.ControlsArmed = true;
		Assert.That (_dashboard.CanStart, Is.True);
		_handler.Reply ("{\"code\":0}");
		await _dashboard.StartSelectedZoneAsync ();
		Assert.That (_handler.Requests.Last ().Body, Does.Contain ("\"port\":" + zone)
			 .And.Contain ("\"mode\":" + (mode + 1))
			 .And.Contain ("\"duration\":" + (mode == 1 ? 60 : 5))
			 .And.Contain (mode == 1 ? "0A001400" : "01000100"));
		_handler.Reply ("{\"code\":0,\"data\":[{\"mid\":101,\"status\":[{\"id\":\"D01\",\"value\":\"11#" + (0x18 + zone).ToString ("X2") + "D8" + (mode + 1).ToString ("X2") + "\"}]}]}");
		await _dashboard.RefreshAsync ();
		Assert.That (_dashboard.Zones[zone - 1].State, Is.EqualTo (mode == 1 ? "Reported misting" : "Reported cycling"));
		_dashboard.ManualBurst = "0";
		Assert.That (_dashboard.CanStart, Is.False);
		Assert.That (_dashboard.CanStop, Is.True);
		_handler.Reply ("{\"code\":0}");
		await _dashboard.StopSelectedZoneAsync ();
		Assert.That (_handler.Requests.Last ().Body, Does.Contain ("\"mode\":0"));
		}

	[TestCase (1)]
	[TestCase (2)]
	public async Task ManualModeRequiresKnownFirmwareButStopDoesNot (int mode)
		{
		await SelectTimerAsync ();
		_dashboard.ManualMode = mode;
		_dashboard.ControlsArmed = true;
		int before = _handler.Requests.Count;
		await _dashboard.StartSelectedZoneAsync ();
		Assert.That (_handler.Requests.Count, Is.EqualTo (before));
		Assert.That (_dashboard.ManualModeHint, Does.Contain ("firmware"));
		Assert.That (_dashboard.CanStop, Is.True);
		}

	[Test]
	public async Task ConstructionAndUnarmedCommandsNeverContactCloud ()
		{
		await _dashboard.StartSelectedZoneAsync ();
		await _dashboard.StopSelectedZoneAsync ();
		await _dashboard.StartSessionRecoveryAsync (action => action ());
		Assert.That (_dashboard.RecoveryText, Is.EqualTo ("Automatic session renewal is off."));
		await _dashboard.StartSessionRecoveryAsync (action => action ());
		Assert.That (_dashboard.RecoveryText, Is.EqualTo ("Automatic session renewal is off."));
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (_handler.Requests, Is.Empty);
			Assert.That (_dashboard.CanStart, Is.False);
			Assert.That (_dashboard.LastReceivedAt, Is.Null);
			}
		}

	[Test]
	public async Task FeedbackUsesReportedStateAndKeepsReceiptSeparateFromCloudTimestamp ()
		{
		await SelectTimerAsync ();
		_handler.Reply (Status);
		await _dashboard.RefreshAsync ();
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (_dashboard.Zones[0].State, Is.EqualTo ("Reported closed"));
			Assert.That (_dashboard.Zones[0].Usage, Does.StartWith (1.4m.ToString ("0.0")));
			Assert.That (_dashboard.Zones[1].State, Is.EqualTo ("Unknown"));
			Assert.That (_dashboard.Zones[1].Usage, Is.EqualTo ("Unknown"));
			Assert.That (_dashboard.HubText, Does.Contain ("-38 dBm"));
			Assert.That (_dashboard.TimerText, Does.Contain ("2023-11-14"));
			Assert.That (_dashboard.LastReceivedAt, Is.GreaterThan (DateTimeOffset.UtcNow.AddMinutes (-1)));
			}
		}

	[TestCase (1, 0, "cloud accepted")]
	[TestCase (2, 0, "cloud accepted")]
	[TestCase (3, 0, "cloud accepted")]
	[TestCase (1, 4, "already requested or transitioning")]
	[TestCase (2, 4, "already requested or transitioning")]
	[TestCase (3, 4, "already requested or transitioning")]
	public async Task CommandAddressesSelectedZoneAndDoesNotOptimisticallyChangeFeedback (int zone, int code, string expected)
		{
		await SelectTimerAsync ();
		_handler.Reply (Status);
		await _dashboard.RefreshAsync ();
		DateTimeOffset? received = _dashboard.LastReceivedAt;
		_dashboard.ControlZone = zone;
		_dashboard.ControlsArmed = true;
		_handler.Reply ("{\"code\":" + code + "}");
		await _dashboard.StartSelectedZoneAsync ();
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (_handler.Requests.Last ().Body, Does.Contain ("\"port\":" + zone).And.Contain ("\"duration\":60"));
			Assert.That (_dashboard.Zones[0].State, Is.EqualTo ("Reported closed"));
			Assert.That (_dashboard.LastReceivedAt, Is.EqualTo (received));
			Assert.That (_dashboard.CommandMessage, Does.Contain (expected).And.Contain ("not confirmed"));
			Assert.That (_handler.Steps, Is.Empty);
			}
		}

	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public async Task CommandResponseIsDisplayedSeparatelyAndSelectionClearsIt (int zone)
		{
		await SelectTimerAsync ();
		_handler.Reply (Status);
		await _dashboard.RefreshAsync ();
		DateTimeOffset? received = _dashboard.LastReceivedAt;
		_dashboard.ControlZone = zone;
		_dashboard.ControlsArmed = true;
		string state = "11#" + (0x18 + zone).ToString ("X2") + "D801" + (0x28 + zone).ToString ("X2") + "9F0E000000";
		_handler.Reply ("{\"code\":0,\"data\":{\"state\":\"" + state + "\",\"timestamp\":1700000000000}}");
		await _dashboard.StopSelectedZoneAsync ();
		Assert.That (_dashboard.CommandMessage, Does.Contain ("cloud accepted").And.Contain ("Response only: Reported open")
			 .And.Contain ("last usage " + 1.4m.ToString ("0.0") + " L").And.Contain ("2023-11-14").And.Contain ("not confirmed"));
		Assert.That (_dashboard.Zones[0].State, Is.EqualTo ("Reported closed"));
		Assert.That (_dashboard.LastReceivedAt, Is.EqualTo (received));
		_dashboard.ControlZone = zone == 1 ? 2 : 1;
		Assert.That (_dashboard.CommandMessage, Does.Not.Contain ("Response only"));
		}

	[TestCase ("{\"code\":0}", "No status was included")]
	[TestCase ("{\"code\":0,\"data\":[]}", "could not be decoded")]
	public async Task AbsentOrUnsupportedResponseDoesNotClaimCommandFailure (string response, string expected)
		{
		await SelectTimerAsync ();
		_dashboard.ControlsArmed = true;
		_handler.Reply (response);
		await _dashboard.StopSelectedZoneAsync ();
		Assert.That (_dashboard.CommandMessage, Does.Contain ("cloud accepted").And.Contain (expected));
		Assert.That (_dashboard.Zones.All (item => item.State == "Unknown"), Is.True);
		}

	[TestCase ("0")]
	[TestCase ("721")]
	[TestCase ("1.5")]
	[TestCase ("garbage")]
	public async Task InvalidDurationBlocksStartButLeavesStopAvailable (string duration)
		{
		await SelectTimerAsync ();
		_dashboard.ControlsArmed = true;
		_dashboard.DurationMinutes = duration;
		int requests = _handler.Requests.Count;
		await _dashboard.StartSelectedZoneAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (requests));
		Assert.That (_dashboard.CanStop, Is.True);
		_handler.Reply ("{\"code\":0}");
		await _dashboard.StopSelectedZoneAsync ();
		Assert.That (_handler.Requests.Last ().Body, Does.Contain ("\"port\":1").And.Contain ("\"duration\":0").And.Contain ("\"mode\":0"));
		}

	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public async Task SwitchingZoneDisarmsAndStopAddressesSelectedZone (int zone)
		{
		await SelectTimerAsync ();
		_dashboard.ControlZone = zone == 1 ? 2 : 1;
		_dashboard.ControlsArmed = true;
		_dashboard.ControlZone = zone;
		Assert.That (_dashboard.ControlsArmed, Is.False);
		int before = _handler.Requests.Count;
		await _dashboard.StartSelectedZoneAsync ();
		await _dashboard.StopSelectedZoneAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (before));
		_dashboard.ControlsArmed = true;
		_dashboard.DurationMinutes = "invalid";
		_handler.Reply ("{\"code\":0}");
		await _dashboard.StopSelectedZoneAsync ();
		Assert.That (_handler.Requests.Last ().Body, Does.Contain ("\"port\":" + zone).And.Contain ("\"mode\":0"));
		Assert.That (_dashboard.CommandMessage, Does.Contain ("zone " + zone + " stop"));
		_dashboard.ControlZone = 4;
		Assert.That (_dashboard.ControlZone, Is.EqualTo (zone));
		}

	[Test]
	public async Task SwitchingTimerDisarmsAndDoesNotReuseOtherTimersReadings ()
		{
		await SelectTimerAsync ();
		_handler.Reply (Status);
		await _dashboard.RefreshAsync ();
		_dashboard.ControlsArmed = true;
		_dashboard.SelectTimer (_dashboard.Timers.Last ());
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (_dashboard.ControlsArmed, Is.False);
			Assert.That (_dashboard.CanStart, Is.False);
			Assert.That (_dashboard.Zones, Has.Count.EqualTo (3));
			Assert.That (_dashboard.Zones.All (row => row.State == "Unknown"), Is.True);
			Assert.That (_dashboard.TimerText, Does.Contain ("RF address 2"));
			}
		}

	[Test]
	public async Task FailedReadRetainsTimestampAndExplicitlyMarksPreviousReadings ()
		{
		await SelectTimerAsync ();
		_handler.Reply (Status);
		await _dashboard.RefreshAsync ();
		DateTimeOffset? received = _dashboard.LastReceivedAt;
		_handler.Steps.Enqueue ((_, _) => throw new HttpRequestException ("secret transport details"));
		await _dashboard.RefreshAsync ();
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (_dashboard.LastReceivedAt, Is.EqualTo (received));
			Assert.That (_dashboard.Message, Does.Contain ("last successful read").And.Not.Contain ("secret"));
			Assert.That (_dashboard.Zones[0].State, Is.EqualTo ("Reported closed"));
			}
		}

	[Test]
	public async Task UncertainCommandIsNotRetriedAndDoesNotLeakTransportDetails ()
		{
		await SelectTimerAsync ();
		_dashboard.ControlsArmed = true;
		int before = _handler.Requests.Count;
		_handler.Steps.Enqueue ((_, _) => throw new HttpRequestException ("secret transport details"));
		await _dashboard.StartSelectedZoneAsync ();
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (_handler.Requests, Has.Count.EqualTo (before + 1));
			Assert.That (_dashboard.CommandMessage, Does.Contain ("outcome is unknown").And.Not.Contain ("secret"));
			Assert.That (_dashboard.CanStop, Is.True);
			}
		}

	[Test]
	public async Task BusyReadBlocksCommandsAndSelectionAndCloseCancelsSafely ()
		{
		await SelectTimerAsync ();
		_dashboard.ControlsArmed = true;
		_handler.Steps.Enqueue (async (_, token) =>
		{
			await Task.Delay (Timeout.Infinite, token);
			throw new InvalidOperationException ("Unreachable");
		});
		Task read = _dashboard.RefreshAsync ();
		int before = _handler.Requests.Count;
		Assert.That (_dashboard.IsBusy, Is.True);
		_dashboard.SelectTimer (_dashboard.Timers.Last ());
		await _dashboard.StartSelectedZoneAsync ();
		await _dashboard.CloseAsync ();
		await read;
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (_handler.Requests, Has.Count.EqualTo (before));
			Assert.That (_dashboard.SelectedTimer!.Address, Is.EqualTo (1));
			Assert.That (_dashboard.IsBusy, Is.False);
			Assert.That (_dashboard.CanEdit, Is.False);
			}
		}

	[Test]
	public async Task MonitoringUsesUiDispatchAndDiscardsQueuedUpdatesAfterSelectionChanges ()
		{
		await SelectTimerAsync ();
		_handler.Reply (Status);
		System.Collections.Concurrent.ConcurrentQueue<Action> pending = new ();
		TaskCompletionSource<bool> queued = new (TaskCreationOptions.RunContinuationsAsynchronously);
		await _dashboard.StartMonitoringAsync (action => { pending.Enqueue (action); queued.TrySetResult (true); }, new RainPointMonitorOptions { EnablePush = false });
		Assert.That (_dashboard.IsMonitoring, Is.True);
		Assert.That (await Task.WhenAny (queued.Task, Task.Delay (5000)), Is.SameAs (queued.Task));
		// Stop joins the worker; queued UI callbacks must not restore the previous selection's feedback.
		await _dashboard.SelectHubAsync (null);
		while (pending.TryDequeue (out Action? action))
			action ();
		Assert.That (_dashboard.IsMonitoring, Is.False);
		Assert.That (_dashboard.SelectedHub, Is.Null);
		Assert.That (_dashboard.Zones, Is.Empty);
		Assert.That (_dashboard.LastReceivedAt, Is.Null);
		}

	[Test]
	public async Task SignOutFailureStillClearsAccountFeedbackAndControlArming ()
		{
		await SelectTimerAsync ();
		_dashboard.ControlsArmed = true;
		_handler.Steps.Enqueue ((_, _) => throw new HttpRequestException ("offline"));
		await _dashboard.DisconnectAsync ();
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (_dashboard.Homes, Is.Empty);
			Assert.That (_dashboard.SelectedTimer, Is.Null);
			Assert.That (_dashboard.Zones, Is.Empty);
			Assert.That (_dashboard.ControlsArmed, Is.False);
			Assert.That (_dashboard.CanConnect, Is.True);
			}
		}
	}
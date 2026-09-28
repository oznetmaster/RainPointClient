using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class ManualWateringTests
	{
	private ScriptedHandler _handler = null!;
	private HttpClient _http = null!;
	private RainPointCloudClient _client = null!;
	private static RainPointHub Hub (string? version = "130", string model = "HTV345FRF") => new ()
		{
		Id = 101,
		DeviceName = "fixture",
		ProductKey = "fixture-key",
		Model = "HWG023WBRF",
		Devices = new[] { new RainPointDevice { Address = 2, Model = model, FirmwareVersion = version } }
		};
	[SetUp]
	public void SetUp ()
		{
		_handler = new ();
		_http = new (_handler, false);
		_client = new (_http);
		}
	[TearDown]
	public void TearDown ()
		{
		_client.Dispose ();
		_http.Dispose ();
		_handler.Dispose ();
		}
	private async Task Login ()
		{
		_handler.Reply ("""{"code":0,"data":{"token":"fixture-session","tokenExpired":3600}}""");
		await _client.LoginAsync ("test@example.invalid", "password", "44");
		}
	private Task<RainPointWateringCommandResult> Start (bool misting, int zone, TimeSpan duration, TimeSpan burst, TimeSpan pause,
		 RainPointHub? hub = null, CancellationToken token = default) => misting
		 ? _client.StartMistingAsync (hub ?? Hub (), 2, zone, duration, burst, pause, token)
		 : _client.StartCycleAndSoakAsync (hub ?? Hub (), 2, zone, duration, burst, pause, token);

	[TestCase (1, true, 0)]
	[TestCase (2, true, 4)]
	[TestCase (3, true, 0)]
	[TestCase (1, false, 4)]
	[TestCase (2, false, 0)]
	[TestCase (3, false, 0)]
	public async Task SendsExactManualModeUnitsAndTypedResponseForEveryZone (int zone, bool misting, int code)
		{
		await Login ();
		_handler.Reply ("{\"code\":" + code + ",\"data\":\"11#19D800\"}");
		var result = await Start (misting, zone, TimeSpan.FromMinutes (600),
			 misting ? TimeSpan.FromSeconds (300) : TimeSpan.FromMinutes (300),
			 misting ? TimeSpan.FromSeconds (3600) : TimeSpan.FromMinutes (720));
		using JsonDocument json = JsonDocument.Parse (_handler.Requests.Last ().Body!);
		JsonElement body = json.RootElement;
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (_handler.Requests.Last ().Path, Is.EqualTo ("/app/device/controlWorkMode"));
			Assert.That (body.GetProperty ("mode").GetInt32 (), Is.EqualTo (misting ? 2 : 3));
			Assert.That (body.GetProperty ("duration").GetInt32 (), Is.EqualTo (misting ? 36000 : 600));
			Assert.That (body.GetProperty ("param").GetString (), Is.EqualTo (misting ? "2C01100E" : "2C01D002"));
			Assert.That (body.GetProperty ("mid").GetInt64 (), Is.EqualTo (101));
			Assert.That (body.GetProperty ("addr").GetInt32 (), Is.EqualTo (2));
			Assert.That (body.GetProperty ("port").GetInt32 (), Is.EqualTo (zone));
			Assert.That (result.RequestedZone, Is.EqualTo (zone));
			Assert.That (result.Outcome, Is.EqualTo (code == 0 ? RainPointCommandOutcome.Accepted : RainPointCommandOutcome.AlreadyInRequestedStateOrTransitioning));
			Assert.That (result.Status.Zones[0].IsOpen, Is.False, "Requested start cannot fabricate an open reading.");
			Assert.That (_handler.Requests, Has.Count.EqualTo (2));
			}
		}

	[TestCase (true, 60, 5, 5)]
	[TestCase (true, 43200, 3600, 3600)]
	[TestCase (false, 300, 60, 60)]
	[TestCase (false, 86400, 43200, 43200)]
	public async Task ExactBoundariesAreAccepted (bool misting, int duration, int burst, int pause)
		{
		await Login ();
		_handler.Reply ("{\"code\":0}");
		await Start (misting, 1, TimeSpan.FromSeconds (duration), TimeSpan.FromSeconds (burst), TimeSpan.FromSeconds (pause));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[TestCase (true, 0, 5, 5)]
	[TestCase (true, 59, 5, 5)]
	[TestCase (true, 61, 5, 5)]
	[TestCase (true, 43260, 5, 5)]
	[TestCase (true, 60, 4, 5)]
	[TestCase (true, 60, 5.5, 5)]
	[TestCase (true, 60, 3601, 5)]
	[TestCase (true, 60, 5, 4)]
	[TestCase (true, 60, 5, 3601)]
	[TestCase (false, 240, 60, 60)]
	[TestCase (false, 301, 60, 60)]
	[TestCase (false, 86460, 60, 60)]
	[TestCase (false, 300, 0, 60)]
	[TestCase (false, 300, 61, 60)]
	[TestCase (false, 300, 360, 60)]
	[TestCase (false, 86400, 43260, 60)]
	[TestCase (false, 300, 60, 0)]
	[TestCase (false, 300, 60, 61)]
	[TestCase (false, 300, 60, 43260)]
	public void InvalidTimingsNeverContactCloud (bool misting, double duration, double burst, double pause)
		{
		Assert.ThrowsAsync<ArgumentOutOfRangeException> (async () => await Start (misting, 1,
			 TimeSpan.FromSeconds (duration), TimeSpan.FromSeconds (burst), TimeSpan.FromSeconds (pause)));
		Assert.That (_handler.Requests, Is.Empty);
		}

	[TestCase (null)]
	[TestCase ("")]
	[TestCase ("119")]
	[TestCase ("1.1.130")]
	[TestCase ("-130")]
	public void UnknownOrOldFirmwareNeverReceivesNewModes (string? firmware)
		{
		Assert.That (Hub (firmware).Devices[0].SupportsManualCycles, Is.False);
		foreach (bool misting in new[] { true, false })
			Assert.ThrowsAsync<NotSupportedException> (async () => await Start (misting, 1,
				 TimeSpan.FromMinutes (5), TimeSpan.FromMinutes (1), TimeSpan.FromMinutes (1), Hub (firmware)));
		Assert.That (_handler.Requests, Is.Empty);
		}

	[TestCase (0)]
	[TestCase (4)]
	public void InvalidZoneNeverContactsCloud (int zone)
		{
		foreach (bool misting in new[] { true, false })
			Assert.ThrowsAsync<ArgumentOutOfRangeException> (async () => await Start (misting, zone,
				 TimeSpan.FromMinutes (5), TimeSpan.FromMinutes (1), TimeSpan.FromMinutes (1)));
		Assert.That (_handler.Requests, Is.Empty);
		}

	[Test]
	public void UnsupportedDeviceDoesNotInheritFirmwareCapability ()
		{
		Assert.That (Hub ("130", "Other").Devices[0].SupportsManualCycles, Is.False);
		Assert.ThrowsAsync<NotSupportedException> (async () => await Start (true, 1,
			 TimeSpan.FromMinutes (1), TimeSpan.FromSeconds (10), TimeSpan.FromSeconds (20), Hub ("130", "Other")));
		Assert.That (_handler.Requests, Is.Empty);
		}

	[TestCase (true)]
	[TestCase (false)]
	public async Task UncertainStartIsNotReplayedAndNormalStopRemainsUsable (bool misting)
		{
		await Login ();
		_handler.Reply ("unavailable", HttpStatusCode.ServiceUnavailable);
		Assert.ThrowsAsync<RainPointException> (async () => await Start (misting, 1,
			 TimeSpan.FromMinutes (5), TimeSpan.FromMinutes (1), TimeSpan.FromMinutes (1)));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		_handler.Reply ("{\"code\":0}");
		await _client.StopWateringAsync (Hub (), 2, 1);
		using JsonDocument json = JsonDocument.Parse (_handler.Requests.Last ().Body!);
		Assert.That (json.RootElement.GetProperty ("mode").GetInt32 (), Is.Zero);
		Assert.That (json.RootElement.GetProperty ("duration").GetInt32 (), Is.Zero);
		Assert.That (json.RootElement.GetProperty ("param").GetString (), Is.Empty);
		}
	}
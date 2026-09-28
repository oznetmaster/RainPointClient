using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using NUnit.Framework;
namespace RainPointClient.Tests;

[TestFixture]
public sealed class DeviceLifecycleTests
	{
	private ScriptedHandler _handler = null!; private HttpClient _http = null!; private RainPointCloudClient _client = null!;
	internal const string Port = "58020a001e00038000000000d7,/,,646464646464646464646464,tail";
	internal static string Hubs (string? parameter = null, string sibling = "HCS021FRF") => JsonSerializer.Serialize (new { code = 0, data = new[] { new { mid = 101, name = "Hub", deviceName = "fixture", productKey = "fixture", model = "HWG023WBRF", modelCode = 289, subDevices = new[] { new { sid = 201, addr = 2, model = "HTV345FRF", modelCode = 271, portNumber = 3, param = parameter ?? string.Join ("|", Enumerable.Repeat (Port, 3)), style = "preserve-me", softVer = "130" }, new { sid = 202, addr = 3, model = sibling, modelCode = 72, portNumber = 1, param = "", style = "", softVer = "100" } } } } });
	internal const string Catalog = """{"code":0,"data":{"models":[{"model":"HTV345FRF","modelCode":271,"isMainDevice":false},{"model":"HCS021FRF","modelCode":72,"isMainDevice":false}]}}""";
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
	private async Task<(RainPointHomeDetails Home, RainPointHub Hub)> Observe (string? hubs = null)
		{
		_handler.Reply (AdministrationTests.HOME);
		var home = await _client.GetHomeAsync (5);
		_handler.Reply (hubs ?? Hubs ());
		return (home, (await _client.GetHubsAsync (5)).Single ());
		}
	private void BeforeWrite (string? hubs = null)
		{
		_handler.Reply (hubs ?? Hubs ());
		_handler.Reply (AdministrationTests.HOME);
		_handler.Reply ("{\"code\":0}");
		}
	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	[TestCase (0)]
	public async Task SensorRemovalClearsOnlyMatchingAssociationsAndPreservesOtherSettings (int zone)
		{
		string[] ports = Enumerable.Range (1, 3).Select (z => zone == 0 || z == zone ? Port : Port.Replace ("0380", "0480")).ToArray ();
		string original = string.Join ("|", ports);
		string hubs = Hubs (original);
		var state = await Observe (hubs);
		BeforeWrite (hubs);
		await _client.RemoveDeviceAsync (state.Home, state.Hub, 3);
		var request = _handler.Requests.Last ();
		Assert.That (request.Path, Is.EqualTo ("/app/device/sub/deleteSubDevice"));
		using var body = JsonDocument.Parse (request.Body!);
		var relation = body.RootElement.GetProperty ("updateList")[0];
		Assert.That (body.RootElement.GetProperty ("sid").GetString (), Is.EqualTo ("202"));
		Assert.That (relation.GetProperty ("sid").GetString (), Is.EqualTo ("201"));
		Assert.That (relation.GetProperty ("style").GetString (), Is.EqualTo ("preserve-me"));
		Assert.That (relation.GetProperty ("param").GetString (), Is.EqualTo (original.Replace ("0380", "0080")));
		int count = _handler.Requests.Count;
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.RemoveDeviceAsync (state.Home, state.Hub, 3));
		Assert.That (_handler.Requests, Has.Count.EqualTo (count));
		}
	[TestCase (true)]
	[TestCase (false)]
	public async Task HubAndTimerRemovalSendOneDeletionWithoutValveCommands (bool hub)
		{
		var state = await Observe ();
		BeforeWrite ();
		if (hub)
			await _client.RemoveHubAsync (state.Home, state.Hub);
		else
			await _client.RemoveDeviceAsync (state.Home, state.Hub, 2);
		using var body = JsonDocument.Parse (_handler.Requests.Last ().Body!);
		Assert.That (body.RootElement.GetProperty ("mid").GetString (), Is.EqualTo ("101"));
		if (!hub)
			Assert.That (body.RootElement.GetProperty ("updateList").GetArrayLength (), Is.Zero);
		Assert.That (_handler.Requests.Count (r => r.Method == HttpMethod.Post), Is.EqualTo (2));
		}
	[TestCase ("other")]
	[TestCase ("bad-param")]
	public async Task UnreadableOrUnsupportedRelationshipsBlockRemoval (string kind)
		{
		string hubs = kind == "other" ? Hubs (sibling: "Unknown") : Hubs ("unreadable");
		var state = await Observe (hubs);
		_handler.Reply (hubs);
		Assert.ThrowsAsync<NotSupportedException> (async () => await _client.RemoveDeviceAsync (state.Home, state.Hub, 3));
		Assert.That (_handler.Requests.Count (r => r.Method == HttpMethod.Post), Is.EqualTo (1));
		}
	[Test]
	public async Task ChangedTopologyBlocksDeletion ()
		{
		var state = await Observe ();
		_handler.Reply (Hubs ().Replace ("preserve-me", "changed"));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.RemoveHubAsync (state.Home, state.Hub));
		Assert.That (_handler.Requests.Count (r => r.Method == HttpMethod.Post), Is.EqualTo (1));
		}
	[TestCase (0)]
	[TestCase (1)]
	public async Task PairingUsesCurrentCatalogAndSupportsCancellation (int index)
		{
		var state = await Observe ();
		_handler.Reply (Catalog);
		var model = (await _client.GetProductCatalogAsync ()).Models[index];
		_handler.Reply (Hubs ());
		_handler.Reply (Catalog);
		_handler.Reply (AdministrationTests.HOME);
		_handler.Reply ("{\"code\":0,\"data\":{\"time\":120}}");
		var window = await _client.StartDevicePairingAsync (state.Home, state.Hub, model);
		Assert.That (window, Is.EqualTo (TimeSpan.FromSeconds (120)));
		using var body = JsonDocument.Parse (_handler.Requests.Last ().Body!);
		Assert.That (body.RootElement.GetProperty ("modelCode")[0].GetInt32 (), Is.EqualTo (model.ModelCode));
		Assert.That (body.RootElement.GetProperty ("parentModelCode").GetInt32 (), Is.EqualTo (289));
		Assert.That (body.RootElement.GetProperty ("did").GetString (), Is.Empty);
		_handler.Reply ("{\"code\":0}");
		await _client.CancelDevicePairingAsync (state.Hub);
		Assert.That (_handler.Requests.Last ().Body, Is.EqualTo ("{\"deviceName\":\"fixture\",\"productKey\":\"fixture\"}"));
		}
	[Test]
	public async Task UncertainDeletionConsumesObservation ()
		{
		var state = await Observe ();
		_handler.Reply (Hubs ());
		_handler.Reply (AdministrationTests.HOME);
		_handler.Reply ("{\"code\":42}");
		Assert.ThrowsAsync<RainPointException> (async () => await _client.RemoveHubAsync (state.Home, state.Hub));
		int count = _handler.Requests.Count;
		Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.RemoveHubAsync (state.Home, state.Hub));
		Assert.That (_handler.Requests, Has.Count.EqualTo (count));
		}
	}
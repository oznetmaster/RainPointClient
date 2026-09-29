// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class ZoneProfileTests
	{
	internal const string Profile = """[{"open":1,"target":[30]},{"open":0,"target":[60]},{"open":1,"target":[]}]""";
	internal const string Catalog = """{"code":0,"data":[{"id":1,"langField":"@plan_config_plant_type","subType":[{"id":30,"langField":"@plan_config_cool_turf","defaultFlag":1},{"id":31,"langField":"@plan_config_warm_turf","defaultFlag":0}]},{"id":2,"langField":"@plan_config_soil_type","subType":[{"id":60,"langField":"@plan_config_loam","defaultFlag":1},{"id":61,"langField":"@plan_config_sand","defaultFlag":0}]}]}""";
	internal static string Discovery (string? profile = Profile, long sid = 42) => JsonSerializer.Serialize (new { code = 0, data = new[] { new { mid = 101, deviceName = "hub", productKey = "product", model = "HWG023WBRF", subDevices = new[] { new { sid, addr = 2, model = "HTV345FRF", portNumber = 3, softVer = "130", planJson = profile, param = "untouched" } } } } });
	internal static RainPointHub Hub () => new () { HomeId = 5, Id = 101, DeviceName = "hub", ProductKey = "product", Model = "HWG023WBRF", Devices = new[] { new RainPointDevice { Id = 42, Address = 2, Model = "HTV345FRF", PortNumber = 3 } } };
	private ScriptedHandler _handler = null!; private HttpClient _http = null!; private RainPointCloudClient _client = null!;
	[SetUp]
	public async Task SetUp ()
		{
		_handler = new ();
		_http = new (_handler, false);
		_client = new (_http);
		_handler.Reply ("""{"code":0,"data":{"token":"fixture","tokenExpired":3600}}""");
		await _client.LoginAsync ("fixture@example.invalid", "fixture", "44");
		}
	[TearDown]
	public void TearDown ()
		{
		_client.Dispose ();
		_http.Dispose ();
		_handler.Dispose ();
		}
	private async Task<RainPointZoneProfileSnapshot> Read (int zone = 1, string? profile = Profile)
		{
		_handler.Reply (Discovery (profile));
		return await _client.GetZoneProfileAsync (Hub (), 2, zone);
		}
	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public async Task ReadsAndWritesOneZoneWithoutChangingOthers (int zone)
		{
		var before = await Read (zone);
		Assert.That (before.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
		Assert.That (before.RecommendationsEnabled, Is.EqualTo (zone != 2));
		_handler.Reply (Catalog);
		_handler.Reply (Discovery ());
		_handler.Reply ("{\"code\":0}");
		await _client.SetZoneProfileAsync (Hub (), before, false, new[] { 31, 61 });
		var request = _handler.Requests.Last ();
		Assert.That (request.Path, Is.EqualTo ("/app/device/sub/update"));
		var fields = JsonSerializer.Deserialize<Dictionary<string, object>> (request.Body!)!;
		Assert.That (fields.Keys, Is.EquivalentTo (new[] { "mid", "sid", "planJson" }));
		string payload = JsonSerializer.Deserialize<ProfileRequest> (request.Body!)!.Payload;
		for (int n = 1; n <= 3; n++)
			{
			var after = await Read (n, payload);
			if (n == zone)
				{
				Assert.That (after.RecommendationsEnabled, Is.False);
				Assert.That (after.SelectedOptionIds, Is.EqualTo (new[] { 31, 61 }));
				}
			else
				{
				Assert.That (after.RecommendationsEnabled, Is.EqualTo (n != 2));
				Assert.That (after.SelectedOptionIds, Is.EqualTo (n == 1 ? new[] { 30 } : n == 2 ? new[] { 60 } : Array.Empty<int> ()));
				}
			}
		int requests = _handler.Requests.Count;
		await Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.SetZoneProfileAsync (Hub (), before, true, new[] { 30 }));
		Assert.That (_handler.Requests, Has.Count.EqualTo (requests));
		}
	[TestCase (null)]
	[TestCase ("")]
	[TestCase ("null")]
	public async Task AbsentProfileUsesExplicitUnconfiguredDefaults (string? payload)
		{
		var profile = await Read (2, payload);
		Assert.That (profile.IsConfigured, Is.False);
		Assert.That (profile.RecommendationsEnabled, Is.False);
		Assert.That (profile.SelectedOptionIds, Is.Empty);
		}
	[TestCase ("[")]
	[TestCase ("[]")]
	[TestCase ("[{},{}]")]
	[TestCase ("[null,{},{}]")]
	[TestCase ("[{\"open\":2},{},{}]")]
	[TestCase ("[{\"target\":[1,1]},{},{}]")]
	[TestCase ("[{\"target\":[0]},{},{}]")]
	[TestCase ("[{\"target\":null},{},{}]")]
	[TestCase ("[{\"futureField\":1},{},{}]")]
	[TestCase ("[{\"open\":0,\"open\":1},{},{}]")]
	public async Task UnreadableProfileNeverBecomesWritableDefaults (string payload)
		{
		var profile = await Read (1, payload);
		Assert.That (profile.Availability, Is.EqualTo (TimerReadingAvailability.Malformed));
		Assert.That (profile.RecommendationsEnabled, Is.Null);
		await Assert.ThrowsAsync<ArgumentException> (async () => await _client.SetZoneProfileAsync (Hub (), profile, false, Array.Empty<int> ()));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}
	[TestCase ("[30,30]")]
	[TestCase ("[0]")]
	[TestCase ("[-1]")]
	public async Task InvalidSelectionCannotReachNetwork (string selection)
		{
		var before = await Read ();
		await Assert.ThrowsAsync<ArgumentException> (async () => await _client.SetZoneProfileAsync (Hub (), before, true, JsonSerializer.Deserialize<int[]> (selection)!));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}
	[TestCase ("[999]")]
	[TestCase ("[30,31]")]
	public async Task SelectionMustUseCurrentCatalogAndOneOptionPerCategory (string selection)
		{
		var before = await Read ();
		_handler.Reply (Catalog);
		await Assert.ThrowsAsync<ArgumentException> (async () => await _client.SetZoneProfileAsync (Hub (), before, true, JsonSerializer.Deserialize<int[]> (selection)!));
		Assert.That (_handler.Requests, Has.Count.EqualTo (3));
		}
	[Test]
	public async Task StaleProfileCannotOverwriteConcurrentChanges ()
		{
		var before = await Read ();
		_handler.Reply (Catalog);
		_handler.Reply (Discovery (Profile.Replace ("30", "31")));
		await Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.SetZoneProfileAsync (Hub (), before, false, new[] { 31 }));
		Assert.That (_handler.Requests.Count (r => r.Path == "/app/device/sub/update"), Is.Zero);
		}
	[Test]
	public async Task UncertainProfileWriteCannotReplay ()
		{
		var before = await Read ();
		_handler.Reply (Catalog);
		_handler.Reply (Discovery ());
		_handler.Steps.Enqueue ((_, _) => throw new HttpRequestException ("fixture"));
		await Assert.ThrowsAsync<HttpRequestException> (async () => await _client.SetZoneProfileAsync (Hub (), before, false, new[] { 31 }));
		await Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.SetZoneProfileAsync (Hub (), before, false, new[] { 31 }));
		Assert.That (_handler.Requests.Count (r => r.Path == "/app/device/sub/update"), Is.EqualTo (1));
		}
	[Test]
	public async Task CatalogHasReadOnlyCollectionsAndExplicitDefaults ()
		{
		_handler.Reply (Catalog);
		var catalog = await _client.GetZoneProfileCatalogAsync ();
		Assert.That (catalog[0].Options[0].IsDefault, Is.True);
		Assert.That (catalog, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<RainPointProfileCategory>> ());
		Assert.That (catalog[0].Options, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<RainPointProfileOption>> ());
		}
	[TestCase ("[{\"id\":1,\"langField\":\"plant\",\"subType\":null}]")]
	[TestCase ("[{\"id\":1,\"langField\":\"plant\",\"subType\":[{\"id\":30,\"langField\":\"a\"},{\"id\":30,\"langField\":\"b\"}]}]")]
	public async Task MalformedCatalogIsRejected (string data)
		{
		_handler.Reply ("{\"code\":0,\"data\":" + data + "}");
		await Assert.ThrowsAsync<RainPointException> (async () => await _client.GetZoneProfileCatalogAsync ());
		}
	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public async Task RecommendationPreservesFractionsAndAddressesSelectedZone (int zone)
		{
		_handler.Reply (Discovery ());
		_handler.Reply ("""{"code":0,"data":[{"day":2.4,"second":301}]}""");
		var result = await _client.GetZoneRecommendationsAsync (Hub (), 2, zone);
		Assert.That (result.Single ().IntervalDays, Is.EqualTo (2.4m));
		Assert.That (result.Single ().Duration, Is.EqualTo (TimeSpan.FromSeconds (301)));
		Assert.That (_handler.Requests.Last ().Path, Is.EqualTo ("/app/device/getPlanValueByDevice?mid=101&sid=42&port=" + zone));
		Assert.That (_handler.Requests.Last ().Method, Is.EqualTo (HttpMethod.Get));
		}
	[TestCase ("[]")]
	[TestCase ("[{}]")]
	public async Task AbsentRecommendationIsNotFabricated (string data)
		{
		_handler.Reply (Discovery ());
		_handler.Reply ("{\"code\":0,\"data\":" + data + "}");
		var result = await _client.GetZoneRecommendationsAsync (Hub (), 2, 1);
		Assert.That (result.All (r => r.IntervalDays is null && r.Duration is null), Is.True);
		}
	[TestCase ("[{\"day\":-1}]")]
	[TestCase ("[{\"second\":-1}]")]
	[TestCase ("[{\"second\":999999999999}]")]
	public async Task InvalidRecommendationIsRejected (string data)
		{
		_handler.Reply (Discovery ());
		_handler.Reply ("{\"code\":0,\"data\":" + data + "}");
		await Assert.ThrowsAsync<RainPointException> (async () => await _client.GetZoneRecommendationsAsync (Hub (), 2, 1));
		}
	private sealed class ProfileRequest
		{
		[JsonPropertyName ("planJson")] public string Payload { get; set; } = string.Empty;
		}
	}
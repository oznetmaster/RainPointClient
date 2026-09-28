using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using NUnit.Framework;
namespace RainPointClient.Tests;

[TestFixture]
public sealed class ProductCatalogTests
	{
	[Test]
	public async Task CatalogPreservesVariantsAndFreezesTypedMetadata ()
		{
		using var handler = new ScriptedHandler ();
		using var http = new HttpClient (handler);
		using var client = new RainPointCloudClient (http);
		handler.Reply ("{\"code\":0,\"data\":{\"token\":\"fixture\",\"tokenExpired\":3600}}");
		await client.LoginAsync ("fixture@example.invalid", "password", "44");
		handler.Reply ("{\"code\":0,\"data\":{\"version\":1788493376838,\"models\":[{\"model\":\"HTV345FRF\",\"modelCode\":37,\"portNumber\":3,\"isMainDevice\":false,\"dp\":[{\"dpId\":46,\"dpCode\":1,\"dpType\":2,\"dpPort\":1,\"dpLen\":2,\"identity\":\"CTL_WATER\"}]},{\"model\":\"HTV345FRF\",\"modelCode\":999}]}}");
		var catalog = await client.GetProductCatalogAsync ();
		Assert.That (catalog.Version, Is.EqualTo (1788493376838));
		Assert.That (catalog.Models, Has.Count.EqualTo (2));
		Assert.That (catalog.Models, Is.InstanceOf<ReadOnlyCollection<RainPointProductModel>> ());
		Assert.That (catalog.Models[0].DataPoints, Is.InstanceOf<ReadOnlyCollection<RainPointDataPointDefinition>> ());
		Assert.That (catalog.Models[0].DataPoints[0].Identity, Is.EqualTo ("CTL_WATER"));
		Assert.That (catalog.Models[1].ModelCode, Is.EqualTo (999));
		Assert.That (catalog.Models[1].PortCount, Is.Null);
		Assert.That (handler.Requests[1].Method, Is.EqualTo (HttpMethod.Get));
		Assert.That (handler.Requests[1].Path, Is.EqualTo ("/app/common/core/productModel"));
		}
	[TestCase ("{}")]
	[TestCase ("{\"models\":null}")]
	[TestCase ("{\"models\":[null]}")]
	[TestCase ("{\"models\":[{\"model\":\"\",\"modelCode\":1}]}")]
	[TestCase ("{\"models\":[{\"model\":\"x\"}]}")]
	[TestCase ("{\"models\":[{\"model\":\"x\",\"modelCode\":1,\"dp\":[null]}]}")]
	[TestCase ("{\"models\":[{\"model\":\"x\",\"modelCode\":1},{\"model\":\"x\",\"modelCode\":1}]}")]
	public async Task MalformedCatalogDoesNotPretendToBeEmpty (string data)
		{
		using var handler = new ScriptedHandler ();
		using var http = new HttpClient (handler);
		using var client = new RainPointCloudClient (http);
		handler.Reply ("{\"code\":0,\"data\":{\"token\":\"fixture\",\"tokenExpired\":3600}}");
		await client.LoginAsync ("fixture@example.invalid", "password", "44");
		handler.Reply ("{\"code\":0,\"data\":" + data + "}");
		Assert.ThrowsAsync<RainPointException> (async () => await client.GetProductCatalogAsync ());
		}
	[TestCase ("{}", null)]
	[TestCase ("{\"recich\":0}", null)]
	[TestCase ("{\"recich\":-1}", null)]
	[TestCase ("{\"recich\":3}", 3)]
	public void RfChannelIsReportedOrUnknown (string json, int? expected)
		{
		var hub = JsonSerializer.Deserialize<RainPointHub> (json.Replace ("{", "{\"mid\":1,\"deviceName\":\"fixture\",\"productKey\":\"fixture\",").Replace (",}", "}"));
		Assert.That (hub!.RfChannel, Is.EqualTo (expected));
		}
	}
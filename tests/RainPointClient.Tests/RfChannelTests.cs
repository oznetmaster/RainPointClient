// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class RfChannelTests
	{
	private ScriptedHandler _handler = null!;
	private HttpClient _http = null!;
	private RainPointCloudClient _client = null!;
	private static RainPointHub Hub (int? channel = 1) => new () { HomeId = 5, Id = 101, Model = "HWG023WBRF", DeviceName = "hub", ProductKey = "product", ReportedRfChannel = channel };
	private void Discovery (int? channel = 1, string field = "") => _handler.Reply (JsonSerializer.Serialize (new
		{
		code = 0,
		data = field == "missing" ? Array.Empty<object> () : new object[] { new
  {
	mid = 101, model = field == "model" ? "HWG023WBRF-V2" : "HWG023WBRF", deviceName = field == "name" ? "replacement" : "hub",
	productKey = field == "key" ? "replacement" : "product", recich = channel, param = "preserve|1|tail", subDevices = new object[0]
  } }
		}));
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
	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public async Task UpdatesOnlyTheChannelAfterFreshRead (int channel)
		{
		int original = channel == 1 ? 2 : 1;
		Discovery (original);
		_handler.Reply ("{\"code\":0}");
		var hub = Hub (original);
		await _client.SetRfChannelAsync (hub, channel);
		Assert.That (_handler.Requests.Last ().Path, Is.EqualTo ("/app/device/main/update"));
		Assert.That (_handler.Requests.Last ().Body, Is.EqualTo ("{\"mid\":101,\"recich\":" + channel + "}"));
		Assert.That (hub.RfChannel, Is.EqualTo (original), "A command acknowledgement must not rewrite observed state.");
		await Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.SetRfChannelAsync (hub, channel));
		Assert.That (_handler.Requests, Has.Count.EqualTo (3));
		}
	[TestCase (-1)]
	[TestCase (0)]
	[TestCase (4)]
	public async Task InvalidChannelNeverContactsCloud (int channel)
		{
		await Assert.ThrowsAsync<ArgumentOutOfRangeException> (async () => await _client.SetRfChannelAsync (Hub (), channel));
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		}
	[TestCase (null)]
	[TestCase (0)]
	[TestCase (4)]
	public async Task UnknownOrUnsupportedOriginalCannotWrite (int? channel)
		{
		await Assert.ThrowsAsync<ArgumentException> (async () => await _client.SetRfChannelAsync (Hub (channel), 2));
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		}
	[TestCase ("missing")]
	[TestCase ("model")]
	[TestCase ("name")]
	[TestCase ("key")]
	[TestCase ("channel")]
	public async Task ChangedHubCannotWrite (string changed)
		{
		Discovery (changed == "channel" ? 3 : 1, changed);
		await Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.SetRfChannelAsync (Hub (), 2));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}
	[Test]
	public async Task SameChannelVerifiesFreshStateWithoutWriting ()
		{
		Discovery ();
		await _client.SetRfChannelAsync (Hub (), 1);
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		Assert.That (_handler.Requests.Last ().Method, Is.EqualTo (HttpMethod.Get));
		}
	[TestCase (false)]
	[TestCase (true)]
	public async Task FailureDoesNotReplayAndConsumesWriteSnapshot (bool rejected)
		{
		Discovery ();
		if (rejected)
			_handler.Reply ("{\"code\":9993}");
		else
			_handler.Steps.Enqueue ((_, _) => throw new HttpRequestException ("fixture"));
		var hub = Hub ();
		try
			{
			await _client.SetRfChannelAsync (hub, 2);
			Assert.Fail ("Expected failure");
			}
		catch (RainPointException) { Assert.That (rejected, Is.True); }
		catch (HttpRequestException) { Assert.That (rejected, Is.False); }
		await Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.SetRfChannelAsync (hub, 2));
		Assert.That (_handler.Requests, Has.Count.EqualTo (3));
		}
	[Test]
	public async Task CancellationBeforeDiscoveryDoesNotConsumeSnapshot ()
		{
		var hub = Hub ();
		await Assert.ThrowsAsync<OperationCanceledException> (async () => await _client.SetRfChannelAsync (hub, 2, new CancellationToken (true)));
		Assert.That (hub.RfChannelWriteAttempted, Is.Zero);
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		}
	}
// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class ClientTests
	{
	private const string LOGIN = "{\"code\":0,\"data\":{\"token\":\"fixture-session\",\"tokenExpired\":3600}}";
	private ScriptedHandler _handler = null!;
	private HttpClient _http = null!;
	private RainPointCloudClient _client = null!;

	[SetUp]
	public void SetUp ()
		{
		_handler = new ScriptedHandler ();
		_http = new HttpClient (_handler, disposeHandler: false);
		_client = new RainPointCloudClient (_http);
		}

	[TearDown]
	public void TearDown ()
		{
		_client.Dispose ();
		_http.Dispose ();
		_handler.Dispose ();
		}

	private Task LoginAsync ()
		{
		_handler.Reply (LOGIN);
		return _client.LoginAsync ("test@example.invalid", "password", "44");
		}

	private static RainPointHub Hub (string model = "HTV345FRF") => new ()
		{
		Id = 101,
		DeviceName = "fixture-hub",
		ProductKey = "fixture-product",
		Model = "HWG023WBRF",
		Devices = new[] { new RainPointDevice { Address = 2, Model = model } }
		};

	[Test]
	public async Task LoginUsesRainPointHeadersAndProtocolHash ()
		{
		await LoginAsync ();
		CapturedRequest request = _handler.Requests.Single ();
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (request.Path, Is.EqualTo ("/auth/basic/app/login"));
			Assert.That (request.AppCode, Is.EqualTo ("2"));
			Assert.That (request.Token, Is.Null);
			}
		using JsonDocument document = JsonDocument.Parse (request.Body!);
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (document.RootElement.GetProperty ("password").GetString (), Is.EqualTo ("5f4dcc3b5aa765d61d8327deb882cf99"));
			Assert.That (document.RootElement.GetProperty ("areaCode").GetString (), Is.EqualTo ("44"));
			Assert.That (document.RootElement.GetProperty ("phoneOrEmail").GetString (), Is.EqualTo ("test@example.invalid"));
			Assert.That (document.RootElement.GetProperty ("deviceId").GetString (), Has.Length.EqualTo (32));
			Assert.That (request.Body, Does.Not.Contain (":\"password\""));
			}
		}

	[Test]
	public async Task DiscoveryHandlesNumericStringsAndIgnoresNewFields ()
		{
		await LoginAsync ();
		_handler.Reply ("""{"code":0,"data":[{"hid":"42","homeName":"Garden","newField":true}]}""");
		_handler.Reply ("""{"code":0,"data":[{"mid":101,"name":"Hub","model":"HWG023WBRF-V2","deviceName":"fixture-hub","productKey":"fixture-product","subDevices":[{"addr":2,"model":"HTV345FRF","modelCode":37,"name":"Three taps"}]}]}""");
		IReadOnlyList<RainPointHome> homes = await _client.GetHomesAsync ();
		IReadOnlyList<RainPointHub> hubs = await _client.GetHubsAsync (homes[0].Id);
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (homes[0].Name, Is.EqualTo ("Garden"));
			Assert.That (hubs[0].Devices[0].SupportedZoneCount, Is.EqualTo (3));
			Assert.That (_handler.Requests.Last ().Path, Is.EqualTo ("/app/device/getDeviceByHid?hid=42"));
			Assert.That (_handler.Requests.Last ().Token, Is.EqualTo ("fixture-session"));
			Assert.That (_http.DefaultRequestHeaders.Contains ("auth"), Is.False);
			}
		}

	[TestCase ("{}")]
	[TestCase ("null")]
	[TestCase ("{\"code\":0}")]
	[TestCase ("{\"code\":0,\"data\":{}}")]
	[TestCase ("{\"code\":0,\"data\":[{}]}")]
	[TestCase ("{\"code\":0,\"data\":[null]}")]
	[TestCase ("<html>fixture-session</html>")]
	public async Task MalformedResponsesAreErrorsNotEmptySuccess (string payload)
		{
		await LoginAsync ();
		_handler.Reply (payload);
		RainPointException? error = await Assert.ThrowsAsync<RainPointException> (async () => await _client.GetHomesAsync ());
		Assert.That (error!.ToString (), Does.Not.Contain ("fixture-session"));
		}

	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public async Task StartSendsZoneAndSecondsWithHubAddressing (int zone)
		{
		await LoginAsync ();
		_handler.Reply ("""{"code":0,"data":{"state":"not a confirmed valve reading"}}""");
		RainPointWateringCommandResult result = await _client.StartWateringAsync (Hub (), 2, zone, TimeSpan.FromSeconds (90));
		CapturedRequest request = _handler.Requests.Last ();
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (request.Path, Is.EqualTo ("/app/device/controlWorkMode"));
			Assert.That (request.Method, Is.EqualTo (HttpMethod.Post));
			}
		using JsonDocument document = JsonDocument.Parse (request.Body!);
		JsonElement body = document.RootElement;
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (body.EnumerateObject ().Select (property => property.Name), Is.EquivalentTo (
					 new[] { "mid", "addr", "deviceName", "productKey", "port", "mode", "duration", "param" }));
			Assert.That (body.GetProperty ("mid").GetInt64 (), Is.EqualTo (101));
			Assert.That (body.GetProperty ("addr").GetInt32 (), Is.EqualTo (2));
			Assert.That (body.GetProperty ("port").GetInt32 (), Is.EqualTo (zone));
			Assert.That (body.GetProperty ("mode").GetInt32 (), Is.EqualTo (1));
			Assert.That (body.GetProperty ("duration").GetInt32 (), Is.EqualTo (90));
			Assert.That (body.GetProperty ("param").GetString (), Is.Empty);
			Assert.That (result.Outcome, Is.EqualTo (RainPointCommandOutcome.Accepted));
			}
		}

	[TestCase ("{\"code\":0,\"data\":\"01#19D800\"}", RainPointCommandOutcome.Accepted)]
	[TestCase ("{\"code\":0}", RainPointCommandOutcome.Accepted)]
	[TestCase ("{\"code\":4}", RainPointCommandOutcome.AlreadyInRequestedStateOrTransitioning)]
	public async Task StopHandlesAcknowledgementShapes (string response, RainPointCommandOutcome expected)
		{
		await LoginAsync ();
		_handler.Reply (response);
		Assert.That ((await _client.StopWateringAsync (Hub (), 2, 3)).Outcome, Is.EqualTo (expected));
		using JsonDocument body = JsonDocument.Parse (_handler.Requests.Last ().Body!);
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (body.RootElement.GetProperty ("duration").GetInt32 (), Is.Zero);
			Assert.That (body.RootElement.GetProperty ("mode").GetInt32 (), Is.Zero);
			}
		}

	[TestCase (0)]
	[TestCase (4)]
	[TestCase (-1)]
	public async Task InvalidZonesDoNotSendRequests (int zone)
		{
		_ = await Assert.ThrowsAsync<ArgumentOutOfRangeException> (async () => await _client.StopWateringAsync (Hub (), 2, zone));
		Assert.That (_handler.Requests, Is.Empty);
		}

	[TestCase (0)]
	[TestCase (-1)]
	[TestCase (0.5)]
	[TestCase (59)]
	[TestCase (60.5)]
	[TestCase (43201)]
	public async Task InvalidDurationsDoNotSendRequests (double seconds)
		{
		_ = await Assert.ThrowsAsync<ArgumentOutOfRangeException> (async () =>
			 await _client.StartWateringAsync (Hub (), 2, 1, TimeSpan.FromSeconds (seconds)));
		Assert.That (_handler.Requests, Is.Empty);
		}

	[Test]
	public async Task UnsupportedModelsDoNotReceiveGuessedCommands ()
		{
		_ = await Assert.ThrowsAsync<NotSupportedException> (async () => await _client.StopWateringAsync (Hub ("HIC801W"), 2, 1));
		Assert.That (_handler.Requests, Is.Empty);
		}

	[Test]
	public async Task AddressMustBelongToTheGivenHub ()
		{
		_ = await Assert.ThrowsAsync<ArgumentException> (async () => await _client.StopWateringAsync (Hub (), 3, 1));
		Assert.That (_handler.Requests, Is.Empty);
		}

	[TestCase (1001)]
	[TestCase (1004)]
	public async Task RejectedSessionsAreInvalidatedWithoutRetryingCommands (int code)
		{
		await LoginAsync ();
		_handler.Reply ("{\"code\":" + code + ",\"msg\":\"private server content\"}");
		RainPointException? error = await Assert.ThrowsAsync<RainPointException> (async () => await _client.StopWateringAsync (Hub (), 2, 1));
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (error!.ApiCode, Is.EqualTo (code));
			Assert.That (error.Message, Does.Not.Contain ("private server content"));
			}
		_ = await Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.GetHomesAsync ());
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[Test]
	public async Task HttpFailureDoesNotReplayStart ()
		{
		await LoginAsync ();
		_handler.Reply ("private server text", HttpStatusCode.ServiceUnavailable);
		RainPointException? error = await Assert.ThrowsAsync<RainPointException> (async () =>
			 await _client.StartWateringAsync (Hub (), 2, 1, TimeSpan.FromSeconds (60)));
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (error!.HttpStatus, Is.EqualTo (HttpStatusCode.ServiceUnavailable));
			Assert.That (_handler.Requests, Has.Count.EqualTo (2));
			}
		}

	[TestCase ("{\"code\":0,\"data\":{\"token\":\"x\",\"tokenExpired\":0}}")]
	[TestCase ("{\"code\":0,\"data\":{\"tokenExpired\":3600}}")]
	[TestCase ("{\"code\":0,\"data\":{\"token\":\"x\",\"tokenExpired\":9223372036854775807}}")]
	public async Task InvalidLoginSessionIsNotAccepted (string body)
		{
		_handler.Reply (body);
		_ = await Assert.ThrowsAsync<RainPointException> (async () => await _client.LoginAsync ("test@example.invalid", "password", "44"));
		_ = await Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.GetHomesAsync ());
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		}

	[Test]
	public async Task FailedAccountSwitchDoesNotRetainPreviousAccount ()
		{
		await LoginAsync ();
		_handler.Reply ("{\"code\":9999}");
		_ = await Assert.ThrowsAsync<RainPointException> (async () => await _client.LoginAsync ("other@example.invalid", "incorrect", "44"));
		_ = await Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.GetHomesAsync ());
		}

	[Test]
	public async Task DelayedRejectionCannotInvalidateNewSession ()
		{
		await LoginAsync ();
		TaskCompletionSource<HttpResponseMessage> delayed = new (TaskCreationOptions.RunContinuationsAsynchronously);
		_handler.Steps.Enqueue ((_, _) => delayed.Task);
		Task<IReadOnlyList<RainPointHome>> oldRequest = _client.GetHomesAsync ();
		_handler.Reply ("{\"code\":0,\"data\":{\"token\":\"new-session\",\"tokenExpired\":3600}}");
		await _client.LoginAsync ("test@example.invalid", "password", "44");
		delayed.SetResult (new HttpResponseMessage (HttpStatusCode.OK)
			{
			Content = new StringContent ("{\"code\":1001}")
			});
		_ = await Assert.ThrowsAsync<RainPointException> (async () => await oldRequest);
		_handler.Reply ("{\"code\":0,\"data\":[]}");
		_ = await _client.GetHomesAsync ();
		Assert.That (_handler.Requests.Last ().Token, Is.EqualTo ("new-session"));
		}

	[Test]
	public async Task HttpUnauthorizedInvalidatesSession ()
		{
		await LoginAsync ();
		_handler.Reply ("{}", HttpStatusCode.Unauthorized);
		_ = await Assert.ThrowsAsync<RainPointException> (async () => await _client.GetHomesAsync ());
		_ = await Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.GetHomesAsync ());
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[Test]
	public async Task CodeFourIsNotASuccessForReads ()
		{
		await LoginAsync ();
		_handler.Reply ("{\"code\":4}");
		_ = await Assert.ThrowsAsync<RainPointException> (async () => await _client.GetHomesAsync ());
		}

	[Test]
	public async Task AmbiguousDeviceAddressesAreRejected ()
		{
		await LoginAsync ();
		_handler.Reply ("""{"code":0,"data":[{"mid":101,"deviceName":"fixture-hub","productKey":"fixture-product","subDevices":[{"addr":2,"model":"HTV345FRF"},{"addr":2,"model":"HTV345FRF"}]}]}""");
		_ = await Assert.ThrowsAsync<RainPointException> (async () => await _client.GetHubsAsync (1));
		}

	[TestCase (403, "{}")]
	[TestCase (429, "{}")]
	[TestCase (200, "{\"code\":9993}")]
	public async Task LoginThrottlesBlockImmediateAdditionalLogin (int status, string body)
		{
		_handler.Reply (body, (HttpStatusCode)status);
		RainPointException? first = await Assert.ThrowsAsync<RainPointException> (async () =>
			 await _client.LoginAsync ("test@example.invalid", "password", "44"));
		Assert.That (first!.RetryAfter, Is.GreaterThanOrEqualTo (TimeSpan.FromSeconds (120)));
		_ = await Assert.ThrowsAsync<RainPointException> (async () =>
			 await _client.LoginAsync ("test@example.invalid", "password", "44"));
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		}

	[Test]
	public async Task StatusSelectsRfAddressAndKeepsMissingThirdZoneUnknown ()
		{
		await LoginAsync ();
		_handler.Reply ("""{"code":0,"data":[{"mid":101,"status":[{"id":"D01","value":"11#19D800"},{"id":"D02","value":"11#17E1D70018DC0219D8011AD80025AD5802","time":1785420002247}]}]}""");
		RainPointTimerStatus result = await _client.GetTimerStatusAsync (Hub (), 2);
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (result.Zones[0].IsOpen, Is.True);
			Assert.That (result.Zones[1].IsOpen, Is.False);
			Assert.That (result.Zones[2].IsOpen, Is.Null);
			Assert.That (result.SignalStrengthDbm, Is.EqualTo (-41));
			Assert.That (result.BatteryConditionCode, Is.EqualTo (2));
			Assert.That (result.LastDataChange, Is.EqualTo (DateTimeOffset.FromUnixTimeMilliseconds (1785420002247)));
			}
		}

	[Test]
	public async Task AbsentTimerIsNotReportedRatherThanClosed ()
		{
		await LoginAsync ();
		_handler.Reply ("""{"code":0,"data":[{"mid":101,"status":[]}]}""");
		RainPointTimerStatus result = await _client.GetTimerStatusAsync (Hub (), 2);
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (result.Availability, Is.EqualTo (TimerReadingAvailability.NotReported));
			Assert.That (result.Zones, Is.Empty);
			}
		}

	[Test]
	public async Task DuplicateTimerStatusIsRejected ()
		{
		await LoginAsync ();
		_handler.Reply ("""{"code":0,"data":[{"mid":101,"status":[{"id":"D02"},{"id":"D02"}]}]}""");
		_ = await Assert.ThrowsAsync<RainPointException> (async () => await _client.GetTimerStatusAsync (Hub (), 2));
		}

	[Test]
	public async Task StatusUsesTypedBatchAddressingAndSelectsRequestedHub ()
		{
		await LoginAsync ();
		_handler.Reply ("""{"code":0,"data":[{"mid":999,"status":[{"id":"D02","value":"11#19D801"}]},{"mid":101,"status":[{"id":"D02","value":"11#19D800"}]}]}""");
		RainPointTimerStatus result = await _client.GetTimerStatusAsync (Hub (), 2);
		CapturedRequest request = _handler.Requests.Last ();
		Assert.That (request.Path, Is.EqualTo ("/app/device/multipleDeviceStatus"));
		Assert.That (request.Method, Is.EqualTo (HttpMethod.Post));
		using JsonDocument document = JsonDocument.Parse (request.Body!);
		JsonElement device = document.RootElement.GetProperty ("devices")[0];
		Assert.That (device.EnumerateObject ().Select (property => property.Name), Is.EquivalentTo (new[] { "mid", "deviceName", "productKey" }));
		Assert.That (device.GetProperty ("mid").GetInt64 (), Is.EqualTo (101));
		Assert.That (device.GetProperty ("deviceName").GetString (), Is.EqualTo ("fixture-hub"));
		Assert.That (device.GetProperty ("productKey").GetString (), Is.EqualTo ("fixture-product"));
		Assert.That (result.Zones[0].IsOpen, Is.False);
		}

	[TestCase ("{\"code\":0,\"data\":[]}")]
	[TestCase ("{\"code\":0,\"data\":[{\"mid\":999,\"status\":[]}]}")]
	public async Task MissingHubDoesNotInventTimerState (string response)
		{
		await LoginAsync ();
		_handler.Reply (response);
		RainPointTimerStatus status = await _client.GetTimerStatusAsync (Hub (), 2);
		Assert.That (status.Availability, Is.EqualTo (TimerReadingAvailability.NotReported));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[TestCase ("{\"code\":0,\"data\":[{\"mid\":101,\"status\":[]},{\"mid\":101,\"status\":[]}]}")]
	[TestCase ("{\"code\":0,\"data\":[{\"status\":[]}]}")]
	[TestCase ("{\"code\":0,\"data\":[{\"mid\":101}]}")]
	[TestCase ("{\"code\":0,\"data\":[{\"mid\":101,\"status\":null}]}")]
	[TestCase ("{\"code\":0,\"data\":[null]}")]
	public async Task InvalidBatchHubRecordsAreRejected (string response)
		{
		await LoginAsync ();
		_handler.Reply (response);
		_ = await Assert.ThrowsAsync<RainPointException> (async () => await _client.GetTimerStatusAsync (Hub (), 2));
		}

	[TestCase (60)]
	[TestCase (43200)]
	public async Task DocumentedNormalIrrigationDurationBoundsAreAccepted (int seconds)
		{
		await LoginAsync ();
		_handler.Reply ("{\"code\":0}");
		Assert.That ((await _client.StartWateringAsync (Hub (), 2, 1, TimeSpan.FromSeconds (seconds))).Outcome, Is.EqualTo (RainPointCommandOutcome.Accepted));
		}

	[Test]
	public async Task CancellationReachesTransport ()
		{
		await LoginAsync ();
		_handler.Steps.Enqueue (async (_, token) =>
		{
			await Task.Delay (Timeout.Infinite, token);
			throw new InvalidOperationException ("Unreachable");
		});
		using CancellationTokenSource cancellation = new ();
		Task<IReadOnlyList<RainPointHome>> request = _client.GetHomesAsync (cancellation.Token);
		cancellation.Cancel ();
		Assert.That (async () => await request, Throws.InstanceOf<OperationCanceledException> ());
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[Test]
	public async Task DisposalDoesNotDisposeBorrowedHttpClient ()
		{
		_client.Dispose ();
		_ = await Assert.ThrowsAsync<ObjectDisposedException> (async () => await _client.GetHomesAsync ());
		_handler.Reply ("{}");
		using HttpResponseMessage result = await _http.GetAsync ("https://example.invalid/");
		Assert.That (result.IsSuccessStatusCode, Is.True);
		}

	[Test]
	public async Task RequestsRequireExplicitLogin ()
		{
		_ = await Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.GetHomesAsync ());
		Assert.That (_handler.Requests, Is.Empty);
		}

	[TestCase ("http://example.invalid/")]
	[TestCase ("https://user:password@example.invalid/")]
	[TestCase ("https://example.invalid/path")]
	[TestCase ("https://example.invalid/?q=1")]
	public void ServiceAddressMustBeSecureOrigin (string address) => _ = Assert.Throws<ArgumentException> (() => new RainPointCloudClient (_http, new Uri (address)));
	}

internal sealed class ScriptedHandler : HttpMessageHandler
	{
	internal Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> Steps { get; } = new ();
	internal List<CapturedRequest> Requests { get; } = [];

	internal void Reply (string json, HttpStatusCode status = HttpStatusCode.OK) =>
		 Steps.Enqueue ((_, _) => Task.FromResult (new HttpResponseMessage (status)
			 {
			 Content = new StringContent (json, Encoding.UTF8, "application/json")
			 }));

	protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
		{
		cancellationToken.ThrowIfCancellationRequested ();
		CapturedRequest captured = new ()
			{
			Path = request.RequestUri!.PathAndQuery,
			Method = request.Method,
			Token = request.Headers.TryGetValues ("auth", out IEnumerable<string>? auth) ? auth.Single () : null,
			AppCode = request.Headers.TryGetValues ("appCode", out IEnumerable<string>? app) ? app.Single () : null
			};
		Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> step;
		// Reserve the response before body capture can yield and let another request overtake it.
		lock (Steps)
			{
			if (Steps.Count == 0)
				throw new InvalidOperationException ("Unexpected network request in offline test.");
			step = Steps.Dequeue ();
			Requests.Add (captured);
			}
		captured.Body = request.Content is null ? null : await request.Content.ReadAsStringAsync ();
		return await step (request, cancellationToken);
		}
	}

internal sealed class CapturedRequest
	{
	internal string Path { get; set; } = string.Empty;
	internal HttpMethod Method { get; set; } = HttpMethod.Get;
	internal string? Token
		{
		get; set;
		}
	internal string? AppCode
		{
		get; set;
		}
	internal string? Body
		{
		get; set;
		}
	}
// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class SessionManagementTests
	{
	private ScriptedHandler _handler = null!;
	private HttpClient _http = null!;
	private RainPointCloudClient _client = null!;

	[SetUp]
	public async Task SetUp ()
		{
		_handler = new ScriptedHandler ();
		_http = new HttpClient (_handler, false);
		_client = new RainPointCloudClient (_http);
		_handler.Reply ("""{"code":0,"data":{"token":"fixture-session","refreshToken":"refresh-one","tokenExpired":3600}}""");
		await _client.LoginAsync ("test@example.invalid", "password", "44");
		}

	[TearDown]
	public void TearDown ()
		{
		_client.Dispose ();
		_http.Dispose ();
		_handler.Dispose ();
		}

	[Test]
	public async Task RefreshRotatesTokensAndUsesServerExpiry ()
		{
		DateTimeOffset before = DateTimeOffset.UtcNow;
		_handler.Reply ("""{"code":0,"data":{"token":"fresh-session","refreshToken":"refresh-two","tokenExpired":1200}}""");
		await _client.RefreshSessionAsync ();
		CapturedRequest refresh = _handler.Requests.Last ();
		Assert.That (refresh.Path, Is.EqualTo ("/auth/basic/app/token/refresh"));
		Assert.That (refresh.Token, Is.Null);
		using JsonDocument body = JsonDocument.Parse (refresh.Body!);
		Assert.That (body.RootElement.GetProperty ("refreshToken").GetString (), Is.EqualTo ("refresh-one"));
		Assert.That (body.RootElement.EnumerateObject ().Count (), Is.EqualTo (1));
		Assert.That (_client.SessionExpiresAt, Is.InRange (before.AddSeconds (1139), DateTimeOffset.UtcNow.AddSeconds (1141)));
		_handler.Reply ("""{"code":0,"data":[]}""");
		await _client.GetHomesAsync ();
		Assert.That (_handler.Requests.Last ().Token, Is.EqualTo ("fresh-session"));
		_handler.Reply ("""{"code":0,"data":{"token":"new-session","tokenExpired":3600}}""");
		await _client.RefreshSessionAsync ();
		Assert.That (_handler.Requests.Last ().Body, Does.Contain ("refresh-two"));
		Assert.That (_client.CanRefreshSession, Is.True);
		}

	[TestCase ("{\"token\":\"fresh-session\"}")]
	[TestCase ("{\"token\":\"fresh-session\",\"tokenExpired\":0}")]
	[TestCase ("{\"token\":\"\",\"tokenExpired\":3600}")]
	public void MalformedRefreshDoesNotGuessAnExpiry (string data)
		{
		DateTimeOffset? expiry = _client.SessionExpiresAt;
		_handler.Reply ("{\"code\":0,\"data\":" + data + "}");
		Assert.ThrowsAsync<RainPointException> (async () => await _client.RefreshSessionAsync ());
		Assert.That (_client.SessionExpiresAt, Is.EqualTo (expiry));
		}

	[Test]
	public void RejectedRefreshInvalidatesTheSessionWithoutLoggingIn ()
		{
		_handler.Reply ("""{"code":1004}""");
		Assert.ThrowsAsync<RainPointException> (async () => await _client.RefreshSessionAsync ());
		Assert.That (_client.HasValidSession, Is.False);
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}

	[TestCase (0)]
	[TestCase (1004)]
	public async Task LogoutClearsLocalSessionEvenWhenRejected (int code)
		{
		_handler.Reply ("{\"code\":" + code + "}");
		if (code == 0)
			{
			await _client.LogoutAsync ();
			}
		else
			{
			Assert.ThrowsAsync<RainPointException> (async () => await _client.LogoutAsync ());
			}
		Assert.That (_handler.Requests.Last ().Path, Is.EqualTo ("/auth/basic/app/logOut"));
		Assert.That (_client.HasValidSession, Is.False);
		Assert.That (_client.CanRefreshSession, Is.False);
		await _client.LogoutAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}
	}
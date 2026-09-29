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
public sealed class AccountAdministrationTests
	{
	private ScriptedHandler _handler = null!; private HttpClient _http = null!; private RainPointCloudClient _client = null!;
	private const string Login = """{"code":0,"data":{"token":"fixture","tokenExpired":3600,"refreshToken":"refresh","user":{"uid":"10","email":"fixture@example.invalid","nickname":"Fixture","photo":"https://fixture.invalid/image.png","lang":"en"}}}""";
	[SetUp]
	public void Setup ()
		{
		_handler = new ();
		_http = new (_handler, false);
		_client = new (_http);
		}
	[TearDown]
	public void Cleanup ()
		{
		_client.Dispose ();
		_http.Dispose ();
		_handler.Dispose ();
		}
	private async Task SignIn ()
		{
		_handler.Reply (Login);
		await _client.LoginAsync ("fixture@example.invalid", "fixture", "44");
		}
	private async Task<RainPointVerifiedEmail> Verify (RainPointEmailVerificationPurpose purpose)
		{
		_handler.Reply ("{\"code\":0}");
		return await _client.VerifyEmailCodeAsync ("fixture@example.invalid", "44", purpose, "123456");
		}
	[TestCase (RainPointEmailVerificationPurpose.Registration)]
	[TestCase (RainPointEmailVerificationPurpose.PasswordReset)]
	public async Task CodeRequestsAndVerificationAreExplicitAndAnonymous (RainPointEmailVerificationPurpose purpose)
		{
		_handler.Reply ("{\"code\":0}");
		await _client.SendEmailVerificationCodeAsync ("fixture@example.invalid", "44", purpose);
		var verified = await Verify (purpose);
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		Assert.That (_handler.Requests.All (r => r.Token is null), Is.True);
		using var body = JsonDocument.Parse (_handler.Requests.Last ().Body!);
		Assert.That (body.RootElement.GetProperty ("type").GetInt32 (), Is.EqualTo ((int)purpose));
		Assert.That (body.RootElement.GetProperty ("appCode").GetString (), Is.EqualTo ("2"));
		Assert.That (verified.Purpose, Is.EqualTo (purpose));
		Assert.That (_client.HasValidSession, Is.False);
		}
	[Test]
	public async Task RegistrationUsesVerifiedEmailHashedPasswordAndNoImplicitLogin ()
		{
		var verified = await Verify (RainPointEmailVerificationPurpose.Registration);
		_handler.Reply ("{\"code\":0}");
		await _client.RegisterEmailAsync (verified, "newpass", "GB", "fixture-device", "reviewed-version");
		var request = _handler.Requests.Last ();
		using var body = JsonDocument.Parse (request.Body!);
		Assert.That (request.Path, Is.EqualTo ("/app/common/core/account/email/register"));
		Assert.That (body.RootElement.GetProperty ("password").GetString (), Is.EqualTo ("e6053eb8d35e02ae40beeeacef203c1a"));
		Assert.That (body.RootElement.GetProperty ("agreementVer").GetString (), Is.EqualTo ("reviewed-version"));
		Assert.That (body.RootElement.GetProperty ("isocode").GetString (), Is.EqualTo ("GB"));
		Assert.That (_client.HasValidSession, Is.False);
		await Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.RegisterEmailAsync (verified, "newpass", "GB", "fixture-device"));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}
	[TestCase (true)]
	[TestCase (false)]
	public async Task ResetAttemptConsumesVerificationEvenOnCloudFailure (bool succeeds)
		{
		var verified = await Verify (RainPointEmailVerificationPurpose.PasswordReset);
		_handler.Reply (succeeds ? "{\"code\":0}" : "{\"code\":42}");
		if (succeeds)
			await _client.ResetPasswordByEmailAsync (verified, "newpass");
		else
			await Assert.ThrowsAsync<RainPointException> (async () => await _client.ResetPasswordByEmailAsync (verified, "newpass"));
		using var body = JsonDocument.Parse (_handler.Requests.Last ().Body!);
		Assert.That (body.RootElement.GetProperty ("type").GetInt32 (), Is.EqualTo (1));
		Assert.That (body.RootElement.GetProperty ("password").GetString (), Is.EqualTo ("e6053eb8d35e02ae40beeeacef203c1a"));
		await Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.ResetPasswordByEmailAsync (verified, "newpass"));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}
	[Test]
	public async Task ManualRegistrationInstructionsNeverSendAnEmail ()
		{
		_handler.Reply ("""{"code":0,"data":{"email":"verification@example.invalid","content":"fixture-challenge"}}""");
		var instructions = await _client.GetRegistrationEmailAsync ("fixture@example.invalid", "44");
		Assert.That (instructions.Destination, Is.EqualTo ("verification@example.invalid"));
		Assert.That (instructions.Content, Is.EqualTo ("fixture-challenge"));
		_handler.Reply ("{\"code\":0}");
		var verified = await _client.VerifyRegistrationEmailAsync (instructions);
		Assert.That (_handler.Requests.Last ().Path, Is.EqualTo ("/app/common/core/email/verify/manual/captcha"));
		Assert.That (_handler.Requests.All (r => !r.Path.Contains ("send/code")), Is.True);
		_handler.Reply ("{\"code\":0}");
		await _client.RegisterEmailAsync (verified, "newpass", "GB", "fixture-device");
		using var body = JsonDocument.Parse (_handler.Requests.Last ().Body!);
		Assert.That (body.RootElement.GetProperty ("code").GetString (), Is.EqualTo ("fixture-challenge"));
		}
	[Test]
	public async Task WrongPurposeOrOtherClientCannotUseVerification ()
		{
		var verified = await Verify (RainPointEmailVerificationPurpose.Registration);
		await Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.ResetPasswordByEmailAsync (verified, "newpass"));
		using var other = new RainPointCloudClient (_http);
		await Assert.ThrowsAsync<InvalidOperationException> (async () => await other.RegisterEmailAsync (verified, "newpass", "GB", "fixture"));
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		}
	[Test]
	public async Task SignedInAccountCannotRunAnonymousRecoveryFlow ()
		{
		await SignIn ();
		await Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.SendEmailVerificationCodeAsync ("other@example.invalid", "44", RainPointEmailVerificationPurpose.PasswordReset));
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		Assert.That (_client.HasValidSession, Is.True);
		}
	[TestCase (true)]
	[TestCase (false)]
	public async Task PasswordChangeHashesBothValuesAndClearsSessionAfterAttempt (bool succeeds)
		{
		await SignIn ();
		_handler.Reply (succeeds ? "{\"code\":0}" : "{\"code\":42}");
		if (succeeds)
			await _client.ChangePasswordAsync ("oldpass", "newpass");
		else
			await Assert.ThrowsAsync<RainPointException> (async () => await _client.ChangePasswordAsync ("oldpass", "newpass"));
		using var body = JsonDocument.Parse (_handler.Requests.Last ().Body!);
		Assert.That (body.RootElement.GetProperty ("password").GetString (), Is.EqualTo ("65596aece8ead4b14c78d52f2b88ec37"));
		Assert.That (body.RootElement.GetProperty ("newPassword").GetString (), Is.EqualTo ("e6053eb8d35e02ae40beeeacef203c1a"));
		Assert.That (_client.HasValidSession, Is.False);
		Assert.That (_client.AccountProfile, Is.Null);
		}
	[Test]
	public async Task CancelledPasswordChangeBeforeSubmissionPreservesSession ()
		{
		await SignIn ();
		using var cancel = new CancellationTokenSource ();
		cancel.Cancel ();
		await Assert.CatchAsync<OperationCanceledException> (async () => await _client.ChangePasswordAsync ("oldpass", "newpass", cancel.Token));
		Assert.That (_client.HasValidSession, Is.True);
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		}
	[TestCase ("")]
	[TestCase ("short")]
	[TestCase ("123456789012345678901")]
	public async Task PasswordBoundsAreCheckedBeforeRequests (string password)
		{
		await Assert.ThrowsAsync<ArgumentException> (async () => await _client.ChangePasswordAsync (password, "newpass"));
		Assert.That (_handler.Requests, Is.Empty);
		}
	[TestCase ("bad")]
	[TestCase ("Name <fixture@example.invalid>")]
	[TestCase ("fixture@example.invalid\r\nBcc:someone@example.invalid")]
	public async Task NonCanonicalEmailAddressesAreRejected (string email)
		{
		await Assert.ThrowsAsync<ArgumentException> (async () => await _client.SendEmailVerificationCodeAsync (email, "44", RainPointEmailVerificationPurpose.Registration));
		Assert.That (_handler.Requests, Is.Empty);
		}
	[Test]
	public async Task ProfileUpdateIsMinimalAndOneAttempt ()
		{
		await SignIn ();
		var profile = _client.AccountProfile!;
		Assert.That (profile.Id, Is.EqualTo (10));
		Assert.That (profile.Nickname, Is.EqualTo ("Fixture"));
		_handler.Reply ("{\"code\":0}");
		await _client.SetAccountNicknameAsync (profile, "New fixture");
		Assert.That (_handler.Requests.Last ().Body, Is.EqualTo ("{\"nickname\":\"New fixture\"}"));
		Assert.That (_client.AccountProfile, Is.Null);
		await Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.SetAccountPhotoAsync (profile, new Uri ("https://fixture.invalid/another.png")));
		}
	[Test]
	public async Task OldProfileCannotCrossAccountSignIn ()
		{
		await SignIn ();
		var profile = _client.AccountProfile!;
		await SignIn ();
		await Assert.ThrowsAsync<InvalidOperationException> (async () => await _client.SetAccountNicknameAsync (profile, "No"));
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		}
	[Test]
	public async Task ProfileObservationSurvivesTokenRefresh ()
		{
		await SignIn ();
		var profile = _client.AccountProfile!;
		_handler.Reply ("""{"code":0,"data":{"token":"next","tokenExpired":3600}}""");
		await _client.RefreshSessionAsync ();
		Assert.That (_client.AccountProfile, Is.SameAs (profile));
		}
	}
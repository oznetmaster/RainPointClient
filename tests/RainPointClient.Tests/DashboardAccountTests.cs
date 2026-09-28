using System;
using System.Net.Http;
using System.Threading.Tasks;

using NUnit.Framework;

using RainPointClient.Desktop.Core;
namespace RainPointClient.Tests;

[TestFixture]
public sealed class DashboardAccountTests
	{
	private ScriptedHandler _handler = null!; private HttpClient _http = null!; private Dashboard _dashboard = null!;
	[SetUp]
	public void Setup ()
		{
		_handler = new ();
		_http = new (_handler, false);
		_dashboard = new (new RainPointCloudClient (_http));
		_dashboard.AccountEmail = "fixture@example.invalid";
		}
	[TearDown]
	public async Task Cleanup ()
		{
		await _dashboard.CloseAsync ();
		_http.Dispose ();
		_handler.Dispose ();
		}
	private async Task Verify ()
		{
		_handler.Reply ("{\"code\":0}");
		await _dashboard.VerifyAccountCodeAsync ("123456");
		Assert.That (_dashboard.CanCompleteAccount, Is.True);
		}
	private async Task Login ()
		{
		_handler.Reply ("""{"code":0,"data":{"token":"fixture","tokenExpired":3600,"user":{"uid":"10","nickname":"Fixture"}}}""");
		_handler.Reply ("{\"code\":0,\"data\":[]}");
		await _dashboard.ConnectAsync ("fixture@example.invalid", "fixture", "44");
		}
	[TestCase (0)]
	[TestCase (1)]
	[TestCase (2)]
	public async Task ChangingVerificationIdentityInvalidatesProof (int field)
		{
		await Verify ();
		if (field == 0)
			_dashboard.AccountEmail = "other@example.invalid";
		else if (field == 1)
			_dashboard.AccountAreaCode = "1";
		else
			_dashboard.AccountPurpose = RainPointEmailVerificationPurpose.PasswordReset;
		Assert.That (_dashboard.CanCompleteAccount, Is.False);
		await _dashboard.CompleteAccountAsync ("newpass", "GB", true);
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		}
	[Test]
	public async Task RegistrationRequiresAgreementAndDoesNotAutomaticallySignIn ()
		{
		await Verify ();
		await _dashboard.CompleteAccountAsync ("newpass", "GB", false);
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		_handler.Reply ("{\"code\":0}");
		await _dashboard.CompleteAccountAsync ("newpass", "GB", true);
		Assert.That (_handler.Requests, Has.Count.EqualTo (2));
		Assert.That (_dashboard.CanCompleteAccount, Is.False);
		Assert.That (_dashboard.CanChangeAccount, Is.False);
		}
	[Test]
	public async Task ResetDoesNotProceedIfSavedCredentialsCannotBeForgotten ()
		{
		_dashboard.AccountPurpose = RainPointEmailVerificationPurpose.PasswordReset;
		await Verify ();
		await _dashboard.CompleteAccountAsync ("newpass", "GB", false, () => throw new System.IO.IOException ());
		Assert.That (_handler.Requests, Has.Count.EqualTo (1));
		Assert.That (_dashboard.CanCompleteAccount, Is.False);
		}
	[TestCase (true)]
	[TestCase (false)]
	public async Task PasswordChangeClearsAccountAndNeverReplays (bool success)
		{
		await Login ();
		bool prepared = false;
		_handler.Reply (success ? "{\"code\":0}" : "{\"code\":42}");
		await _dashboard.ChangeAccountPasswordAsync ("fixture", "newpass", () => prepared = true);
		Assert.That (prepared, Is.True);
		Assert.That (_dashboard.CanChangeAccount, Is.False);
		Assert.That (_dashboard.Homes, Is.Empty);
		Assert.That (_handler.Requests, Has.Count.EqualTo (3));
		}
	[Test]
	public async Task ProfileWriteInvalidatesObservation ()
		{
		await Login ();
		Assert.That (_dashboard.AccountProfile!.Nickname, Is.EqualTo ("Fixture"));
		_handler.Reply ("{\"code\":0}");
		await _dashboard.SetAccountNicknameAsync ("Updated");
		Assert.That (_dashboard.AccountProfile, Is.Null);
		Assert.That (_dashboard.CanEditAccountProfile, Is.False);
		}
	}
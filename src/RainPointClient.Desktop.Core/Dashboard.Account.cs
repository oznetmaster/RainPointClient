// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
namespace RainPointClient.Desktop.Core;

public sealed partial class Dashboard
	{
	private RainPointRegistrationEmail? _registrationEmail;
	private RainPointVerifiedEmail? _verifiedEmail;
	private string _accountEmail = "", _accountArea = "44";
	private RainPointEmailVerificationPurpose _accountPurpose;
	private readonly string _registrationDeviceId = Guid.NewGuid ().ToString ("N");
	public string AccountEmail
		{
		get => _accountEmail; set
			{
			if (!CanEdit)
				return;
			_accountEmail = value;
			ClearAccountVerification ();
			Changed ();
			}
		}
	public string AccountAreaCode
		{
		get => _accountArea; set
			{
			if (!CanEdit)
				return;
			_accountArea = value;
			ClearAccountVerification ();
			Changed ();
			}
		}
	public RainPointEmailVerificationPurpose AccountPurpose
		{
		get => _accountPurpose; set
			{
			if (!CanEdit)
				return;
			_accountPurpose = value;
			ClearAccountVerification ();
			Changed ();
			}
		}
	public Array AccountPurposes => Enum.GetValues (typeof (RainPointEmailVerificationPurpose));
	public bool CanVerifyAccount => CanEdit && !_client.HasValidSession && !IsRecoveringSession && Homes.Count == 0;
	public bool CanCompleteAccount => CanVerifyAccount && _verifiedEmail is not null;
	public bool CanVerifyRegistrationEmail => CanVerifyAccount && _registrationEmail is not null;
	public bool CanChangeAccount => CanEdit && _client.HasValidSession;
	public bool CanEditAccountProfile => CanChangeAccount && _client.AccountProfile is not null;
	public RainPointAccountProfile? AccountProfile => _client.AccountProfile;
	public string AccountInstructions { get; private set; } = "Choose registration or password reset, then verify the email.";
	private void ClearAccountVerification ()
		{
		_registrationEmail = null;
		_verifiedEmail = null;
		AccountInstructions = "Verify this email before an account write.";
		}
	public Task SendAccountCodeAsync () => RunAsync (async token =>
	{
		ClearAccountVerification ();
		await _client.SendEmailVerificationCodeAsync (AccountEmail.Trim (), AccountAreaCode.Trim (), AccountPurpose, token);
		AccountInstructions = "Verification code requested. Check your email.";
	}, "Could not request a verification code. Sign out first and check the email address.");
	public Task VerifyAccountCodeAsync (string code) => RunAsync (async token =>
	{
		_verifiedEmail = null;
		_verifiedEmail = await _client.VerifyEmailCodeAsync (AccountEmail.Trim (), AccountAreaCode.Trim (), AccountPurpose, code.Trim (), token);
		AccountInstructions = "Email verified for one account operation.";
	}, "Email verification failed. Check the code and try explicitly again.");
	public Task GetAccountRegistrationEmailAsync () => RunAsync (async token =>
	{
		ClearAccountVerification ();
		if (AccountPurpose != RainPointEmailVerificationPurpose.Registration)
			throw new InvalidOperationException ();
		_registrationEmail = await _client.GetRegistrationEmailAsync (AccountEmail.Trim (), AccountAreaCode.Trim (), token);
		AccountInstructions = "Send an email from " + _registrationEmail.Email + " to " + _registrationEmail.Destination + " with this content: " + _registrationEmail.Content + ". Then choose Check sent email.";
	}, "Could not obtain registration email instructions.");
	public Task CheckAccountRegistrationEmailAsync () => RunAsync (async token =>
	{
		_verifiedEmail = null;
		_verifiedEmail = await _client.VerifyRegistrationEmailAsync (_registrationEmail ?? throw new InvalidOperationException (), token);
		AccountInstructions = "Registration email verified for one registration attempt.";
	}, "Registration email is not verified. Follow the email instructions before checking again.");
	public Task CompleteAccountAsync (string password, string country, bool agreementAccepted, Action? beforeCredentialChange = null) => RunAsync (async token =>
	{
		var verified = _verifiedEmail ?? throw new InvalidOperationException ();
		if (verified.Purpose == RainPointEmailVerificationPurpose.Registration && !agreementAccepted)
			throw new InvalidOperationException ();
		_verifiedEmail = null;
		if (verified.Purpose == RainPointEmailVerificationPurpose.Registration)
			await _client.RegisterEmailAsync (verified, password, country.Trim (), _registrationDeviceId, cancellationToken: token);
		else
			{
			beforeCredentialChange?.Invoke ();
			await _client.ResetPasswordByEmailAsync (verified, password, token);
			}
		ClearAccountVerification ();
		Message = "Account operation accepted. Sign in explicitly with the new credentials.";
	}, "Account operation failed or its outcome is unknown. It was not retried. Verify again before another attempt.");
	public Task ChangeAccountPasswordAsync (string oldPassword, string newPassword, Action? beforeCredentialChange = null) => RunAsync (async token =>
	{
		await StopSessionRecoveryCoreAsync ();
		await StopMonitorCoreAsync ();
		beforeCredentialChange?.Invoke ();
		try
			{
			await _client.ChangePasswordAsync (oldPassword, newPassword, token);
			}
		finally { if (!_client.HasValidSession) ClearAccount (); }
		Message = "Password change accepted. Sign in explicitly with your new password.";
	}, "Password change failed or its outcome is unknown. It was not retried. Sign in explicitly to check your account.");
	public Task SetAccountNicknameAsync (string nickname) => RunAsync (async token =>
	{
		await _client.SetAccountNicknameAsync (_client.AccountProfile ?? throw new InvalidOperationException (), nickname.Trim (), token);
		Message = "Profile change accepted. Sign in again to read the updated profile.";
	}, "Profile change failed or its outcome is unknown. Sign in again before another change.");
	public Task SetAccountPhotoAsync (string url) => RunAsync (async token =>
	{
		await _client.SetAccountPhotoAsync (_client.AccountProfile ?? throw new InvalidOperationException (), new Uri (url, UriKind.Absolute), token);
		Message = "Profile image change accepted. Sign in again to read the updated profile.";
	}, "Profile image change failed or its outcome is unknown. Use an uploaded HTTPS image URL.");
	}
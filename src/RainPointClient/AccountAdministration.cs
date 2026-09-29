// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Linq;
using System.Net.Http;
using System.Net.Mail;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using RainPointClient.Protocol;
namespace RainPointClient;

/// <summary>
/// Identifies the account operation for which an email verification code is requested.
/// </summary>
public enum RainPointEmailVerificationPurpose
	{
	/// <summary>Verification for a new account registration.</summary>
	Registration = 0,
	/// <summary>Verification for resetting an existing account password.</summary>
	PasswordReset = 1
	}
/// <summary>Instructions for the vendor's manual registration verification route. The client does not send this email.</summary>
public sealed class RainPointRegistrationEmail
	{
	/// <summary>
	/// Initializes registration email from the supplied typed values.
	/// </summary>
	/// <param name="owner">The client identity to which the verification result belongs.</param>
	/// <param name="email">The account email address.</param>
	/// <param name="areaCode">The account country calling code as digits without a plus sign.</param>
	/// <param name="destination">The vendor mailbox used by the manual registration-verification route.</param>
	/// <param name="content">The message content required by that manual verification route.</param>
	internal RainPointRegistrationEmail (object owner, string email, string areaCode, string destination, string content)
		{
		Owner = owner;
		Email = email;
		AreaCode = areaCode;
		Destination = destination;
		Content = content;
		}
	/// <summary>
	/// Gets the client identity that owns this verification result.
	/// </summary>
	internal object Owner
		{
		get;
		}
	/// <summary>
	/// Gets the email address associated with this verification or profile.
	/// </summary>
	public string Email
		{
		get;
		}
	/// <summary>
	/// Gets the account country calling code as digits without a plus sign.
	/// </summary>
	public string AreaCode
		{
		get;
		}
	/// <summary>
	/// Gets the vendor address to which the caller must send the manual verification email.
	/// </summary>
	public string Destination
		{
		get;
		}
	/// <summary>
	/// Gets the required manual verification-email content; the library does not send it.
	/// </summary>
	public string Content
		{
		get;
		}
	}
/// <summary>A successful email verification, usable for one matching registration/reset attempt on the same client.</summary>
public sealed class RainPointVerifiedEmail
	{
	/// <summary>
	/// Initializes verified email from the supplied typed values.
	/// </summary>
	/// <param name="owner">The client identity to which the verification result belongs.</param>
	/// <param name="email">The account email address.</param>
	/// <param name="areaCode">The account country calling code as digits without a plus sign.</param>
	/// <param name="purpose">The registration or password-reset purpose of the verification.</param>
	/// <param name="code">The verification code received by email.</param>
	internal RainPointVerifiedEmail (object owner, string email, string areaCode, RainPointEmailVerificationPurpose purpose, string code)
		{
		Owner = owner;
		Email = email;
		AreaCode = areaCode;
		Purpose = purpose;
		Code = code;
		}
	/// <summary>
	/// Gets the client identity that owns this verification result.
	/// </summary>
	internal object Owner
		{
		get;
		}
	/// <summary>
	/// Stores the verified email code for one matching account operation.
	/// </summary>
	internal string Code
		{
		get;
		}
	/// <summary>
	/// Tracks whether this observation has already been used for a write attempt.
	/// </summary>
	internal int Attempted;
	/// <summary>
	/// Gets the email address associated with this verification or profile.
	/// </summary>
	public string Email
		{
		get;
		}
	/// <summary>
	/// Gets the account country calling code as digits without a plus sign.
	/// </summary>
	public string AreaCode
		{
		get;
		}
	/// <summary>
	/// Gets the account operation for which this email was verified.
	/// </summary>
	public RainPointEmailVerificationPurpose Purpose
		{
		get;
		}
	}
/// <summary>
/// Captures the signed-in account profile for guarded nickname and photo updates.
/// </summary>
public sealed class RainPointAccountProfile
	{
	/// <summary>
	/// Initializes account profile from the supplied typed values.
	/// </summary>
	/// <param name="user">The authenticated profile returned by the login response.</param>
	internal RainPointAccountProfile (LoginUser user)
		{
		Id = user.Id;
		Email = user.Email;
		Nickname = user.Nickname;
		Photo = user.Photo;
		Language = user.Language;
		}
	/// <summary>
	/// Tracks whether this observation has already been used for a write attempt.
	/// </summary>
	internal int Attempted;
	/// <summary>
	/// Gets the signed-in account identifier, or null when not supplied.
	/// </summary>
	public long? Id
		{
		get;
		}
	/// <summary>
	/// Gets the profile email address, or null when not supplied.
	/// </summary>
	public string? Email
		{
		get;
		}
	/// <summary>
	/// Gets the profile display nickname, or null when not supplied.
	/// </summary>
	public string? Nickname
		{
		get;
		}
	/// <summary>
	/// Gets the reported profile-image address, or null when not supplied.
	/// </summary>
	public string? Photo
		{
		get;
		}
	/// <summary>
	/// Gets the reported profile language code, or null when not supplied.
	/// </summary>
	public string? Language
		{
		get;
		}
	}
public sealed partial class RainPointCloudClient
	{
	/// <summary>Profile observed at sign-in. Null after an attempted profile write; sign in again for a fresh observation.</summary>
	public RainPointAccountProfile? AccountProfile
		{
		get
			{
			var session = Volatile.Read (ref _session);
			return session?.ExpiresAt > DateTimeOffset.UtcNow && session.Profile?.Attempted == 0 ? session.Profile : null;
			}
		}
	/// <summary>
	/// Requests that the service email a verification code for registration or password reset.
	/// </summary>
	/// <param name="email">The account email address.</param>
	/// <param name="areaCode">The account country calling code as digits without a plus sign.</param>
	/// <param name="purpose">The registration or password-reset purpose of the verification.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task SendEmailVerificationCodeAsync (string email, string areaCode, RainPointEmailVerificationPurpose purpose, CancellationToken cancellationToken = default)
		{
		ValidateEmailVerification (email, areaCode, purpose);
		return AnonymousAccountAsync<EmailAccountRequest, ApiResult> ("app/common/core/email/send/code", new ()
			{
			Email = email,
			AreaCode = areaCode,
			Type = (int)purpose
			}, cancellationToken);
		}
	/// <summary>
	/// Verifies an emailed code and returns a token bound to this client, account and purpose.
	/// </summary>
	/// <param name="email">The account email address.</param>
	/// <param name="areaCode">The account country calling code as digits without a plus sign.</param>
	/// <param name="purpose">The registration or password-reset purpose of the verification.</param>
	/// <param name="code">The verification code received by email.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the typed verified email result.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task<RainPointVerifiedEmail> VerifyEmailCodeAsync (string email, string areaCode, RainPointEmailVerificationPurpose purpose, string code, CancellationToken cancellationToken = default)
		{
		ValidateEmailVerification (email, areaCode, purpose);
		RequireText (code, nameof (code));
		await AnonymousAccountAsync<EmailAccountRequest, ApiResult> ("app/common/core/email/verify/code", new ()
			{
			Email = email,
			AreaCode = areaCode,
			Type = (int)purpose,
			Code = code,
			AppCode = "2"
			}, cancellationToken).ConfigureAwait (false);
		return new (this, email, areaCode, purpose, code);
		}
	/// <summary>
	/// Obtains the vendor's manual registration-email instructions without sending an email.
	/// </summary>
	/// <param name="email">The account email address.</param>
	/// <param name="areaCode">The account country calling code as digits without a plus sign.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the typed registration email result.</returns>
	/// <exception cref="RainPointException">Registration email instructions are incomplete. The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task<RainPointRegistrationEmail> GetRegistrationEmailAsync (string email, string areaCode, CancellationToken cancellationToken = default)
		{
		ValidateEmailVerification (email, areaCode, RainPointEmailVerificationPurpose.Registration);
		var response = await AnonymousAccountAsync<EmailAccountRequest, ApiResult<ManualEmailWire>> ("app/common/core/email/manual/captcha", new ()
			{
			Email = email,
			AreaCode = areaCode,
			Type = 0
			}, cancellationToken).ConfigureAwait (false);
		var data = RequireData (response);
		if (!ValidEmail (data.Email) || string.IsNullOrWhiteSpace (data.Content))
			throw new RainPointException ("Registration email instructions are incomplete.");
		return new (this, email, areaCode, data.Email!, data.Content!);
		}
	/// <summary>
	/// Checks completion of the manual registration-email route for instructions issued by this client.
	/// </summary>
	/// <param name="instructions">Manual-email instructions issued by this client for the matching account.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the typed verified email result.</returns>
	/// <exception cref="System.ArgumentNullException">A required argument is null.</exception>
	/// <exception cref="System.ArgumentException">Use registration instructions from this client.</exception>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task<RainPointVerifiedEmail> VerifyRegistrationEmailAsync (RainPointRegistrationEmail instructions, CancellationToken cancellationToken = default)
		{
		if (instructions is null)
			throw new ArgumentNullException (nameof (instructions));
		if (!ReferenceEquals (instructions.Owner, this))
			throw new ArgumentException ("Use registration instructions from this client.");
		await AnonymousAccountAsync<EmailAccountRequest, ApiResult> ("app/common/core/email/verify/manual/captcha", new ()
			{
			Email = instructions.Email,
			AreaCode = instructions.AreaCode,
			Type = 0
			}, cancellationToken).ConfigureAwait (false);
		return new (this, instructions.Email, instructions.AreaCode, RainPointEmailVerificationPurpose.Registration, instructions.Content);
		}
	/// <summary>Registers the verified email. The caller must obtain the user's agreement and supply their selected country. Never automatically retries.</summary>
	/// <param name="verified">An unused verification result from this client matching the account and requested operation.</param>
	/// <param name="password">The account password; it is not persisted or logged by the client.</param>
	/// <param name="countryIsoCode">The caller-selected country ISO code for registration.</param>
	/// <param name="deviceId">The caller-selected app/device identity for the account operation.</param>
	/// <param name="agreementVersion">The accepted agreement version, where supplied by the registration flow.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="System.ArgumentException">Use a two-letter uppercase country code.</exception>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task RegisterEmailAsync (RainPointVerifiedEmail verified, string password, string countryIsoCode, string deviceId, string? agreementVersion = null, CancellationToken cancellationToken = default)
		{
		ValidateAccountPassword (password);
		if (countryIsoCode is null || countryIsoCode.Length != 2 || countryIsoCode.Any (c => c is < 'A' or > 'Z'))
			throw new ArgumentException ("Use a two-letter uppercase country code.", nameof (countryIsoCode));
		RequireText (deviceId, nameof (deviceId));
		ValidateVerifiedEmail (verified, RainPointEmailVerificationPurpose.Registration);
		var request = new EmailAccountRequest { Email = verified.Email, AreaCode = verified.AreaCode, Code = verified.Code, Type = 0, Password = Hash (password), PushId = "", DeviceType = 1, DeviceModel = "RainPointClient", Language = "en", Country = countryIsoCode, DeviceId = deviceId, AgreementVersion = agreementVersion };
		return AnonymousAccountAsync<EmailAccountRequest, ApiResult> ("app/common/core/account/email/register", request, cancellationToken, verified);
		}
	/// <summary>
	/// Submits one password-reset attempt using a matching verified email token.
	/// </summary>
	/// <param name="verified">An unused verification result from this client matching the account and requested operation.</param>
	/// <param name="newPassword">The replacement account password.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task ResetPasswordByEmailAsync (RainPointVerifiedEmail verified, string newPassword, CancellationToken cancellationToken = default)
		{
		ValidateAccountPassword (newPassword);
		ValidateVerifiedEmail (verified, RainPointEmailVerificationPurpose.PasswordReset);
		return AnonymousAccountAsync<EmailAccountRequest, ApiResult> ("app/common/core/account/email/resetPassword", new ()
			{
			Email = verified.Email,
			AreaCode = verified.AreaCode,
			Code = verified.Code,
			Type = 1,
			Password = Hash (newPassword)
			}, cancellationToken, verified);
		}
	/// <summary>Changes the signed-in account password. Stop automatic recovery first. Any submitted attempt clears the local session; sign in explicitly to reconcile uncertainty.</summary>
	/// <param name="oldPassword">The current account password.</param>
	/// <param name="newPassword">The replacement account password.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="System.ArgumentException">The new password must differ.</exception>
	/// <exception cref="System.InvalidOperationException">Stop automatic recovery before changing account credentials.</exception>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task ChangePasswordAsync (string oldPassword, string newPassword, CancellationToken cancellationToken = default)
		{
		ValidateAccountPassword (oldPassword);
		ValidateAccountPassword (newPassword);
		if (oldPassword == newPassword)
			throw new ArgumentException ("The new password must differ.");
		if (Volatile.Read (ref _sessionRecoveryActive) != 0)
			throw new InvalidOperationException ("Stop automatic recovery before changing account credentials.");
		await _loginGate.WaitAsync (cancellationToken).ConfigureAwait (false);
		try
			{
			ThrowIfDisposed ();
			var session = GetSession ();
			cancellationToken.ThrowIfCancellationRequested ();
			try
				{
				var result = await SendAsync<PasswordChangeWire, ApiResult> (HttpMethod.Post, "app/member/user/setPassword", new ()
					{
					Password = Hash (oldPassword),
					NewPassword = Hash (newPassword)
					}, session, cancellationToken).ConfigureAwait (false);
				CheckResult (result, session);
				}
			finally { Interlocked.CompareExchange (ref _session, null, session); }
			}
		finally { _loginGate.Release (); }
		}
	/// <summary>
	/// Changes the nickname from a matching account-profile observation without replaying the write.
	/// </summary>
	/// <param name="expected">An unused, current account profile observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="nickname">The new account display nickname.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task SetAccountNicknameAsync (RainPointAccountProfile expected, string nickname, CancellationToken cancellationToken = default)
		{
		RequireText (nickname, nameof (nickname));
		return WriteProfileAsync (expected, new ()
			{
			Nickname = nickname
			}, cancellationToken);
		}
	/// <summary>Associates an already uploaded HTTPS profile image. This does not upload or fetch a local image.</summary>
	/// <param name="expected">An unused, current account profile observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="photo">The HTTPS address of an already uploaded profile image; the method does not upload or fetch an image.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="System.ArgumentException">Use an absolute HTTPS image URL.</exception>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task SetAccountPhotoAsync (RainPointAccountProfile expected, Uri photo, CancellationToken cancellationToken = default)
		{
		if (photo is null || !photo.IsAbsoluteUri || photo.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty (photo.UserInfo))
			throw new ArgumentException ("Use an absolute HTTPS image URL.", nameof (photo));
		return WriteProfileAsync (expected, new ()
			{
			Photo = photo.AbsoluteUri
			}, cancellationToken);
		}
	private async Task WriteProfileAsync (RainPointAccountProfile expected, ProfilePatch request, CancellationToken token)
		{
		if (expected is null)
			throw new ArgumentNullException (nameof (expected));
		var session = GetSession ();
		if (!ReferenceEquals (session.Profile, expected))
			throw new InvalidOperationException ("Use this session's profile observation.");
		token.ThrowIfCancellationRequested ();
		if (Interlocked.CompareExchange (ref expected.Attempted, 1, 0) != 0)
			throw new InvalidOperationException ("Sign in again before another profile write.");
		var result = await SendAsync<ProfilePatch, ApiResult> (HttpMethod.Post, "app/member/user/info/set", request, session, token).ConfigureAwait (false);
		CheckResult (result, session);
		}
	private async Task<TResponse> AnonymousAccountAsync<TRequest, TResponse> (string path, TRequest request, CancellationToken token, RainPointVerifiedEmail? verified = null) where TRequest : class where TResponse : ApiResult
		{
		ThrowIfDisposed ();
		await _loginGate.WaitAsync (token).ConfigureAwait (false);
		try
			{
			if (Volatile.Read (ref _session) is not null || Volatile.Read (ref _sessionRecoveryActive) != 0)
				throw new InvalidOperationException ("Sign out and stop recovery before registration or password recovery.");
			token.ThrowIfCancellationRequested ();
			if (verified is not null && Interlocked.CompareExchange (ref verified.Attempted, 1, 0) != 0)
				throw new InvalidOperationException ("Verify the email again before another account write attempt.");
			var result = await SendAsync<TRequest, TResponse> (HttpMethod.Post, path, request, null, token).ConfigureAwait (false);
			CheckResult (result, null);
			return result;
			}
		finally { _loginGate.Release (); }
		}
	private void ValidateVerifiedEmail (RainPointVerifiedEmail verified, RainPointEmailVerificationPurpose purpose)
		{
		if (verified is null)
			throw new ArgumentNullException (nameof (verified));
		if (!ReferenceEquals (verified.Owner, this) || verified.Purpose != purpose || Volatile.Read (ref verified.Attempted) != 0)
			throw new InvalidOperationException ("Use a fresh verification for this operation on this client.");
		}
	private static void ValidateEmailVerification (string email, string areaCode, RainPointEmailVerificationPurpose purpose)
		{
		if (!ValidEmail (email))
			throw new ArgumentException ("Use a single email address.", nameof (email));
		if (string.IsNullOrEmpty (areaCode) || areaCode.Any (c => c is < '0' or > '9'))
			throw new ArgumentException ("Use a numeric country calling code.", nameof (areaCode));
		if (!Enum.IsDefined (typeof (RainPointEmailVerificationPurpose), purpose))
			throw new ArgumentOutOfRangeException (nameof (purpose));
		}
	private static bool ValidEmail (string? email)
		{
		try
			{
			return !string.IsNullOrWhiteSpace (email) && new MailAddress (email).Address == email && email.IndexOfAny (new[] { '\r', '\n' }) < 0;
			}
		catch (FormatException) { return false; }
		}
	private static void ValidateAccountPassword (string password)
		{
		if (password is null || password.Length is < 6 or > 20)
			throw new ArgumentException ("Passwords must contain 6 to 20 characters.", nameof (password));
		}
	private sealed class EmailAccountRequest
		{
		/// <summary>
		/// Stores the email protocol field for email account request.
		/// </summary>
		[JsonPropertyName ("email")] public string Email { get; set; } = "";
		/// <summary>
		/// Stores the areaCode protocol field for email account request.
		/// </summary>
		[JsonPropertyName ("areaCode")] public string AreaCode { get; set; } = "";
		/// <summary>
		/// Stores the type protocol field for email account request.
		/// </summary>
		[JsonPropertyName ("type")]
		public int Type
			{
			get; set;
			}
		/// <summary>
		/// Stores the verified email code for one matching account operation.
		/// </summary>
		[JsonPropertyName ("code"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Code
			{
			get; set;
			}
		/// <summary>
		/// Stores the appCode protocol field for email account request.
		/// </summary>
		[JsonPropertyName ("appCode"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? AppCode
			{
			get; set;
			}
		/// <summary>
		/// Stores the password protocol field for email account request.
		/// </summary>
		[JsonPropertyName ("password"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Password
			{
			get; set;
			}
		/// <summary>
		/// Stores the pushId protocol field for email account request.
		/// </summary>
		[JsonPropertyName ("pushId"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? PushId
			{
			get; set;
			}
		/// <summary>
		/// Stores the deviceType protocol field for email account request.
		/// </summary>
		[JsonPropertyName ("deviceType"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public int? DeviceType
			{
			get; set;
			}
		/// <summary>
		/// Stores the deviceModel protocol field for email account request.
		/// </summary>
		[JsonPropertyName ("deviceModel"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? DeviceModel
			{
			get; set;
			}
		/// <summary>
		/// Stores the language protocol field for email account request.
		/// </summary>
		[JsonPropertyName ("language"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Language
			{
			get; set;
			}
		/// <summary>
		/// Stores the isocode protocol field for email account request.
		/// </summary>
		[JsonPropertyName ("isocode"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Country
			{
			get; set;
			}
		/// <summary>
		/// Stores the deviceId protocol field for email account request.
		/// </summary>
		[JsonPropertyName ("deviceId"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? DeviceId
			{
			get; set;
			}
		/// <summary>
		/// Stores the agreementVer protocol field for email account request.
		/// </summary>
		[JsonPropertyName ("agreementVer"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? AgreementVersion
			{
			get; set;
			}
		}
	private sealed class ManualEmailWire
		{
		/// <summary>
		/// Stores the email protocol field for manual email wire.
		/// </summary>
		[JsonPropertyName ("email")]
		public string? Email
			{
			get; set;
			}
		/// <summary>
		/// Stores the content protocol field for manual email wire.
		/// </summary>
		[JsonPropertyName ("content")]
		public string? Content
			{
			get; set;
			}
		}
	private sealed class PasswordChangeWire
		{
		/// <summary>
		/// Stores the password protocol field for password change wire.
		/// </summary>
		[JsonPropertyName ("password")] public string Password { get; set; } = "";
		/// <summary>
		/// Stores the newPassword protocol field for password change wire.
		/// </summary>
		[JsonPropertyName ("newPassword")] public string NewPassword { get; set; } = "";
		}
	private sealed class ProfilePatch
		{
		/// <summary>
		/// Stores the nickname protocol field for profile patch.
		/// </summary>
		[JsonPropertyName ("nickname"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Nickname
			{
			get; set;
			}
		/// <summary>
		/// Stores the photo protocol field for profile patch.
		/// </summary>
		[JsonPropertyName ("photo"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Photo
			{
			get; set;
			}
		}
	}
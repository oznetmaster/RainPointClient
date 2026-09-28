using System;
using System.Linq;
using System.Net.Http;
using System.Net.Mail;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using RainPointClient.Protocol;
namespace RainPointClient;

public enum RainPointEmailVerificationPurpose
	{
	Registration = 0, PasswordReset = 1
	}
/// <summary>Instructions for the vendor's manual registration verification route. The client does not send this email.</summary>
public sealed class RainPointRegistrationEmail
	{
	internal RainPointRegistrationEmail (object owner, string email, string areaCode, string destination, string content)
		{
		Owner = owner;
		Email = email;
		AreaCode = areaCode;
		Destination = destination;
		Content = content;
		}
	internal object Owner
		{
		get;
		}
	public string Email
		{
		get;
		}
	public string AreaCode
		{
		get;
		}
	public string Destination
		{
		get;
		}
	public string Content
		{
		get;
		}
	}
/// <summary>A successful email verification, usable for one matching registration/reset attempt on the same client.</summary>
public sealed class RainPointVerifiedEmail
	{
	internal RainPointVerifiedEmail (object owner, string email, string areaCode, RainPointEmailVerificationPurpose purpose, string code)
		{
		Owner = owner;
		Email = email;
		AreaCode = areaCode;
		Purpose = purpose;
		Code = code;
		}
	internal object Owner
		{
		get;
		}
	internal string Code
		{
		get;
		}
	internal int Attempted;
	public string Email
		{
		get;
		}
	public string AreaCode
		{
		get;
		}
	public RainPointEmailVerificationPurpose Purpose
		{
		get;
		}
	}
public sealed class RainPointAccountProfile
	{
	internal RainPointAccountProfile (LoginUser user)
		{
		Id = user.Id;
		Email = user.Email;
		Nickname = user.Nickname;
		Photo = user.Photo;
		Language = user.Language;
		}
	internal int Attempted;
	public long? Id
		{
		get;
		}
	public string? Email
		{
		get;
		}
	public string? Nickname
		{
		get;
		}
	public string? Photo
		{
		get;
		}
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
	public Task SetAccountNicknameAsync (RainPointAccountProfile expected, string nickname, CancellationToken cancellationToken = default)
		{
		RequireText (nickname, nameof (nickname));
		return WriteProfileAsync (expected, new ()
			{
			Nickname = nickname
			}, cancellationToken);
		}
	/// <summary>Associates an already uploaded HTTPS profile image. This does not upload or fetch a local image.</summary>
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
		[JsonPropertyName ("email")] public string Email { get; set; } = "";
		[JsonPropertyName ("areaCode")] public string AreaCode { get; set; } = "";
		[JsonPropertyName ("type")]
		public int Type
			{
			get; set;
			}
		[JsonPropertyName ("code"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Code
			{
			get; set;
			}
		[JsonPropertyName ("appCode"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? AppCode
			{
			get; set;
			}
		[JsonPropertyName ("password"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Password
			{
			get; set;
			}
		[JsonPropertyName ("pushId"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? PushId
			{
			get; set;
			}
		[JsonPropertyName ("deviceType"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public int? DeviceType
			{
			get; set;
			}
		[JsonPropertyName ("deviceModel"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? DeviceModel
			{
			get; set;
			}
		[JsonPropertyName ("language"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Language
			{
			get; set;
			}
		[JsonPropertyName ("isocode"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Country
			{
			get; set;
			}
		[JsonPropertyName ("deviceId"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? DeviceId
			{
			get; set;
			}
		[JsonPropertyName ("agreementVer"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? AgreementVersion
			{
			get; set;
			}
		}
	private sealed class ManualEmailWire
		{
		[JsonPropertyName ("email")]
		public string? Email
			{
			get; set;
			}
		[JsonPropertyName ("content")]
		public string? Content
			{
			get; set;
			}
		}
	private sealed class PasswordChangeWire
		{
		[JsonPropertyName ("password")] public string Password { get; set; } = ""; [JsonPropertyName ("newPassword")] public string NewPassword { get; set; } = "";
		}
	private sealed class ProfilePatch
		{
		[JsonPropertyName ("nickname"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Nickname
			{
			get; set;
			}
		[JsonPropertyName ("photo"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Photo
			{
			get; set;
			}
		}
	}
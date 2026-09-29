// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using RainPointClient.Protocol;
namespace RainPointClient;

/// <summary>
/// Identifies the non-owner membership roles accepted by the home-management API.
/// </summary>
public enum RainPointMemberRole
	{
	/// <summary>A regular invited home member.</summary>
	Member = 0,
	/// <summary>An invited home administrator; this does not transfer home ownership.</summary>
	Administrator = 1
	}
/// <summary>
/// Selects the pressure unit used for app display preferences.
/// </summary>
public enum RainPointPressureUnit
	{
	/// <summary>Display pressure in pascals.</summary>
	Pascal = 0,
	/// <summary>Display pressure in inches of mercury.</summary>
	InchesOfMercury = 1,
	/// <summary>Display pressure in millimetres of mercury.</summary>
	MillimetresOfMercury = 2
	}
/// <summary>Display units only. Client readings retain their explicitly documented units.</summary>
public sealed class RainPointDisplayUnits
	{
	/// <summary>
	/// Gets or sets whether the app uses a twelve-hour clock instead of a twenty-four-hour clock.
	/// </summary>
	public bool TwelveHourClock
		{
		get; set;
		}
	/// <summary>
	/// Gets or sets whether the app displays Fahrenheit; typed client weather readings remain Celsius.
	/// </summary>
	public bool Fahrenheit
		{
		get; set;
		}
	/// <summary>
	/// Gets or sets the app's imperial-length display preference.
	/// </summary>
	public bool ImperialLength
		{
		get; set;
		}
	/// <summary>
	/// Gets or sets the app's imperial-volume display preference; typed client usage remains litres.
	/// </summary>
	public bool ImperialVolume
		{
		get; set;
		}
	/// <summary>
	/// Gets or sets the recognized app date format, or null to preserve an unrecognized format on update.
	/// </summary>
	public RainPointDateFormat? DateFormat
		{
		get; set;
		}
	/// <summary>
	/// Gets or sets the app's pressure display unit.
	/// </summary>
	public RainPointPressureUnit Pressure
		{
		get; set;
		}
	/// <summary>
	/// Decodes recognized display-unit flags, retaining unsupported data through the original observation.
	/// </summary>
	/// <param name="encoded">The original vendor-encoded field to decode without guessing unsupported values.</param>
	/// <returns>The recognized display preferences, or null for an unsupported or malformed representation.</returns>
	internal static RainPointDisplayUnits? Decode (string? encoded)
		{
		if (encoded is null || encoded.Length < 2 || !byte.TryParse (encoded.Substring (0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value) || ((value >> 5) & 3) == 3)
			return null;
		return new ()
			{
			TwelveHourClock = (value & 1) != 0,
			Fahrenheit = (value & 2) != 0,
			ImperialLength = (value & 4) != 0,
			ImperialVolume = (value & 16) != 0,
			Pressure = (RainPointPressureUnit)((value >> 5) & 3),
			DateFormat = DecodeDateFormat (encoded)
			};
		}
	/// <summary>
	/// Decodes a recognized date-format byte without guessing unknown values.
	/// </summary>
	/// <param name="encoded">The original vendor-encoded field to decode without guessing unsupported values.</param>
	/// <returns>The recognized format, or null when absent or unknown.</returns>
	internal static RainPointDateFormat? DecodeDateFormat (string encoded) => encoded.Length >= 4 && byte.TryParse (encoded.Substring (2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte format) && Enum.IsDefined (typeof (RainPointDateFormat), (int)format) ? (RainPointDateFormat)format : null;
	/// <summary>
	/// Encodes requested display preferences while preserving unrelated bytes and flag bits.
	/// </summary>
	/// <param name="original">The original encoded field whose unrelated bytes and bits must be retained.</param>
	/// <returns>The updated encoded preferences with unrelated data preserved.</returns>
	/// <exception cref="System.ArgumentOutOfRangeException">An argument is outside the supported range described above.</exception>
	internal string Encode (string original)
		{
		if (!Enum.IsDefined (typeof (RainPointPressureUnit), Pressure))
			throw new ArgumentOutOfRangeException (nameof (Pressure));
		int value = byte.Parse (original.Substring (0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) & 0x88;
		value |= (TwelveHourClock ? 1 : 0) | (Fahrenheit ? 2 : 0) | (ImperialLength ? 4 : 0) | (ImperialVolume ? 16 : 0) | ((int)Pressure << 5);
		string suffix = original.Substring (2);
		if (DateFormat.HasValue)
			{
			if (!Enum.IsDefined (typeof (RainPointDateFormat), DateFormat.Value))
				throw new ArgumentOutOfRangeException (nameof (DateFormat));
			int format = (int)DateFormat.Value;
			value = (value & 127) | (format is >= 8 and <= 11 or 13 ? 128 : 0);
			suffix = format.ToString ("X2", CultureInfo.InvariantCulture) + (original.Length > 4 ? original.Substring (4) : string.Empty);
			}
		return value.ToString ("X2", CultureInfo.InvariantCulture) + suffix;
		}
	}
/// <summary>A home management observation. Read again after any attempted change.</summary>
public sealed class RainPointHomeDetails
	{
	/// <summary>
	/// Initializes home details from the supplied typed values.
	/// </summary>
	/// <param name="wire">The attributed protocol response from which to create the typed observation.</param>
	/// <param name="session">The session identity that owns this observation and guards subsequent writes.</param>
	internal RainPointHomeDetails (HomeDetailsResponse wire, object session)
		{
		Wire = wire;
		Session = session;
		CalendarTimeZone = RainPointCalendarTimeZone.Decode (wire.Offset, wire.DaylightTransitions);
		Rooms = Array.AsReadOnly ((wire.Rooms ?? []).Select (r => new RainPointRoom (r)).ToArray ());
		}
	/// <summary>
	/// Gets the original attributed response retained for guarded edits and unknown-field preservation.
	/// </summary>
	internal HomeDetailsResponse Wire
		{
		get;
		}
	/// <summary>
	/// Gets the session identity that owns this observation.
	/// </summary>
	internal object Session
		{
		get;
		}
	/// <summary>
	/// Tracks whether this observation has already been used for a write attempt.
	/// </summary>
	internal int Attempted;
	/// <summary>
	/// Gets the cloud home identifier.
	/// </summary>
	public long Id => Wire.Id;
	/// <summary>
	/// Gets the home's assigned display name.
	/// </summary>
	public string Name => Wire.Name;
	/// <summary>
	/// Gets the reported IANA time-zone name, or null when absent.
	/// </summary>
	public string? TimeZoneName => Wire.ZoneName;
	/// <summary>
	/// Gets the reported base UTC offset in minutes, or null when absent.
	/// </summary>
	public int? TimeZoneOffsetMinutes => Wire.Offset;
	/// <summary>Null when the reported calendar offset/transition data is missing or malformed.</summary>
	public RainPointCalendarTimeZone? CalendarTimeZone
		{
		get;
		}
	/// <summary>
	/// Gets whether the account is the home owner, or null for unknown permission metadata.
	/// </summary>
	public bool? IsOwner => Wire.Owner is 0 or 1 ? Wire.Owner == 1 : null;
	/// <summary>
	/// Gets the recognized non-owner role, or null when the role code is unknown.
	/// </summary>
	public RainPointMemberRole? Role => Wire.Rights is 0 or 1 ? (RainPointMemberRole)Wire.Rights : null;
	/// <summary>
	/// Gets latitude in decimal degrees, or null for an absent or invalid coordinate.
	/// </summary>
	public decimal? Latitude => Wire.Latitude is >= -90000000 and <= 90000000 ? Wire.Latitude / 1000000m : null;
	/// <summary>
	/// Gets longitude in decimal degrees, or null for an absent or invalid coordinate.
	/// </summary>
	public decimal? Longitude => Wire.Longitude is >= -180000000 and <= 180000000 ? Wire.Longitude / 1000000m : null;
	/// <summary>
	/// Gets decoded app display preferences, or null when unsupported or malformed.
	/// </summary>
	public RainPointDisplayUnits? DisplayUnits => RainPointDisplayUnits.Decode (Wire.Units);
	/// <summary>
	/// Gets the vendor currency code, or null when absent.
	/// </summary>
	public int? CurrencyCode => Wire.Currency;
	/// <summary>
	/// Gets the rooms included in this home snapshot.
	/// </summary>
	public IReadOnlyList<RainPointRoom> Rooms
		{
		get;
		}
	}
/// <summary>
/// Describes a room from a home-management snapshot.
/// </summary>
public sealed class RainPointRoom
	{
	/// <summary>
	/// Initializes room from the supplied typed values.
	/// </summary>
	/// <param name="wire">The attributed protocol response from which to create the typed observation.</param>
	internal RainPointRoom (RoomResponse wire)
		{
		Wire = wire;
		}
	/// <summary>
	/// Gets the original attributed response retained for guarded edits and unknown-field preservation.
	/// </summary>
	internal RoomResponse Wire
		{
		get;
		}
	/// <summary>
	/// Gets the cloud or catalog identifier for this record.
	/// </summary>
	public long Id => Wire.Id;
	/// <summary>
	/// Gets the display name supplied for this record.
	/// </summary>
	public string Name => Wire.Name;
	}
/// <summary>
/// Describes an account's membership and reported privileges in a home.
/// </summary>
public sealed class RainPointMember
	{
	/// <summary>
	/// Initializes member from the supplied typed values.
	/// </summary>
	/// <param name="wire">The attributed protocol response from which to create the typed observation.</param>
	internal RainPointMember (MemberResponse wire)
		{
		Wire = wire;
		}
	/// <summary>
	/// Gets the original attributed response retained for guarded edits and unknown-field preservation.
	/// </summary>
	internal MemberResponse Wire
		{
		get;
		}
	/// <summary>
	/// Gets the member's cloud account identifier.
	/// </summary>
	public long Id => Wire.Id;
	/// <summary>
	/// Gets the member's display name, or null when absent.
	/// </summary>
	public string? Name => Wire.Name;
	/// <summary>
	/// Gets the member's email address, or null when absent.
	/// </summary>
	public string? Email => Wire.Email;
	/// <summary>
	/// Gets the reported owner flag, or null for unknown permission metadata.
	/// </summary>
	public bool? IsOwner => Wire.Owner is 0 or 1 ? Wire.Owner == 1 : null;
	/// <summary>
	/// Gets the recognized membership role, or null when the role code is unknown.
	/// </summary>
	public RainPointMemberRole? Role => Wire.Rights is 0 or 1 ? (RainPointMemberRole)Wire.Rights : null;
	}
/// <summary>
/// Captures a pending home invitation for a guarded accept or decline operation.
/// </summary>
public sealed class RainPointInvitation
	{
	/// <summary>
	/// Initializes invitation from the supplied typed values.
	/// </summary>
	/// <param name="wire">The attributed protocol response from which to create the typed observation.</param>
	/// <param name="session">The session identity that owns this observation and guards subsequent writes.</param>
	internal RainPointInvitation (InvitationResponse wire, object session)
		{
		Wire = wire;
		Session = session;
		}
	/// <summary>
	/// Gets the original attributed response retained for guarded edits and unknown-field preservation.
	/// </summary>
	internal InvitationResponse Wire
		{
		get;
		}
	/// <summary>
	/// Gets the session identity that owns this observation.
	/// </summary>
	internal object Session
		{
		get;
		}
	/// <summary>
	/// Tracks whether this observation has already been used for a write attempt.
	/// </summary>
	internal int Attempted;
	/// <summary>
	/// Gets the cloud or catalog identifier for this record.
	/// </summary>
	public long Id => Wire.Id;
	/// <summary>
	/// Gets the cloud home identifier associated with this record.
	/// </summary>
	public long HomeId => Wire.HomeId;
	/// <summary>
	/// Gets the assigned name of the home associated with this record.
	/// </summary>
	public string HomeName => Wire.HomeName;
	}
public sealed partial class RainPointCloudClient
	{

	/// <summary>Creates a home with the specified IANA time-zone name and room names. No retry is performed.</summary>
	/// <param name="name">The nonempty display name to assign.</param>
	/// <param name="timeZoneName">The IANA time-zone name reported in the vendor catalog.</param>
	/// <param name="rooms">Distinct, nonempty names of the rooms to create.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the typed home details result.</returns>
	/// <exception cref="System.ArgumentNullException">A required argument is null.</exception>
	/// <exception cref="System.ArgumentException">Room names must be nonempty and distinct.</exception>
	/// <exception cref="RainPointException">The accepted home creation did not return an identifier. Discover homes before retrying. The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task<RainPointHomeDetails> CreateHomeAsync (string name, string timeZoneName, IReadOnlyList<string> rooms, CancellationToken cancellationToken = default)
		{
		RequireText (name, nameof (name));
		RequireText (timeZoneName, nameof (timeZoneName));
		if (rooms is null)
			throw new ArgumentNullException (nameof (rooms));
		string[] names = rooms.ToArray ();
		if (names.Any (string.IsNullOrWhiteSpace) || names.Distinct (StringComparer.Ordinal).Count () != names.Length)
			throw new ArgumentException ("Room names must be nonempty and distinct.", nameof (rooms));
		Session session = GetSession ();
		var result = await SendAsync<CreateHomeRequest, ApiResult<HomeDetailsResponse>> (HttpMethod.Post, "app/member/appHome/create", new ()
			{
			Name = name,
			TimeZone = timeZoneName,
			Rooms = names
			}, session, cancellationToken).ConfigureAwait (false);
		CheckResult (result, session);
		var data = RequireData (result);
		if (data.Id <= 0)
			throw new RainPointException ("The accepted home creation did not return an identifier. Discover homes before retrying.");
		return await GetHomeAsync (data.Id, cancellationToken).ConfigureAwait (false);
		}
	/// <summary>Deletes a home. This can remove access to its devices and cannot be undone by this client.</summary>
	/// <param name="expected">An unused, current home details observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task DeleteHomeAsync (RainPointHomeDetails expected, CancellationToken cancellationToken = default) => WriteHomeAsync (expected, "app/member/appHome/delete", new HomePatch { Id = expected?.Id ?? 0 }, cancellationToken);
	/// <summary>Leaves a shared home.</summary>
	/// <param name="expected">An unused, current home details observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task LeaveHomeAsync (RainPointHomeDetails expected, CancellationToken cancellationToken = default) => WriteHomeAsync (expected, "app/member/appHome/quit", new HomePatch { Id = expected?.Id ?? 0 }, cancellationToken);
	/// <summary>Sends a home invitation to an already registered email account. Caller must explicitly intend to send it.</summary>
	/// <param name="expected">An unused, current home details observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="email">The account email address.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="System.ArgumentException">Use an email address without a display name.</exception>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task InviteMemberAsync (RainPointHomeDetails expected, string email, CancellationToken cancellationToken = default)
		{
		RequireText (email, nameof (email));
		var parsed = new System.Net.Mail.MailAddress (email);
		if (parsed.Address != email)
			throw new ArgumentException ("Use an email address without a display name.", nameof (email));
		return WriteHomeAsync (expected, "app/member/appHome/invite/create", new MemberPatch { HomeId = expected?.Id ?? 0, Email = email }, cancellationToken);
		}
	/// <summary>
	/// Changes a discovered non-owner member's role after validating current home permissions.
	/// </summary>
	/// <param name="expectedHome">The current home-management observation used to validate permissions and detect conflicting changes.</param>
	/// <param name="expectedMember">The observed member belonging to the selected home.</param>
	/// <param name="role">The supported membership role to assign; ownership transfer uses its separate operation.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="System.ArgumentOutOfRangeException">An argument is outside the supported range described above.</exception>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task SetMemberRoleAsync (RainPointHomeDetails expectedHome, RainPointMember expectedMember, RainPointMemberRole role, CancellationToken cancellationToken = default)
		{
		if (!Enum.IsDefined (typeof (RainPointMemberRole), role))
			throw new ArgumentOutOfRangeException (nameof (role));
		return WriteMemberAsync (expectedHome, expectedMember, "app/member/appHome/member/right/update", new MemberPatch { HomeId = expectedHome?.Id ?? 0, UserId = expectedMember?.Id, Role = (int)role }, cancellationToken);
		}
	/// <summary>
	/// Removes the selected member from the home after validating the observed membership.
	/// </summary>
	/// <param name="expectedHome">The current home-management observation used to validate permissions and detect conflicting changes.</param>
	/// <param name="expectedMember">The observed member belonging to the selected home.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task RemoveMemberAsync (RainPointHomeDetails expectedHome, RainPointMember expectedMember, CancellationToken cancellationToken = default) => WriteMemberAsync (expectedHome, expectedMember, "app/member/appHome/member/delete", new MemberPatch { HomeId = expectedHome?.Id ?? 0, UserId = expectedMember?.Id }, cancellationToken);
	/// <summary>Transfers home ownership to the selected member. This changes the caller's privileges.</summary>
	/// <param name="expectedHome">The current home-management observation used to validate permissions and detect conflicting changes.</param>
	/// <param name="expectedMember">The observed member belonging to the selected home.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task TransferHomeOwnershipAsync (RainPointHomeDetails expectedHome, RainPointMember expectedMember, CancellationToken cancellationToken = default) => WriteMemberAsync (expectedHome, expectedMember, "app/member/appHome/owner/transfer", new MemberPatch { HomeId = expectedHome?.Id ?? 0, TargetUserId = expectedMember?.Id }, cancellationToken);
	private async Task WriteMemberAsync (RainPointHomeDetails home, RainPointMember member, string path, MemberPatch request, CancellationToken token)
		{
		if (home is null)
			throw new ArgumentNullException (nameof (home));
		if (member is null)
			throw new ArgumentNullException (nameof (member));
		if (member.Wire.HomeId.HasValue && member.Wire.HomeId != home.Id)
			throw new ArgumentException ("The member belongs to a different home.", nameof (member));
		if (member.IsOwner != false)
			throw new InvalidOperationException ("Select a non-owner member with known ownership status.");
		var matches = (await GetMembersAsync (home.Id, token).ConfigureAwait (false)).Where (m => m.Id == member.Id).ToArray ();
		if (matches.Length != 1 || JsonSerializer.Serialize (member.Wire, _json) != JsonSerializer.Serialize (matches[0].Wire, _json))
			throw new InvalidOperationException ("Member changed. Reload before writing.");
		await WriteHomeAsync (home, path, request, token).ConfigureAwait (false);
		}
	private sealed class CreateHomeRequest
		{
		/// <summary>
		/// Stores the homeName protocol field for create home request.
		/// </summary>
		[JsonPropertyName ("homeName")] public string Name { get; set; } = string.Empty;
		/// <summary>
		/// Stores the zoneName protocol field for create home request.
		/// </summary>
		[JsonPropertyName ("zoneName")] public string TimeZone { get; set; } = string.Empty;
		/// <summary>
		/// Stores the rooms protocol field for create home request.
		/// </summary>
		[JsonPropertyName ("rooms")] public string[] Rooms { get; set; } = [];
		}
	private sealed class MemberPatch
		{
		/// <summary>
		/// Stores the hid protocol field for member patch.
		/// </summary>
		[JsonPropertyName ("hid")]
		public long HomeId
			{
			get; set;
			}
		/// <summary>
		/// Stores the uid protocol field for member patch.
		/// </summary>
		[JsonPropertyName ("uid"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public long? UserId
			{
			get; set;
			}
		/// <summary>
		/// Stores the targetUid protocol field for member patch.
		/// </summary>
		[JsonPropertyName ("targetUid"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public long? TargetUserId
			{
			get; set;
			}
		/// <summary>
		/// Stores the rightCode protocol field for member patch.
		/// </summary>
		[JsonPropertyName ("rightCode"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public int? Role
			{
			get; set;
			}
		/// <summary>
		/// Stores the email protocol field for member patch.
		/// </summary>
		[JsonPropertyName ("email"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Email
			{
			get; set;
			}
		}

	private static string IdText (long id) => id > 0 ? id.ToString (CultureInfo.InvariantCulture) : throw new ArgumentOutOfRangeException (nameof (id));
	/// <summary>
	/// Reads a home-management snapshot, including rooms, permissions and display preferences.
	/// </summary>
	/// <param name="homeId">The positive cloud home identifier.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the typed home details result.</returns>
	/// <exception cref="RainPointException">Invalid home or room identity. The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.InvalidOperationException">Session changed during home read.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task<RainPointHomeDetails> GetHomeAsync (long homeId, CancellationToken cancellationToken = default)
		{
		Session session = GetSession ();
		var data = await GetAsync<HomeDetailsResponse> ("app/member/appHome/get?hid=" + IdText (homeId), cancellationToken).ConfigureAwait (false);
		if (data.Id != homeId || (data.Rooms?.Any (r => r is null || r.Id <= 0 || (r.HomeId.HasValue && r.HomeId != homeId)) ?? false)
		 || (data.Rooms?.Select (r => r.Id).Distinct ().Count () ?? 0) != (data.Rooms?.Count ?? 0))
			throw new RainPointException ("Invalid home or room identity.");
		if (!ReferenceEquals (session, GetSession ()))
			throw new InvalidOperationException ("Session changed during home read.");
		return new (data, session);
		}
	/// <summary>
	/// Lists members and their reported privileges for a home.
	/// </summary>
	/// <param name="homeId">The positive cloud home identifier.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the requested member records.</returns>
	/// <exception cref="RainPointException">Invalid member identity. The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task<IReadOnlyList<RainPointMember>> GetMembersAsync (long homeId, CancellationToken cancellationToken = default)
		{
		var data = await GetAsync<List<MemberResponse>> ("app/member/appHome/member/list?hid=" + IdText (homeId), cancellationToken).ConfigureAwait (false);
		if (data.Any (m => m is null || m.Id <= 0 || (m.HomeId.HasValue && m.HomeId != homeId)) || data.Select (m => m.Id).Distinct ().Count () != data.Count)
			throw new RainPointException ("Invalid member identity.");
		foreach (var member in data)
			member.HomeId = homeId;
		return Array.AsReadOnly (data.Select (m => new RainPointMember (m)).ToArray ());
		}
	/// <summary>
	/// Lists the pending home invitations visible to the signed-in account.
	/// </summary>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the requested invitation records.</returns>
	/// <exception cref="RainPointException">Invalid invitation identity. The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.InvalidOperationException">Session changed during invitation read.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task<IReadOnlyList<RainPointInvitation>> GetInvitationsAsync (CancellationToken cancellationToken = default)
		{
		Session session = GetSession ();
		var data = await GetAsync<List<InvitationResponse>> ("app/member/appHome/be/invite/list", cancellationToken).ConfigureAwait (false);
		if (data.Any (i => i is null || i.Id <= 0 || i.HomeId <= 0) || data.Select (i => i.Id).Distinct ().Count () != data.Count)
			throw new RainPointException ("Invalid invitation identity.");
		if (!ReferenceEquals (session, GetSession ()))
			throw new InvalidOperationException ("Session changed during invitation read.");
		return Array.AsReadOnly (data.Select (i => new RainPointInvitation (i, session)).ToArray ());
		}
	/// <summary>
	/// Changes a home's display name using a current home-management observation.
	/// </summary>
	/// <param name="expected">An unused, current home details observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="name">The nonempty display name to assign.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task RenameHomeAsync (RainPointHomeDetails expected, string name, CancellationToken cancellationToken = default)
		{
		RequireText (name, nameof (name));
		return WriteHomeAsync (expected, "app/member/appHome/update", new HomePatch { Id = expected?.Id ?? 0, Name = name }, cancellationToken);
		}
	/// <summary>
	/// Changes app display preferences while preserving unrelated home settings and encoded flags.
	/// </summary>
	/// <param name="expected">An unused, current home details observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="units">The desired app display preferences; typed client readings retain their documented units.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="System.ArgumentNullException">A required argument is null.</exception>
	/// <exception cref="System.InvalidOperationException">Display units are missing or unsupported.</exception>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task SetHomeDisplayUnitsAsync (RainPointHomeDetails expected, RainPointDisplayUnits units, CancellationToken cancellationToken = default)
		{
		if (expected is null)
			throw new ArgumentNullException (nameof (expected));
		if (units is null)
			throw new ArgumentNullException (nameof (units));
		if (expected.DisplayUnits is null)
			throw new InvalidOperationException ("Display units are missing or unsupported.");
		return WriteHomeAsync (expected, "app/member/appHome/update", new HomePatch { Id = expected.Id, Units = units.Encode (expected.Wire.Units!) }, cancellationToken);
		}
	/// <summary>
	/// Sets the home's coordinates for location-dependent features such as weather.
	/// </summary>
	/// <param name="expected">An unused, current home details observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="latitude">Latitude in decimal degrees, from -90 through 90.</param>
	/// <param name="longitude">Longitude in decimal degrees, from -180 through 180.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="System.ArgumentOutOfRangeException">An argument is outside the supported range described above.</exception>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task SetHomeLocationAsync (RainPointHomeDetails expected, decimal latitude, decimal longitude, CancellationToken cancellationToken = default)
		{
		if (latitude is < -90 or > 90)
			throw new ArgumentOutOfRangeException (nameof (latitude));
		if (longitude is < -180 or > 180)
			throw new ArgumentOutOfRangeException (nameof (longitude));
		return WriteHomeAsync (expected, "app/member/appHome/update", new HomePatch { Id = expected?.Id ?? 0, Latitude = (int)(latitude * 1000000), Longitude = (int)(longitude * 1000000), UpdateTemporaryPosition = false }, cancellationToken);
		}
	/// <summary>
	/// Creates a named room in the observed home.
	/// </summary>
	/// <param name="expected">An unused, current home details observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="name">The nonempty display name to assign.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task CreateRoomAsync (RainPointHomeDetails expected, string name, CancellationToken cancellationToken = default)
		{
		RequireText (name, nameof (name));
		return WriteHomeAsync (expected, "app/member/appHome/room/create", new RoomPatch { HomeId = expected?.Id ?? 0, Name = name }, cancellationToken);
		}
	/// <summary>
	/// Renames an existing room in the observed home.
	/// </summary>
	/// <param name="expected">An unused, current home details observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="roomId">The positive identifier of a room in the observed home.</param>
	/// <param name="name">The nonempty display name to assign.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task RenameRoomAsync (RainPointHomeDetails expected, long roomId, string name, CancellationToken cancellationToken = default)
		{
		RequireText (name, nameof (name));
		RequireRoom (expected, roomId);
		return WriteHomeAsync (expected, "app/member/appHome/room/update", new RoomPatch { HomeId = expected.Id, Id = roomId, Name = name }, cancellationToken);
		}
	/// <summary>Deletes the selected room. This is explicit and never automatically retried.</summary>
	/// <param name="expected">An unused, current home details observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="roomId">The positive identifier of a room in the observed home.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public Task DeleteRoomAsync (RainPointHomeDetails expected, long roomId, CancellationToken cancellationToken = default)
		{
		RequireRoom (expected, roomId);
		return WriteHomeAsync (expected, "app/member/appHome/room/delete", new RoomPatch { HomeId = expected.Id, Id = roomId }, cancellationToken);
		}
	private static void RequireRoom (RainPointHomeDetails expected, long roomId)
		{
		if (expected is null)
			throw new ArgumentNullException (nameof (expected));
		if (roomId <= 0 || !expected.Rooms.Any (r => r.Id == roomId))
			throw new ArgumentException ("Select a room from this home observation.", nameof (roomId));
		}
	/// <summary>Accepts or declines a discovered invitation. Changes membership and never retries.</summary>
	/// <param name="expected">An unused, current invitation observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="accept">True to accept the invitation; false to decline it.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="System.ArgumentNullException">A required argument is null.</exception>
	/// <exception cref="System.InvalidOperationException">Read invitations in the current session. Read invitations again after an attempt. Invitation or session changed.</exception>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task RespondToInvitationAsync (RainPointInvitation expected, bool accept, CancellationToken cancellationToken = default)
		{
		if (expected is null)
			throw new ArgumentNullException (nameof (expected));
		Session session = GetSession ();
		if (!ReferenceEquals (expected.Session, session))
			throw new InvalidOperationException ("Read invitations in the current session.");
		if (Volatile.Read (ref expected.Attempted) != 0)
			throw new InvalidOperationException ("Read invitations again after an attempt.");
		var current = await GetInvitationsAsync (cancellationToken).ConfigureAwait (false);
		if (!current.Any (i => i.Id == expected.Id && i.HomeId == expected.HomeId) || !ReferenceEquals (session, GetSession ()))
			throw new InvalidOperationException ("Invitation or session changed.");
		cancellationToken.ThrowIfCancellationRequested ();
		if (Interlocked.CompareExchange (ref expected.Attempted, 1, 0) != 0)
			throw new InvalidOperationException ("Read invitations again after an attempt.");
		await PostAdministrationAsync ("app/member/appHome/invite/accept", new InvitationDecision { Id = expected.Id, Accept = accept ? 1 : 0 }, session, cancellationToken).ConfigureAwait (false);
		}
	private async Task WriteHomeAsync<T> (RainPointHomeDetails expected, string path, T request, CancellationToken token) where T : class
		{
		Session session = await ClaimHomeWriteAsync (expected, token).ConfigureAwait (false);
		await PostAdministrationAsync (path, request, session, token).ConfigureAwait (false);
		}
	private async Task<Session> ClaimHomeWriteAsync (RainPointHomeDetails expected, CancellationToken token)
		{
		if (expected is null)
			throw new ArgumentNullException (nameof (expected));
		Session session = GetSession ();
		if (!ReferenceEquals (expected.Session, session) || Volatile.Read (ref expected.Attempted) != 0)
			throw new InvalidOperationException ("Read the home again before writing.");
		var current = await GetHomeAsync (expected.Id, token).ConfigureAwait (false);
		if (!ReferenceEquals (session, GetSession ()) || JsonSerializer.Serialize (expected.Wire, _json) != JsonSerializer.Serialize (current.Wire, _json))
			throw new InvalidOperationException ("Home settings or session changed. Reload before writing.");
		token.ThrowIfCancellationRequested ();
		if (Interlocked.CompareExchange (ref expected.Attempted, 1, 0) != 0)
			throw new InvalidOperationException ("Read the home again before writing.");
		return session;
		}

	private async Task PostAdministrationAsync<T> (string path, T request, Session session, CancellationToken token) where T : class
		{
		var result = await SendAsync<T, ApiResult> (HttpMethod.Post, path, request, session, token).ConfigureAwait (false);
		CheckResult (result, session);
		}
	private sealed class HomePatch
		{
		/// <summary>
		/// Stores the hid protocol field for home patch.
		/// </summary>
		[JsonPropertyName ("hid")]
		public long Id
			{
			get; set;
			}
		/// <summary>
		/// Stores the homeName protocol field for home patch.
		/// </summary>
		[JsonPropertyName ("homeName"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Name
			{
			get; set;
			}
		/// <summary>
		/// Stores the unit protocol field for home patch.
		/// </summary>
		[JsonPropertyName ("unit"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Units
			{
			get; set;
			}
		/// <summary>
		/// Stores the lat protocol field for home patch.
		/// </summary>
		[JsonPropertyName ("lat"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public int? Latitude
			{
			get; set;
			}
		/// <summary>
		/// Stores the lon protocol field for home patch.
		/// </summary>
		[JsonPropertyName ("lon"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public int? Longitude
			{
			get; set;
			}
		/// <summary>
		/// Stores the updateTempPosition protocol field for home patch.
		/// </summary>
		[JsonPropertyName ("updateTempPosition"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public bool? UpdateTemporaryPosition
			{
			get; set;
			}
		}
	private sealed class RoomPatch
		{
		/// <summary>
		/// Stores the hid protocol field for room patch.
		/// </summary>
		[JsonPropertyName ("hid")]
		public long HomeId
			{
			get; set;
			}
		/// <summary>
		/// Stores the rid protocol field for room patch.
		/// </summary>
		[JsonPropertyName ("rid"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public long? Id
			{
			get; set;
			}
		/// <summary>
		/// Stores the roomName protocol field for room patch.
		/// </summary>
		[JsonPropertyName ("roomName"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Name
			{
			get; set;
			}
		/// <summary>
		/// Stores the devices protocol field for room patch.
		/// </summary>
		[JsonPropertyName ("devices"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Devices
			{
			get; set;
			}
		}
	private sealed class InvitationDecision
		{
		/// <summary>
		/// Stores the id protocol field for invitation decision.
		/// </summary>
		[JsonPropertyName ("id")]
		public long Id
			{
			get; set;
			}
		/// <summary>
		/// Stores the acceptFlag protocol field for invitation decision.
		/// </summary>
		[JsonPropertyName ("acceptFlag")]
		public int Accept
			{
			get; set;
			}
		}
	}
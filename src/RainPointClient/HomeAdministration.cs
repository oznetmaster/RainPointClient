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

public enum RainPointMemberRole
	{
	Member = 0, Administrator = 1
	}
public enum RainPointPressureUnit
	{
	Pascal = 0, InchesOfMercury = 1, MillimetresOfMercury = 2
	}
/// <summary>Display units only. Client readings retain their explicitly documented units.</summary>
public sealed class RainPointDisplayUnits
	{
	public bool TwelveHourClock
		{
		get; set;
		}
	public bool Fahrenheit
		{
		get; set;
		}
	public bool ImperialLength
		{
		get; set;
		}
	public bool ImperialVolume
		{
		get; set;
		}
	public RainPointDateFormat? DateFormat
		{
		get; set;
		}
	public RainPointPressureUnit Pressure
		{
		get; set;
		}
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
	internal static RainPointDateFormat? DecodeDateFormat (string encoded) => encoded.Length >= 4 && byte.TryParse (encoded.Substring (2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte format) && Enum.IsDefined (typeof (RainPointDateFormat), (int)format) ? (RainPointDateFormat)format : null;
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
	internal RainPointHomeDetails (HomeDetailsResponse wire, object session)
		{
		Wire = wire;
		Session = session;
		CalendarTimeZone = RainPointCalendarTimeZone.Decode (wire.Offset, wire.DaylightTransitions);
		Rooms = Array.AsReadOnly ((wire.Rooms ?? []).Select (r => new RainPointRoom (r)).ToArray ());
		}
	internal HomeDetailsResponse Wire
		{
		get;
		}
	internal object Session
		{
		get;
		}
	internal int Attempted;
	public long Id => Wire.Id;
	public string Name => Wire.Name;
	public string? TimeZoneName => Wire.ZoneName;
	public int? TimeZoneOffsetMinutes => Wire.Offset;
	/// <summary>Null when the reported calendar offset/transition data is missing or malformed.</summary>
	public RainPointCalendarTimeZone? CalendarTimeZone
		{
		get;
		}
	public bool? IsOwner => Wire.Owner is 0 or 1 ? Wire.Owner == 1 : null;
	public RainPointMemberRole? Role => Wire.Rights is 0 or 1 ? (RainPointMemberRole)Wire.Rights : null;
	public decimal? Latitude => Wire.Latitude is >= -90000000 and <= 90000000 ? Wire.Latitude / 1000000m : null;
	public decimal? Longitude => Wire.Longitude is >= -180000000 and <= 180000000 ? Wire.Longitude / 1000000m : null;
	public RainPointDisplayUnits? DisplayUnits => RainPointDisplayUnits.Decode (Wire.Units);
	public int? CurrencyCode => Wire.Currency;
	public IReadOnlyList<RainPointRoom> Rooms
		{
		get;
		}
	}
public sealed class RainPointRoom
	{
	internal RainPointRoom (RoomResponse wire)
		{
		Wire = wire;
		}
	internal RoomResponse Wire
		{
		get;
		}
	public long Id => Wire.Id;
	public string Name => Wire.Name;
	}
public sealed class RainPointMember
	{
	internal RainPointMember (MemberResponse wire)
		{
		Wire = wire;
		}
	internal MemberResponse Wire
		{
		get;
		}
	public long Id => Wire.Id;
	public string? Name => Wire.Name;
	public string? Email => Wire.Email;
	public bool? IsOwner => Wire.Owner is 0 or 1 ? Wire.Owner == 1 : null;
	public RainPointMemberRole? Role => Wire.Rights is 0 or 1 ? (RainPointMemberRole)Wire.Rights : null;
	}
public sealed class RainPointInvitation
	{
	internal RainPointInvitation (InvitationResponse wire, object session)
		{
		Wire = wire;
		Session = session;
		}
	internal InvitationResponse Wire
		{
		get;
		}
	internal object Session
		{
		get;
		}
	internal int Attempted;
	public long Id => Wire.Id;
	public long HomeId => Wire.HomeId;
	public string HomeName => Wire.HomeName;
	}
public sealed partial class RainPointCloudClient
	{

	/// <summary>Creates a home with the specified IANA time-zone name and room names. No retry is performed.</summary>
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
	public Task DeleteHomeAsync (RainPointHomeDetails expected, CancellationToken cancellationToken = default) => WriteHomeAsync (expected, "app/member/appHome/delete", new HomePatch { Id = expected?.Id ?? 0 }, cancellationToken);
	/// <summary>Leaves a shared home.</summary>
	public Task LeaveHomeAsync (RainPointHomeDetails expected, CancellationToken cancellationToken = default) => WriteHomeAsync (expected, "app/member/appHome/quit", new HomePatch { Id = expected?.Id ?? 0 }, cancellationToken);
	/// <summary>Sends a home invitation to an already registered email account. Caller must explicitly intend to send it.</summary>
	public Task InviteMemberAsync (RainPointHomeDetails expected, string email, CancellationToken cancellationToken = default)
		{
		RequireText (email, nameof (email));
		var parsed = new System.Net.Mail.MailAddress (email);
		if (parsed.Address != email)
			throw new ArgumentException ("Use an email address without a display name.", nameof (email));
		return WriteHomeAsync (expected, "app/member/appHome/invite/create", new MemberPatch { HomeId = expected?.Id ?? 0, Email = email }, cancellationToken);
		}
	public Task SetMemberRoleAsync (RainPointHomeDetails expectedHome, RainPointMember expectedMember, RainPointMemberRole role, CancellationToken cancellationToken = default)
		{
		if (!Enum.IsDefined (typeof (RainPointMemberRole), role))
			throw new ArgumentOutOfRangeException (nameof (role));
		return WriteMemberAsync (expectedHome, expectedMember, "app/member/appHome/member/right/update", new MemberPatch { HomeId = expectedHome?.Id ?? 0, UserId = expectedMember?.Id, Role = (int)role }, cancellationToken);
		}
	public Task RemoveMemberAsync (RainPointHomeDetails expectedHome, RainPointMember expectedMember, CancellationToken cancellationToken = default) => WriteMemberAsync (expectedHome, expectedMember, "app/member/appHome/member/delete", new MemberPatch { HomeId = expectedHome?.Id ?? 0, UserId = expectedMember?.Id }, cancellationToken);
	/// <summary>Transfers home ownership to the selected member. This changes the caller's privileges.</summary>
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
		[JsonPropertyName ("homeName")] public string Name { get; set; } = string.Empty;
		[JsonPropertyName ("zoneName")] public string TimeZone { get; set; } = string.Empty;
		[JsonPropertyName ("rooms")] public string[] Rooms { get; set; } = [];
		}
	private sealed class MemberPatch
		{
		[JsonPropertyName ("hid")]
		public long HomeId
			{
			get; set;
			}
		[JsonPropertyName ("uid"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public long? UserId
			{
			get; set;
			}
		[JsonPropertyName ("targetUid"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public long? TargetUserId
			{
			get; set;
			}
		[JsonPropertyName ("rightCode"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public int? Role
			{
			get; set;
			}
		[JsonPropertyName ("email"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Email
			{
			get; set;
			}
		}

	private static string IdText (long id) => id > 0 ? id.ToString (CultureInfo.InvariantCulture) : throw new ArgumentOutOfRangeException (nameof (id));
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
	public async Task<IReadOnlyList<RainPointMember>> GetMembersAsync (long homeId, CancellationToken cancellationToken = default)
		{
		var data = await GetAsync<List<MemberResponse>> ("app/member/appHome/member/list?hid=" + IdText (homeId), cancellationToken).ConfigureAwait (false);
		if (data.Any (m => m is null || m.Id <= 0 || (m.HomeId.HasValue && m.HomeId != homeId)) || data.Select (m => m.Id).Distinct ().Count () != data.Count)
			throw new RainPointException ("Invalid member identity.");
		foreach (var member in data)
			member.HomeId = homeId;
		return Array.AsReadOnly (data.Select (m => new RainPointMember (m)).ToArray ());
		}
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
	public Task RenameHomeAsync (RainPointHomeDetails expected, string name, CancellationToken cancellationToken = default)
		{
		RequireText (name, nameof (name));
		return WriteHomeAsync (expected, "app/member/appHome/update", new HomePatch { Id = expected?.Id ?? 0, Name = name }, cancellationToken);
		}
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
	public Task SetHomeLocationAsync (RainPointHomeDetails expected, decimal latitude, decimal longitude, CancellationToken cancellationToken = default)
		{
		if (latitude is < -90 or > 90)
			throw new ArgumentOutOfRangeException (nameof (latitude));
		if (longitude is < -180 or > 180)
			throw new ArgumentOutOfRangeException (nameof (longitude));
		return WriteHomeAsync (expected, "app/member/appHome/update", new HomePatch { Id = expected?.Id ?? 0, Latitude = (int)(latitude * 1000000), Longitude = (int)(longitude * 1000000), UpdateTemporaryPosition = false }, cancellationToken);
		}
	public Task CreateRoomAsync (RainPointHomeDetails expected, string name, CancellationToken cancellationToken = default)
		{
		RequireText (name, nameof (name));
		return WriteHomeAsync (expected, "app/member/appHome/room/create", new RoomPatch { HomeId = expected?.Id ?? 0, Name = name }, cancellationToken);
		}
	public Task RenameRoomAsync (RainPointHomeDetails expected, long roomId, string name, CancellationToken cancellationToken = default)
		{
		RequireText (name, nameof (name));
		RequireRoom (expected, roomId);
		return WriteHomeAsync (expected, "app/member/appHome/room/update", new RoomPatch { HomeId = expected.Id, Id = roomId, Name = name }, cancellationToken);
		}
	/// <summary>Deletes the selected room. This is explicit and never automatically retried.</summary>
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
		[JsonPropertyName ("hid")]
		public long Id
			{
			get; set;
			}
		[JsonPropertyName ("homeName"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Name
			{
			get; set;
			}
		[JsonPropertyName ("unit"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Units
			{
			get; set;
			}
		[JsonPropertyName ("lat"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public int? Latitude
			{
			get; set;
			}
		[JsonPropertyName ("lon"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public int? Longitude
			{
			get; set;
			}
		[JsonPropertyName ("updateTempPosition"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public bool? UpdateTemporaryPosition
			{
			get; set;
			}
		}
	private sealed class RoomPatch
		{
		[JsonPropertyName ("hid")]
		public long HomeId
			{
			get; set;
			}
		[JsonPropertyName ("rid"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public long? Id
			{
			get; set;
			}
		[JsonPropertyName ("roomName"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Name
			{
			get; set;
			}
		[JsonPropertyName ("devices"), JsonIgnore (Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Devices
			{
			get; set;
			}
		}
	private sealed class InvitationDecision
		{
		[JsonPropertyName ("id")]
		public long Id
			{
			get; set;
			}
		[JsonPropertyName ("acceptFlag")]
		public int Accept
			{
			get; set;
			}
		}
	}
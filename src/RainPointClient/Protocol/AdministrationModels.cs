using System.Collections.Generic;
using System.Text.Json.Serialization;
namespace RainPointClient.Protocol;

internal sealed class HomeDetailsResponse
	{
	[JsonPropertyName ("hid")]
	public long Id
		{
		get; set;
		}
	[JsonPropertyName ("homeName")] public string Name { get; set; } = string.Empty;
	[JsonPropertyName ("homeVersion")]
	public long? Version
		{
		get; set;
		}
	[JsonPropertyName ("owner")]
	public int? Owner
		{
		get; set;
		}
	[JsonPropertyName ("rightCode")]
	public int? Rights
		{
		get; set;
		}
	[JsonPropertyName ("zoneName")]
	public string? ZoneName
		{
		get; set;
		}
	[JsonPropertyName ("zoneDst")]
	public string? DaylightTransitions
		{
		get; set;
		}
	[JsonPropertyName ("zoneOffset")]
	public int? Offset
		{
		get; set;
		}
	[JsonPropertyName ("lat")]
	public int? Latitude
		{
		get; set;
		}
	[JsonPropertyName ("lon")]
	public int? Longitude
		{
		get; set;
		}
	[JsonPropertyName ("unit")]
	public string? Units
		{
		get; set;
		}
	[JsonPropertyName ("currency")]
	public int? Currency
		{
		get; set;
		}
	[JsonPropertyName ("rooms")]
	public List<RoomResponse>? Rooms
		{
		get; set;
		}
	}
internal sealed class RoomResponse
	{
	[JsonPropertyName ("rid")]
	public long Id
		{
		get; set;
		}
	[JsonPropertyName ("hid")]
	public long? HomeId
		{
		get; set;
		}
	[JsonPropertyName ("roomName")] public string Name { get; set; } = string.Empty;
	[JsonPropertyName ("devices")]
	public string? Devices
		{
		get; set;
		}
	}
internal sealed class MemberResponse
	{
	[JsonPropertyName ("uid")]
	public long Id
		{
		get; set;
		}
	[JsonPropertyName ("hid")]
	public long? HomeId
		{
		get; set;
		}
	[JsonPropertyName ("nickname")]
	public string? Name
		{
		get; set;
		}
	[JsonPropertyName ("email")]
	public string? Email
		{
		get; set;
		}
	[JsonPropertyName ("owner")]
	public int? Owner
		{
		get; set;
		}
	[JsonPropertyName ("rightCode")]
	public int? Rights
		{
		get; set;
		}
	}
internal sealed class InvitationResponse
	{
	[JsonPropertyName ("id")]
	public long Id
		{
		get; set;
		}
	[JsonPropertyName ("hid")]
	public long HomeId
		{
		get; set;
		}
	[JsonPropertyName ("homeName")] public string HomeName { get; set; } = string.Empty;
	[JsonPropertyName ("createTime")]
	public long? Created
		{
		get; set;
		}
	}
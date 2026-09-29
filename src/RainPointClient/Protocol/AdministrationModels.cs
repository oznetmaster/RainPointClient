// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System.Collections.Generic;
using System.Text.Json.Serialization;
namespace RainPointClient.Protocol;

/// <summary>
/// Internal home details response representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class HomeDetailsResponse
	{
	/// <summary>
	/// Stores the hid protocol field for home details response.
	/// </summary>
	[JsonPropertyName ("hid")]
	public long Id
		{
		get; set;
		}
	/// <summary>
	/// Stores the homeName protocol field for home details response.
	/// </summary>
	[JsonPropertyName ("homeName")] public string Name { get; set; } = string.Empty;
	/// <summary>
	/// Stores the homeVersion protocol field for home details response.
	/// </summary>
	[JsonPropertyName ("homeVersion")]
	public long? Version
		{
		get; set;
		}
	/// <summary>
	/// Gets the client identity that owns this verification result.
	/// </summary>
	[JsonPropertyName ("owner")]
	public int? Owner
		{
		get; set;
		}
	/// <summary>
	/// Stores the rightCode protocol field for home details response.
	/// </summary>
	[JsonPropertyName ("rightCode")]
	public int? Rights
		{
		get; set;
		}
	/// <summary>
	/// Stores the zoneName protocol field for home details response.
	/// </summary>
	[JsonPropertyName ("zoneName")]
	public string? ZoneName
		{
		get; set;
		}
	/// <summary>
	/// Stores the zoneDst protocol field for home details response.
	/// </summary>
	[JsonPropertyName ("zoneDst")]
	public string? DaylightTransitions
		{
		get; set;
		}
	/// <summary>
	/// Stores the zoneOffset protocol field for home details response.
	/// </summary>
	[JsonPropertyName ("zoneOffset")]
	public int? Offset
		{
		get; set;
		}
	/// <summary>
	/// Stores the lat protocol field for home details response.
	/// </summary>
	[JsonPropertyName ("lat")]
	public int? Latitude
		{
		get; set;
		}
	/// <summary>
	/// Stores the lon protocol field for home details response.
	/// </summary>
	[JsonPropertyName ("lon")]
	public int? Longitude
		{
		get; set;
		}
	/// <summary>
	/// Stores the unit protocol field for home details response.
	/// </summary>
	[JsonPropertyName ("unit")]
	public string? Units
		{
		get; set;
		}
	/// <summary>
	/// Stores the currency protocol field for home details response.
	/// </summary>
	[JsonPropertyName ("currency")]
	public int? Currency
		{
		get; set;
		}
	/// <summary>
	/// Stores the rooms protocol field for home details response.
	/// </summary>
	[JsonPropertyName ("rooms")]
	public List<RoomResponse>? Rooms
		{
		get; set;
		}
	}
/// <summary>
/// Internal room response representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class RoomResponse
	{
	/// <summary>
	/// Stores the rid protocol field for room response.
	/// </summary>
	[JsonPropertyName ("rid")]
	public long Id
		{
		get; set;
		}
	/// <summary>
	/// Stores the hid protocol field for room response.
	/// </summary>
	[JsonPropertyName ("hid")]
	public long? HomeId
		{
		get; set;
		}
	/// <summary>
	/// Stores the roomName protocol field for room response.
	/// </summary>
	[JsonPropertyName ("roomName")] public string Name { get; set; } = string.Empty;
	/// <summary>
	/// Stores the devices protocol field for room response.
	/// </summary>
	[JsonPropertyName ("devices")]
	public string? Devices
		{
		get; set;
		}
	}
/// <summary>
/// Internal member response representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class MemberResponse
	{
	/// <summary>
	/// Stores the uid protocol field for member response.
	/// </summary>
	[JsonPropertyName ("uid")]
	public long Id
		{
		get; set;
		}
	/// <summary>
	/// Stores the hid protocol field for member response.
	/// </summary>
	[JsonPropertyName ("hid")]
	public long? HomeId
		{
		get; set;
		}
	/// <summary>
	/// Stores the nickname protocol field for member response.
	/// </summary>
	[JsonPropertyName ("nickname")]
	public string? Name
		{
		get; set;
		}
	/// <summary>
	/// Stores the email protocol field for member response.
	/// </summary>
	[JsonPropertyName ("email")]
	public string? Email
		{
		get; set;
		}
	/// <summary>
	/// Gets the client identity that owns this verification result.
	/// </summary>
	[JsonPropertyName ("owner")]
	public int? Owner
		{
		get; set;
		}
	/// <summary>
	/// Stores the rightCode protocol field for member response.
	/// </summary>
	[JsonPropertyName ("rightCode")]
	public int? Rights
		{
		get; set;
		}
	}
/// <summary>
/// Internal invitation response representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class InvitationResponse
	{
	/// <summary>
	/// Stores the id protocol field for invitation response.
	/// </summary>
	[JsonPropertyName ("id")]
	public long Id
		{
		get; set;
		}
	/// <summary>
	/// Stores the hid protocol field for invitation response.
	/// </summary>
	[JsonPropertyName ("hid")]
	public long HomeId
		{
		get; set;
		}
	/// <summary>
	/// Stores the homeName protocol field for invitation response.
	/// </summary>
	[JsonPropertyName ("homeName")] public string HomeName { get; set; } = string.Empty;
	/// <summary>
	/// Stores the createTime protocol field for invitation response.
	/// </summary>
	[JsonPropertyName ("createTime")]
	public long? Created
		{
		get; set;
		}
	}
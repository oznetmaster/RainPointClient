// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RainPointClient.Protocol;

/// <summary>
/// Internal usage bucket response representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class UsageBucketResponse
	{
	/// <summary>
	/// Stores the ymd protocol field for usage bucket response.
	/// </summary>
	[JsonPropertyName ("ymd")]
	public int? Day
		{
		get; set;
		}
	/// <summary>
	/// Stores the ym protocol field for usage bucket response.
	/// </summary>
	[JsonPropertyName ("ym")]
	public int? Month
		{
		get; set;
		}
	/// <summary>
	/// Stores the val protocol field for usage bucket response.
	/// </summary>
	[JsonPropertyName ("val")]
	public decimal? Value
		{
		get; set;
		}
	}

/// <summary>
/// Internal event response representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class EventResponse
	{
	/// <summary>
	/// Stores the eid protocol field for event response.
	/// </summary>
	[JsonPropertyName ("eid"), JsonRequired] public string Id { get; set; } = string.Empty;
	/// <summary>
	/// Stores the mid protocol field for event response.
	/// </summary>
	[JsonPropertyName ("mid"), JsonRequired]
	public long HubId
		{
		get; set;
		}
	/// <summary>
	/// Stores the addr protocol field for event response.
	/// </summary>
	[JsonPropertyName ("addr"), JsonRequired]
	public int Address
		{
		get; set;
		}
	/// <summary>
	/// Stores the port protocol field for event response.
	/// </summary>
	[JsonPropertyName ("port"), JsonRequired]
	public int Zone
		{
		get; set;
		}
	/// <summary>
	/// Stores the verified email code for one matching account operation.
	/// </summary>
	[JsonPropertyName ("code"), JsonRequired]
	public int Code
		{
		get; set;
		}
	/// <summary>
	/// Stores the timestamp protocol field for event response.
	/// </summary>
	[JsonPropertyName ("timestamp"), JsonRequired]
	public long Timestamp
		{
		get; set;
		}
	/// <summary>
	/// Stores the time protocol field for event response.
	/// </summary>
	[JsonPropertyName ("time")]
	public string? Time
		{
		get; set;
		}
	/// <summary>
	/// Stores the timezone protocol field for event response.
	/// </summary>
	[JsonPropertyName ("timezone")]
	public string? TimeZone
		{
		get; set;
		}
	/// <summary>
	/// Stores the rule protocol field for event response.
	/// </summary>
	[JsonPropertyName ("rule")]
	public List<EventRuleResponse>? Rules
		{
		get; set;
		}
	}

/// <summary>
/// Internal event rule response representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class EventRuleResponse
	{
	/// <summary>
	/// Stores the type protocol field for event rule response.
	/// </summary>
	[JsonPropertyName ("type")]
	public string? Type
		{
		get; set;
		}
	/// <summary>
	/// Stores the value protocol field for event rule response.
	/// </summary>
	[JsonPropertyName ("value")]
	public string? Value
		{
		get; set;
		}
	}
// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RainPointClient.Protocol;

internal sealed class UsageBucketResponse
	{
	[JsonPropertyName ("ymd")]
	public int? Day
		{
		get; set;
		}
	[JsonPropertyName ("ym")]
	public int? Month
		{
		get; set;
		}
	[JsonPropertyName ("val")]
	public decimal? Value
		{
		get; set;
		}
	}

internal sealed class EventResponse
	{
	[JsonPropertyName ("eid"), JsonRequired] public string Id { get; set; } = string.Empty;
	[JsonPropertyName ("mid"), JsonRequired]
	public long HubId
		{
		get; set;
		}
	[JsonPropertyName ("addr"), JsonRequired]
	public int Address
		{
		get; set;
		}
	[JsonPropertyName ("port"), JsonRequired]
	public int Zone
		{
		get; set;
		}
	[JsonPropertyName ("code"), JsonRequired]
	public int Code
		{
		get; set;
		}
	[JsonPropertyName ("timestamp"), JsonRequired]
	public long Timestamp
		{
		get; set;
		}
	[JsonPropertyName ("time")]
	public string? Time
		{
		get; set;
		}
	[JsonPropertyName ("timezone")]
	public string? TimeZone
		{
		get; set;
		}
	[JsonPropertyName ("rule")]
	public List<EventRuleResponse>? Rules
		{
		get; set;
		}
	}

internal sealed class EventRuleResponse
	{
	[JsonPropertyName ("type")]
	public string? Type
		{
		get; set;
		}
	[JsonPropertyName ("value")]
	public string? Value
		{
		get; set;
		}
	}
// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol/compatibility reference: https://github.com/funkadelic/ha-rainpoint
// Additional reference: https://github.com/brettmeyerowitz/homeassistant-homgar
// Independently written C# implementation. See ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RainPointClient.Protocol;

internal class ApiResult
	{
	[JsonPropertyName ("code"), JsonRequired]
	public int Code
		{
		get; set;
		}
	}

internal sealed class ApiResult<T> : ApiResult
	{
	[JsonPropertyName ("data")]
	public T? Data
		{
		get; set;
		}
	}

internal sealed class LoginRequest
	{
	[JsonPropertyName ("areaCode")]
	public string AreaCode { get; set; } = string.Empty;

	[JsonPropertyName ("phoneOrEmail")]
	public string Email { get; set; } = string.Empty;

	[JsonPropertyName ("password")]
	public string PasswordHash { get; set; } = string.Empty;

	[JsonPropertyName ("deviceId")]
	public string DeviceId { get; set; } = string.Empty;
	}

internal sealed class LoginUser
	{
	[JsonPropertyName ("uid")]
	public long? Id
		{
		get; set;
		}
	[JsonPropertyName ("email")]
	public string? Email
		{
		get; set;
		}
	[JsonPropertyName ("nickname")]
	public string? Nickname
		{
		get; set;
		}
	[JsonPropertyName ("photo")]
	public string? Photo
		{
		get; set;
		}
	[JsonPropertyName ("lang")]
	public string? Language
		{
		get; set;
		}

	[JsonPropertyName ("notice")]
	public int? Notice
		{
		get; set;
		}
	}

internal sealed class LoginResponse
	{
	[JsonPropertyName ("user")]
	public LoginUser? User
		{
		get; set;
		}
	[JsonPropertyName ("refreshToken")]
	public string? RefreshToken
		{
		get; set;
		}

	[JsonPropertyName ("token")]
	public string? Token
		{
		get; set;
		}

	[JsonPropertyName ("tokenExpired")]
	public long ExpiresInSeconds
		{
		get; set;
		}
	}

internal sealed class BatchStatusRequest
	{
	[JsonPropertyName ("devices")]
	public HubAddress[] Devices { get; set; } = [];
	}

internal sealed class HubAddress
	{
	[JsonPropertyName ("mid")]
	public long Id
		{
		get; set;
		}

	[JsonPropertyName ("deviceName")]
	public string DeviceName { get; set; } = string.Empty;

	[JsonPropertyName ("productKey")]
	public string ProductKey { get; set; } = string.Empty;
	}

internal sealed class BatchHubStatusResponse
	{
	[JsonPropertyName ("mid"), JsonRequired]
	public long Id
		{
		get; set;
		}

	[JsonPropertyName ("status"), JsonRequired]
	public List<DeviceStatusResponse> Devices
		{
		get; set;
		} = [];
	}

internal sealed class DeviceStatusResponse
	{
	[JsonPropertyName ("id")]
	public string? Id
		{
		get; set;
		}

	[JsonPropertyName ("value")]
	public string? Value
		{
		get; set;
		}

	[JsonPropertyName ("time")]
	public long? Time
		{
		get; set;
		}
	}

internal sealed class ValveCommand
	{
	[JsonPropertyName ("mid")]
	public long HubId
		{
		get; set;
		}

	[JsonPropertyName ("addr")]
	public int Address
		{
		get; set;
		}

	[JsonPropertyName ("deviceName")]
	public string DeviceName { get; set; } = string.Empty;

	[JsonPropertyName ("productKey")]
	public string ProductKey { get; set; } = string.Empty;

	[JsonPropertyName ("port")]
	public int Zone
		{
		get; set;
		}

	[JsonPropertyName ("mode")]
	public int Mode
		{
		get; set;
		}

	[JsonPropertyName ("duration")]
	public int Duration
		{
		get; set;
		}

	[JsonPropertyName ("param")]
	public string Parameter { get; set; } = string.Empty;
	}

internal sealed class RefreshRequest
	{
	[JsonPropertyName ("refreshToken")]
	public string RefreshToken { get; set; } = string.Empty;
	}

internal sealed class EmptyRequest;

internal sealed class HubParameterRequest
	{
	[JsonPropertyName ("mid")]
	public long Id
		{
		get; set;
		}

	[JsonPropertyName ("param")]
	public string Parameter { get; set; } = string.Empty;
	}
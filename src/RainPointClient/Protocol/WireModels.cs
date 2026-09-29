// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol/compatibility reference: https://github.com/funkadelic/ha-rainpoint
// Additional reference: https://github.com/brettmeyerowitz/homeassistant-homgar
// Independently written C# implementation. See ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RainPointClient.Protocol;

/// <summary>
/// Internal api result representation or processing contract for the RainPoint protocol.
/// </summary>
internal class ApiResult
	{
	/// <summary>
	/// Stores the verified email code for one matching account operation.
	/// </summary>
	[JsonPropertyName ("code"), JsonRequired]
	public int Code
		{
		get; set;
		}
	}

/// <summary>
/// Internal api result representation or processing contract for the RainPoint protocol.
/// </summary>
/// <typeparam name="T">The typed payload carried by this protocol result.</typeparam>
internal sealed class ApiResult<T> : ApiResult
	{
	/// <summary>
	/// Stores the data protocol field for api result.
	/// </summary>
	[JsonPropertyName ("data")]
	public T? Data
		{
		get; set;
		}
	}

/// <summary>
/// Internal login request representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class LoginRequest
	{
	/// <summary>
	/// Stores the areaCode protocol field for login request.
	/// </summary>
	[JsonPropertyName ("areaCode")]
	public string AreaCode { get; set; } = string.Empty;

	/// <summary>
	/// Stores the phoneOrEmail protocol field for login request.
	/// </summary>
	[JsonPropertyName ("phoneOrEmail")]
	public string Email { get; set; } = string.Empty;

	/// <summary>
	/// Stores the password protocol field for login request.
	/// </summary>
	[JsonPropertyName ("password")]
	public string PasswordHash { get; set; } = string.Empty;

	/// <summary>
	/// Stores the deviceId protocol field for login request.
	/// </summary>
	[JsonPropertyName ("deviceId")]
	public string DeviceId { get; set; } = string.Empty;
	}

/// <summary>
/// Internal login user representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class LoginUser
	{
	/// <summary>
	/// Stores the deviceName protocol field for login user.
	/// </summary>
	[JsonPropertyName ("deviceName")]
	public string? DeviceName
		{
		get; set;
		}
	/// <summary>
	/// Stores the productKey protocol field for login user.
	/// </summary>
	[JsonPropertyName ("productKey")]
	public string? ProductKey
		{
		get; set;
		}
	/// <summary>
	/// Stores the deviceSecret protocol field for login user.
	/// </summary>
	[JsonPropertyName ("deviceSecret")]
	public string? DeviceSecret
		{
		get; set;
		}
	/// <summary>
	/// Stores the uid protocol field for login user.
	/// </summary>
	[JsonPropertyName ("uid")]
	public long? Id
		{
		get; set;
		}
	/// <summary>
	/// Stores the email protocol field for login user.
	/// </summary>
	[JsonPropertyName ("email")]
	public string? Email
		{
		get; set;
		}
	/// <summary>
	/// Stores the nickname protocol field for login user.
	/// </summary>
	[JsonPropertyName ("nickname")]
	public string? Nickname
		{
		get; set;
		}
	/// <summary>
	/// Stores the photo protocol field for login user.
	/// </summary>
	[JsonPropertyName ("photo")]
	public string? Photo
		{
		get; set;
		}
	/// <summary>
	/// Stores the lang protocol field for login user.
	/// </summary>
	[JsonPropertyName ("lang")]
	public string? Language
		{
		get; set;
		}

	/// <summary>
	/// Stores the notice protocol field for login user.
	/// </summary>
	[JsonPropertyName ("notice")]
	public int? Notice
		{
		get; set;
		}
	}

/// <summary>
/// Internal login response representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class LoginResponse
	{
	/// <summary>
	/// Stores the user protocol field for login response.
	/// </summary>
	[JsonPropertyName ("user")]
	public LoginUser? User
		{
		get; set;
		}
	/// <summary>
	/// Stores the refreshToken protocol field for login response.
	/// </summary>
	[JsonPropertyName ("refreshToken")]
	public string? RefreshToken
		{
		get; set;
		}

	/// <summary>
	/// Stores the token protocol field for login response.
	/// </summary>
	[JsonPropertyName ("token")]
	public string? Token
		{
		get; set;
		}

	/// <summary>
	/// Stores the tokenExpired protocol field for login response.
	/// </summary>
	[JsonPropertyName ("tokenExpired")]
	public long ExpiresInSeconds
		{
		get; set;
		}
	}

/// <summary>
/// Internal batch status request representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class BatchStatusRequest
	{
	/// <summary>
	/// Stores the devices protocol field for batch status request.
	/// </summary>
	[JsonPropertyName ("devices")]
	public HubAddress[] Devices { get; set; } = [];
	}

/// <summary>
/// Internal hub address representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class HubAddress
	{
	/// <summary>
	/// Stores the mid protocol field for hub address.
	/// </summary>
	[JsonPropertyName ("mid")]
	public long Id
		{
		get; set;
		}

	/// <summary>
	/// Stores the deviceName protocol field for hub address.
	/// </summary>
	[JsonPropertyName ("deviceName")]
	public string DeviceName { get; set; } = string.Empty;

	/// <summary>
	/// Stores the productKey protocol field for hub address.
	/// </summary>
	[JsonPropertyName ("productKey")]
	public string ProductKey { get; set; } = string.Empty;
	}

/// <summary>
/// Internal batch hub status response representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class BatchHubStatusResponse
	{
	/// <summary>
	/// Stores the mid protocol field for batch hub status response.
	/// </summary>
	[JsonPropertyName ("mid"), JsonRequired]
	public long Id
		{
		get; set;
		}

	/// <summary>
	/// Stores the status protocol field for batch hub status response.
	/// </summary>
	[JsonPropertyName ("status"), JsonRequired]
	public List<DeviceStatusResponse> Devices
		{
		get; set;
		} = [];
	}

/// <summary>
/// Internal device status response representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class DeviceStatusResponse
	{
	/// <summary>
	/// Stores the id protocol field for device status response.
	/// </summary>
	[JsonPropertyName ("id")]
	public string? Id
		{
		get; set;
		}

	/// <summary>
	/// Stores the value protocol field for device status response.
	/// </summary>
	[JsonPropertyName ("value")]
	public string? Value
		{
		get; set;
		}

	/// <summary>
	/// Stores the time protocol field for device status response.
	/// </summary>
	[JsonPropertyName ("time")]
	public long? Time
		{
		get; set;
		}
	}

/// <summary>
/// Internal valve command representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class ValveCommand
	{
	/// <summary>
	/// Stores the mid protocol field for valve command.
	/// </summary>
	[JsonPropertyName ("mid")]
	public long HubId
		{
		get; set;
		}

	/// <summary>
	/// Stores the addr protocol field for valve command.
	/// </summary>
	[JsonPropertyName ("addr")]
	public int Address
		{
		get; set;
		}

	/// <summary>
	/// Stores the deviceName protocol field for valve command.
	/// </summary>
	[JsonPropertyName ("deviceName")]
	public string DeviceName { get; set; } = string.Empty;

	/// <summary>
	/// Stores the productKey protocol field for valve command.
	/// </summary>
	[JsonPropertyName ("productKey")]
	public string ProductKey { get; set; } = string.Empty;

	/// <summary>
	/// Stores the port protocol field for valve command.
	/// </summary>
	[JsonPropertyName ("port")]
	public int Zone
		{
		get; set;
		}

	/// <summary>
	/// Stores the mode protocol field for valve command.
	/// </summary>
	[JsonPropertyName ("mode")]
	public int Mode
		{
		get; set;
		}

	/// <summary>
	/// Stores the duration protocol field for valve command.
	/// </summary>
	[JsonPropertyName ("duration")]
	public int Duration
		{
		get; set;
		}

	/// <summary>
	/// Stores the original encoded configuration field for bounded decoding and guarded updates.
	/// </summary>
	[JsonPropertyName ("param")]
	public string Parameter { get; set; } = string.Empty;
	}

/// <summary>
/// Internal refresh request representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class RefreshRequest
	{
	/// <summary>
	/// Stores the refreshToken protocol field for refresh request.
	/// </summary>
	[JsonPropertyName ("refreshToken")]
	public string RefreshToken { get; set; } = string.Empty;
	}

/// <summary>
/// Internal empty request representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class EmptyRequest;

/// <summary>
/// Internal hub parameter request representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class HubParameterRequest
	{
	/// <summary>
	/// Stores the mid protocol field for hub parameter request.
	/// </summary>
	[JsonPropertyName ("mid")]
	public long Id
		{
		get; set;
		}

	/// <summary>
	/// Stores the original encoded configuration field for bounded decoding and guarded updates.
	/// </summary>
	[JsonPropertyName ("param")]
	public string Parameter { get; set; } = string.Empty;
	}
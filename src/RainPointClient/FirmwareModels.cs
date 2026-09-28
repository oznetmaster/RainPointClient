// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System.Text.Json.Serialization;

namespace RainPointClient;

/// <summary>A read-only firmware check. This client does not install firmware.</summary>
public sealed class RainPointFirmwareStatus
	{
	[JsonPropertyName ("softVer"), JsonRequired, JsonInclude]
	public string InstalledVersion { get; internal set; } = string.Empty;

	/// <summary>Null means the cloud reports no available update.</summary>
	[JsonPropertyName ("info"), JsonRequired, JsonInclude]
	public RainPointFirmwareOffer? AvailableUpdate
		{
		get; internal set;
		}
	}

public sealed class RainPointFirmwareOffer
	{
	[JsonPropertyName ("versionName"), JsonRequired, JsonInclude]
	public string Version { get; internal set; } = string.Empty;

	[JsonPropertyName ("modelCode"), JsonInclude]
	public int? ModelCode
		{
		get; internal set;
		}

	[JsonPropertyName ("mark"), JsonInclude]
	public string? ReleaseNotes
		{
		get; internal set;
		}
	}
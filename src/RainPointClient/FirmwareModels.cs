// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System.Text.Json.Serialization;

namespace RainPointClient;

/// <summary>A read-only firmware check. This client does not install firmware.</summary>
public sealed class RainPointFirmwareStatus
	{
	/// <summary>
	/// Gets the reported installed firmware version.
	/// </summary>
	/// <value>The reported installed firmware version.</value>
	[JsonPropertyName ("softVer"), JsonRequired, JsonInclude]
	public string InstalledVersion { get; internal set; } = string.Empty;

	/// <summary>Null means the cloud reports no available update.</summary>
	/// <value>Null means the cloud reports no available update.</value>
	[JsonPropertyName ("info"), JsonRequired, JsonInclude]
	public RainPointFirmwareOffer? AvailableUpdate
		{
		get; internal set;
		}
	}

/// <summary>
/// Describes a firmware version advertised by the cloud; the client does not install it.
/// </summary>
public sealed class RainPointFirmwareOffer
	{
	/// <summary>
	/// Gets the version string reported for this firmware or catalog record.
	/// </summary>
	/// <value>The version string reported for this firmware or catalog record.</value>
	[JsonPropertyName ("versionName"), JsonRequired, JsonInclude]
	public string Version { get; internal set; } = string.Empty;

	/// <summary>
	/// Gets the numeric vendor model code associated with the firmware offer.
	/// </summary>
	/// <value>The numeric vendor model code associated with the firmware offer.</value>
	[JsonPropertyName ("modelCode"), JsonInclude]
	public int? ModelCode
		{
		get; internal set;
		}

	/// <summary>
	/// Gets the firmware release notes supplied by the vendor.
	/// </summary>
	/// <value>The firmware release notes supplied by the vendor.</value>
	[JsonPropertyName ("mark"), JsonInclude]
	public string? ReleaseNotes
		{
		get; internal set;
		}
	}
// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
namespace RainPointClient;

public sealed class RainPointTimeZone
	{
	[JsonPropertyName ("zone"), JsonInclude] public string Name { get; internal set; } = string.Empty;
	[JsonPropertyName ("country"), JsonInclude]
	public string? Country
		{
		get; internal set;
		}
	}
public sealed partial class RainPointCloudClient
	{
	/// <summary>Reads the vendor's IANA time-zone catalog independently of Windows time-zone identifiers.</summary>
	public async Task<IReadOnlyList<RainPointTimeZone>> GetTimeZonesAsync (CancellationToken cancellationToken = default)
		{
		var data = await GetAsync<TimeZoneCatalog> ("app/common/core/timezone?version=0", cancellationToken).ConfigureAwait (false);
		if (data.Zones is null || data.Zones.Any (z => z is null || string.IsNullOrWhiteSpace (z.Name)))
			throw new RainPointException ("The time-zone catalog is incomplete.");
		return Array.AsReadOnly (data.Zones.ToArray ());
		}
	/// <summary>Changes the home time zone. This changes the interpretation of scheduled local times.</summary>
	public async Task SetHomeTimeZoneAsync (RainPointHomeDetails expected, string timeZoneName, CancellationToken cancellationToken = default)
		{
		if (expected is null)
			throw new ArgumentNullException (nameof (expected));
		RequireText (timeZoneName, nameof (timeZoneName));
		if (!(await GetTimeZonesAsync (cancellationToken).ConfigureAwait (false)).Any (z => z.Name == timeZoneName))
			throw new ArgumentException ("Select an IANA time-zone name from the vendor catalog.", nameof (timeZoneName));
		await WriteHomeAsync (expected, "app/member/appHome/update", new TimeZonePatch { HomeId = expected.Id, Name = timeZoneName }, cancellationToken).ConfigureAwait (false);
		}
	private sealed class TimeZoneCatalog
		{
		[JsonPropertyName ("zone")]
		public List<RainPointTimeZone>? Zones
			{
			get; set;
			}
		}
	private sealed class TimeZonePatch
		{
		[JsonPropertyName ("hid")]
		public long HomeId
			{
			get; set;
			}
		[JsonPropertyName ("zoneName")] public string Name { get; set; } = string.Empty;
		}
	}
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

/// <summary>
/// Describes an IANA time zone from the vendor's home time-zone catalog.
/// </summary>
public sealed class RainPointTimeZone
	{
	/// <summary>
	/// Gets the display name supplied for this record.
	/// </summary>
	[JsonPropertyName ("zone"), JsonInclude] public string Name { get; internal set; } = string.Empty;
	/// <summary>
	/// Gets the country code associated with this catalog time zone.
	/// </summary>
	[JsonPropertyName ("country"), JsonInclude]
	public string? Country
		{
		get; internal set;
		}
	}
public sealed partial class RainPointCloudClient
	{
	/// <summary>Reads the vendor's IANA time-zone catalog independently of Windows time-zone identifiers.</summary>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the requested time zone records.</returns>
	/// <exception cref="RainPointException">The time-zone catalog is incomplete. The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task<IReadOnlyList<RainPointTimeZone>> GetTimeZonesAsync (CancellationToken cancellationToken = default)
		{
		var data = await GetAsync<TimeZoneCatalog> ("app/common/core/timezone?version=0", cancellationToken).ConfigureAwait (false);
		if (data.Zones is null || data.Zones.Any (z => z is null || string.IsNullOrWhiteSpace (z.Name)))
			throw new RainPointException ("The time-zone catalog is incomplete.");
		return Array.AsReadOnly (data.Zones.ToArray ());
		}
	/// <summary>Changes the home time zone. This changes the interpretation of scheduled local times.</summary>
	/// <param name="expected">An unused, current home details observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="timeZoneName">The IANA time-zone name reported in the vendor catalog.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="System.ArgumentNullException">A required argument is null.</exception>
	/// <exception cref="System.ArgumentException">Select an IANA time-zone name from the vendor catalog.</exception>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
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
		/// <summary>
		/// Stores the zone protocol field for time zone catalog.
		/// </summary>
		[JsonPropertyName ("zone")]
		public List<RainPointTimeZone>? Zones
			{
			get; set;
			}
		}
	private sealed class TimeZonePatch
		{
		/// <summary>
		/// Stores the hid protocol field for time zone patch.
		/// </summary>
		[JsonPropertyName ("hid")]
		public long HomeId
			{
			get; set;
			}
		/// <summary>
		/// Stores the zoneName protocol field for time zone patch.
		/// </summary>
		[JsonPropertyName ("zoneName")] public string Name { get; set; } = string.Empty;
		}
	}
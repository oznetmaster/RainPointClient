// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using RainPointClient.Protocol;

namespace RainPointClient;

public sealed partial class RainPointCloudClient
	{
	/// <summary>Changes the hub RF receive channel (1..3). May interrupt communication with paired devices.</summary>
	/// <remarks>Uses a discovered hub as the expected channel/identity snapshot, reads again before writing,
	/// and consumes that snapshot after one write attempt. Read again after any attempted write.
	/// Completion is cloud acceptance, not RF delivery; concurrent updates are not atomic.</remarks>
	public async Task SetRfChannelAsync (RainPointHub hub, int channel, CancellationToken cancellationToken = default)
		{
		ValidateHub (hub);
		if (channel is < 1 or > 3)
			throw new ArgumentOutOfRangeException (nameof (channel), "RF channel must be 1, 2 or 3.");
		if (hub.HomeId <= 0 || hub.RfChannel is not (>= 1 and <= 3))
			throw new ArgumentException ("Use a discovered hub with a known RF channel and home.", nameof (hub));
		cancellationToken.ThrowIfCancellationRequested ();
		if (Volatile.Read (ref hub.RfChannelWriteAttempted) != 0)
			throw new InvalidOperationException ("This channel snapshot has already been used. Read again before another write.");
		RainPointHub[] matches = (await GetHubsAsync (hub.HomeId, cancellationToken).ConfigureAwait (false)).Where (h => h.Id == hub.Id).ToArray ();
		if (matches.Length != 1 || matches[0].DeviceName != hub.DeviceName || matches[0].ProductKey != hub.ProductKey
		 || matches[0].Model != hub.Model || matches[0].RfChannel != hub.RfChannel)
			throw new InvalidOperationException ("Hub identity or RF channel changed. Read again before writing.");
		if (channel == hub.RfChannel)
			return;
		if (Interlocked.CompareExchange (ref hub.RfChannelWriteAttempted, 1, 0) != 0)
			throw new InvalidOperationException ("This channel snapshot has already been used. Read again before another write.");
		Session session = GetSession ();
		ApiResult result = await SendAsync<RfChannelRequest, ApiResult> (HttpMethod.Post, "app/device/main/update",
		 new RfChannelRequest { Id = hub.Id, Channel = channel }, session, cancellationToken).ConfigureAwait (false);
		CheckResult (result, session);
		}
	private sealed class RfChannelRequest
		{
		[JsonPropertyName ("mid")]
		public long Id
			{
			get; set;
			}
		[JsonPropertyName ("recich")]
		public int Channel
			{
			get; set;
			}
		}
	}
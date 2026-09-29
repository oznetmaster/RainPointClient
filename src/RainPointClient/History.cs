// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using RainPointClient.Protocol;

namespace RainPointClient;

public sealed partial class RainPointCloudClient
	{
	/// <summary>Reads sparse daily or monthly water totals for one timer zone. Dates are inclusive home-calendar dates, not UTC instants.</summary>
	/// <remarks>Use Unspecified midnight dates. Requests are limited to 30 days or one calendar year, matching the app's chart windows. Monthly buckets can cover a partial month; no missing periods are filled.</remarks>
	/// <param name="hub">A hub discovered through its home in the current account; its child list identifies valid RF addresses and models.</param>
	/// <param name="address">The paired child's RF address within its hub, distinct from its cloud database ID.</param>
	/// <param name="zone">The one-based zone number on the selected timer.</param>
	/// <param name="period">Daily or monthly aggregation of water usage.</param>
	/// <param name="startDate">The inclusive first home-calendar date at midnight with Unspecified kind.</param>
	/// <param name="endDate">The inclusive final home-calendar date at midnight with Unspecified kind.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the requested water usage records.</returns>
	/// <exception cref="System.ArgumentOutOfRangeException">An argument is outside the supported range described above.</exception>
	/// <exception cref="System.ArgumentException">Use an ordered home-calendar range of at most 30 days or one year, with Unspecified midnight dates.</exception>
	/// <exception cref="RainPointException">Water history contained an invalid, duplicate or out-of-range calendar bucket. The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task<IReadOnlyList<RainPointWaterUsage>> GetTimerWaterUsageAsync (RainPointHub hub, int address, int zone,
	 RainPointUsagePeriod period, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
		{
		_ = GetTimer (hub, address);
		if (zone is < 1 or > 3)
			{
			throw new ArgumentOutOfRangeException (nameof (zone));
			}
		if (period is not RainPointUsagePeriod.Day and not RainPointUsagePeriod.Month)
			{
			throw new ArgumentOutOfRangeException (nameof (period));
			}
		if (startDate.Kind != DateTimeKind.Unspecified || endDate.Kind != DateTimeKind.Unspecified
		 || startDate.TimeOfDay != TimeSpan.Zero || endDate.TimeOfDay != TimeSpan.Zero || startDate.Year < 1970 || startDate > endDate
		 || (period == RainPointUsagePeriod.Day ? (endDate - startDate).TotalDays > 29 : startDate < endDate.AddYears (-1)))
			{
			throw new ArgumentException ("Use an ordered home-calendar range of at most 30 days or one year, with Unspecified midnight dates.");
			}
		string path = "app/iot/log/waterAmount/" + (period == RainPointUsagePeriod.Day ? "day" : "month") + "/list?code=0&mid="
		 + Number (hub.Id) + "&addr=" + Number (address) + "&port=" + Number (zone)
		 + "&startDate=" + startDate.ToString ("yyyyMMdd", CultureInfo.InvariantCulture) + "&endDate=" + endDate.ToString ("yyyyMMdd", CultureInfo.InvariantCulture);
		List<UsageBucketResponse> rows = await GetAsync<List<UsageBucketResponse>> (path, cancellationToken).ConfigureAwait (false);
		List<RainPointWaterUsage> result = [];
		HashSet<DateTime> seen = [];
		DateTime first = period == RainPointUsagePeriod.Day ? startDate : new DateTime (startDate.Year, startDate.Month, 1);
		DateTime last = period == RainPointUsagePeriod.Day ? endDate : new DateTime (endDate.Year, endDate.Month, 1);
		foreach (UsageBucketResponse row in rows)
			{
			int? value = period == RainPointUsagePeriod.Day ? row?.Day : row?.Month;
			string format = period == RainPointUsagePeriod.Day ? "yyyyMMdd" : "yyyyMM";
			if (!value.HasValue || !DateTime.TryParseExact (Number (value.Value), format, CultureInfo.InvariantCulture,
			 DateTimeStyles.None, out DateTime date) || date < first || date > last || !seen.Add (date))
				{
				throw new RainPointException ("Water history contained an invalid, duplicate or out-of-range calendar bucket.");
				}
			result.Add (new RainPointWaterUsage (date, row!.Value is >= 0 ? row.Value / 10m : null));
			}
		return result.OrderBy (row => row.PeriodStart).ToList ().AsReadOnly ();
		}

	/// <summary>Reads one event page for a home, with optional hub, address, zone, code and cloud-timestamp filters. Zone queries may include timer-wide power-on events with Zone=0.</summary>
	/// <remarks>No automatic pagination or deduplication. An empty result does not establish the service's retention period.</remarks>
	/// <param name="homeId">The positive cloud home identifier.</param>
	/// <param name="query">Optional history filters and a bounded result limit; null uses the default query.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the typed event page result.</returns>
	/// <exception cref="System.ArgumentException">Use a positive home/hub, nonnegative address/zone/code, ordered bounds and a limit of 1–50. Address requires hub; zone requires address.</exception>
	/// <exception cref="RainPointException">Event history exceeded the requested page limit. Event history contained invalid metadata or mismatched addressing/filter codes. The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task<RainPointEventPage> GetEventsAsync (long homeId, RainPointEventQuery? query = null, CancellationToken cancellationToken = default)
		{
		query ??= new RainPointEventQuery ();
		long? hub = query.HubId;
		int? address = query.Address, zone = query.Zone, code = query.Code;
		int limit = query.Limit;
		DateTimeOffset? begin = query.Begin, end = query.End;
		if (homeId <= 0 || hub is <= 0 || address is < 0 || zone is < 0 || code is < 0 || limit is < 1 or > 50
		 || (address.HasValue && !hub.HasValue) || (zone.HasValue && !address.HasValue)
		 || (begin.HasValue && begin.Value.ToUnixTimeMilliseconds () < 0) || (end.HasValue && end.Value.ToUnixTimeMilliseconds () < 0)
		 || (begin.HasValue && end.HasValue && begin.Value >= end.Value))
			{
			throw new ArgumentException ("Use a positive home/hub, nonnegative address/zone/code, ordered bounds and a limit of 1–50. Address requires hub; zone requires address.");
			}
		string path = "app/device/event/list?hid=" + Number (homeId) + "&size=" + Number (limit);
		if (hub.HasValue)
			{
			path += "&mid=" + Number (hub.Value);
			}
		if (address.HasValue)
			{
			path += "&addr=" + Number (address.Value);
			}
		if (zone.HasValue)
			{
			path += "&port=" + Number (zone.Value);
			}
		if (code.HasValue)
			{
			path += "&code=" + Number (code.Value);
			}
		if (begin.HasValue)
			{
			path += "&begin=" + Number (begin.Value.ToUnixTimeMilliseconds ());
			}
		if (end.HasValue)
			{
			path += "&end=" + Number (end.Value.ToUnixTimeMilliseconds ());
			}
		List<EventResponse> rows = await GetAsync<List<EventResponse>> (path, cancellationToken).ConfigureAwait (false);
		if (rows.Count > limit)
			{
			throw new RainPointException ("Event history exceeded the requested page limit.");
			}
		List<RainPointEvent> events = [];
		foreach (EventResponse row in rows)
			{
			if (row is null || string.IsNullOrWhiteSpace (row.Id) || row.HubId <= 0 || row.Address < 0 || row.Zone < 0 || row.Code < 0
			 || row.Timestamp < 0 || row.Timestamp > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds ()
			 || (hub.HasValue && row.HubId != hub.Value) || (address.HasValue && row.Address != address.Value)
			 || (zone.HasValue && row.Zone != zone.Value && !(row.Zone == 0 && row.Code == 5)) || (code.HasValue && row.Code != code.Value))
				{
				throw new RainPointException ("Event history contained invalid metadata or mismatched addressing/filter codes.");
				}
			events.Add (HistoryDecoder.Decode (row));
			}
		return new RainPointEventPage (events.AsReadOnly (), rows.Count == limit);
		}

	private static string Number (long value) => value.ToString (CultureInfo.InvariantCulture);
	}
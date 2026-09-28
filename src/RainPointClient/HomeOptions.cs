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
/// <summary>Vendor display formats; client dates and units remain independent of these presentation preferences.</summary>
public enum RainPointDateFormat
	{
	MonthSlashDayYear = 0, MonthDashDayYear = 1, YearDashMonthDay = 3, YearDotMonthDay = 4, YearSlashMonthDay = 5,
	DaySlashMonthYear = 8, DayDotMonthYear = 9, DayDashMonthYear = 10, DaySpaceMonthYear = 11, MonthNameDayYear = 12, DayMonthNameYear = 13
	}
public sealed class RainPointCurrency
	{
	[JsonPropertyName ("code"), JsonRequired, JsonInclude]
	public int Code
		{
		get; internal set;
		}
	[JsonPropertyName ("name"), JsonRequired, JsonInclude] public string Name { get; internal set; } = string.Empty;
	}
public sealed class RainPointWeatherType
	{
	[JsonPropertyName ("code"), JsonRequired, JsonInclude]
	public int Code
		{
		get; internal set;
		}
	[JsonPropertyName ("name"), JsonRequired, JsonInclude] public string Name { get; internal set; } = string.Empty;
	}
public sealed class RainPointHomeOptions
	{
	internal RainPointHomeOptions (IReadOnlyList<RainPointCurrency> currencies, IReadOnlyList<RainPointWeatherType> weather)
		{
		Currencies = currencies;
		WeatherTypes = weather;
		}
	public IReadOnlyList<RainPointCurrency> Currencies
		{
		get;
		}
	public IReadOnlyList<RainPointWeatherType> WeatherTypes
		{
		get;
		}
	}
public sealed partial class RainPointCloudClient
	{
	public async Task<RainPointHomeOptions> GetHomeOptionsAsync (CancellationToken cancellationToken = default)
		{
		var data = await GetAsync<HomeOptionsWire> ("app/common/core/dict", cancellationToken).ConfigureAwait (false);
		if (data.Currencies is null || data.Weather is null || data.Currencies.Any (c => c is null || c.Code < 0 || string.IsNullOrWhiteSpace (c.Name)) || data.Weather.Any (w => w is null || w.Code is < 0 or > 31 || string.IsNullOrWhiteSpace (w.Name))
		 || data.Currencies.Select (c => c.Code).Distinct ().Count () != data.Currencies.Count || data.Weather.Select (w => w.Code).Distinct ().Count () != data.Weather.Count)
			throw new RainPointException ("Home option catalog is missing or ambiguous.");
		return new (Array.AsReadOnly (data.Currencies.ToArray ()), Array.AsReadOnly (data.Weather.ToArray ()));
		}
	public async Task SetHomeCurrencyAsync (RainPointHomeDetails expected, RainPointCurrency currency, CancellationToken cancellationToken = default)
		{
		if (expected is null || currency is null)
			throw new ArgumentNullException (nameof (expected));
		var options = await GetHomeOptionsAsync (cancellationToken).ConfigureAwait (false);
		if (!options.Currencies.Any (c => c.Code == currency.Code && c.Name == currency.Name))
			throw new InvalidOperationException ("The selected currency is no longer in the catalog.");
		await WriteHomeAsync (expected, "app/member/appHome/update", new CurrencyPatch { HomeId = expected.Id, Currency = currency.Code }, cancellationToken).ConfigureAwait (false);
		}
	private sealed class HomeOptionsWire
		{
		[JsonPropertyName ("currency")]
		public List<RainPointCurrency>? Currencies
			{
			get; set;
			}
		[JsonPropertyName ("smartWeather")]
		public List<RainPointWeatherType>? Weather
			{
			get; set;
			}
		}
	private sealed class CurrencyPatch
		{
		[JsonPropertyName ("hid")]
		public long HomeId
			{
			get; set;
			}
		[JsonPropertyName ("currency")]
		public int Currency
			{
			get; set;
			}
		}
	}
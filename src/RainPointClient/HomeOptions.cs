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
	/// <summary>
	/// The app date layout uses month slash day year.
	/// </summary>
	MonthSlashDayYear = 0,
	/// <summary>
	/// The app date layout uses month dash day year.
	/// </summary>
	MonthDashDayYear = 1,
	/// <summary>
	/// The app date layout uses year dash month day.
	/// </summary>
	YearDashMonthDay = 3,
	/// <summary>
	/// The app date layout uses year dot month day.
	/// </summary>
	YearDotMonthDay = 4,
	/// <summary>
	/// The app date layout uses year slash month day.
	/// </summary>
	YearSlashMonthDay = 5,
	/// <summary>
	/// The app date layout uses day slash month year.
	/// </summary>
	DaySlashMonthYear = 8,
	/// <summary>
	/// The app date layout uses day dot month year.
	/// </summary>
	DayDotMonthYear = 9,
	/// <summary>
	/// The app date layout uses day dash month year.
	/// </summary>
	DayDashMonthYear = 10,
	/// <summary>
	/// The app date layout uses day space month year.
	/// </summary>
	DaySpaceMonthYear = 11,
	/// <summary>
	/// The app date layout uses month name day year.
	/// </summary>
	MonthNameDayYear = 12,
	/// <summary>
	/// The app date layout uses day month name year.
	/// </summary>
	DayMonthNameYear = 13
	}
/// <summary>
/// Describes a currency offered by the vendor home-options catalog.
/// </summary>
public sealed class RainPointCurrency
	{
	/// <summary>
	/// Gets the vendor catalog code for this option.
	/// </summary>
	[JsonPropertyName ("code"), JsonRequired, JsonInclude]
	public int Code
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the display name supplied for this record.
	/// </summary>
	[JsonPropertyName ("name"), JsonRequired, JsonInclude] public string Name { get; internal set; } = string.Empty;
	}
/// <summary>
/// Describes a weather condition offered by the vendor options catalog.
/// </summary>
public sealed class RainPointWeatherType
	{
	/// <summary>
	/// Gets the vendor catalog code for this option.
	/// </summary>
	[JsonPropertyName ("code"), JsonRequired, JsonInclude]
	public int Code
		{
		get; internal set;
		}
	/// <summary>
	/// Gets the display name supplied for this record.
	/// </summary>
	[JsonPropertyName ("name"), JsonRequired, JsonInclude] public string Name { get; internal set; } = string.Empty;
	}
/// <summary>
/// Contains the vendor's currency and weather-condition choices.
/// </summary>
public sealed class RainPointHomeOptions
	{
	/// <summary>
	/// Initializes home options from the supplied typed values.
	/// </summary>
	/// <param name="currencies">The decoded vendor currency choices.</param>
	/// <param name="weather">The decoded vendor weather-condition choices.</param>
	internal RainPointHomeOptions (IReadOnlyList<RainPointCurrency> currencies, IReadOnlyList<RainPointWeatherType> weather)
		{
		Currencies = currencies;
		WeatherTypes = weather;
		}
	/// <summary>
	/// Gets the supported currencies returned by the vendor catalog.
	/// </summary>
	public IReadOnlyList<RainPointCurrency> Currencies
		{
		get;
		}
	/// <summary>
	/// Gets the supported weather-condition choices returned by the vendor catalog.
	/// </summary>
	public IReadOnlyList<RainPointWeatherType> WeatherTypes
		{
		get;
		}
	}
public sealed partial class RainPointCloudClient
	{
	/// <summary>
	/// Reads the vendor's supported currency and weather-condition catalog.
	/// </summary>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task containing the typed home options result.</returns>
	/// <exception cref="RainPointException">Home option catalog is missing or ambiguous. The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
	public async Task<RainPointHomeOptions> GetHomeOptionsAsync (CancellationToken cancellationToken = default)
		{
		var data = await GetAsync<HomeOptionsWire> ("app/common/core/dict", cancellationToken).ConfigureAwait (false);
		if (data.Currencies is null || data.Weather is null || data.Currencies.Any (c => c is null || c.Code < 0 || string.IsNullOrWhiteSpace (c.Name)) || data.Weather.Any (w => w is null || w.Code is < 0 or > 31 || string.IsNullOrWhiteSpace (w.Name))
		 || data.Currencies.Select (c => c.Code).Distinct ().Count () != data.Currencies.Count || data.Weather.Select (w => w.Code).Distinct ().Count () != data.Weather.Count)
			throw new RainPointException ("Home option catalog is missing or ambiguous.");
		return new (Array.AsReadOnly (data.Currencies.ToArray ()), Array.AsReadOnly (data.Weather.ToArray ()));
		}
	/// <summary>
	/// Sets the home's currency to an entry from the vendor options catalog.
	/// </summary>
	/// <param name="expected">An unused, current home details observation from this session. Read again after any submitted write attempt.</param>
	/// <param name="currency">The selected currency entry from the vendor home-options catalog.</param>
	/// <param name="cancellationToken">Cancellation for this operation; cancelling after submission does not prove that a cloud write was undone.</param>
	/// <returns>A task that completes when the operation finishes. A successful cloud write is not physical-device confirmation.</returns>
	/// <exception cref="System.ArgumentNullException">A required argument is null.</exception>
	/// <exception cref="System.InvalidOperationException">The selected currency is no longer in the catalog.</exception>
	/// <exception cref="RainPointException">The service rejects the request or returns an unusable response.</exception>
	/// <exception cref="System.Net.Http.HttpRequestException">The HTTP transport fails.</exception>
	/// <exception cref="System.OperationCanceledException">The operation is cancelled or the HTTP request times out.</exception>
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
		/// <summary>
		/// Stores the currency protocol field for home options wire.
		/// </summary>
		[JsonPropertyName ("currency")]
		public List<RainPointCurrency>? Currencies
			{
			get; set;
			}
		/// <summary>
		/// Stores the smartWeather protocol field for home options wire.
		/// </summary>
		[JsonPropertyName ("smartWeather")]
		public List<RainPointWeatherType>? Weather
			{
			get; set;
			}
		}
	private sealed class CurrencyPatch
		{
		/// <summary>
		/// Stores the hid protocol field for currency patch.
		/// </summary>
		[JsonPropertyName ("hid")]
		public long HomeId
			{
			get; set;
			}
		/// <summary>
		/// Stores the currency protocol field for currency patch.
		/// </summary>
		[JsonPropertyName ("currency")]
		public int Currency
			{
			get; set;
			}
		}
	}
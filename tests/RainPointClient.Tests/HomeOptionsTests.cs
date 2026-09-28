using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using NUnit.Framework;
namespace RainPointClient.Tests;

[TestFixture]
public sealed class HomeOptionsTests
	{
	internal const string OPTIONS = """{"code":0,"data":{"currency":[{"code":1,"name":"GBP"}],"smartWeather":[{"code":0,"name":"Sunny"},{"code":3,"name":"Rain"}]}}""";
	[TestCase (RainPointDateFormat.MonthSlashDayYear, "0800TAIL")]
	[TestCase (RainPointDateFormat.MonthDashDayYear, "0801TAIL")]
	[TestCase (RainPointDateFormat.YearDashMonthDay, "0803TAIL")]
	[TestCase (RainPointDateFormat.YearDotMonthDay, "0804TAIL")]
	[TestCase (RainPointDateFormat.YearSlashMonthDay, "0805TAIL")]
	[TestCase (RainPointDateFormat.DaySlashMonthYear, "8808TAIL")]
	[TestCase (RainPointDateFormat.DayDotMonthYear, "8809TAIL")]
	[TestCase (RainPointDateFormat.DayDashMonthYear, "880ATAIL")]
	[TestCase (RainPointDateFormat.DaySpaceMonthYear, "880BTAIL")]
	[TestCase (RainPointDateFormat.MonthNameDayYear, "080CTAIL")]
	[TestCase (RainPointDateFormat.DayMonthNameYear, "880DTAIL")]
	public void DatePreferenceUpdatesLegacyOrderBitAndPreservesUnknownFields (RainPointDateFormat format, string expected)
		{
		var units = RainPointDisplayUnits.Decode ("88FETAIL")!;
		Assert.That (units.DateFormat, Is.Null);
		units.DateFormat = format;
		Assert.That (units.Encode ("88FETAIL"), Is.EqualTo (expected));
		Assert.That (RainPointDisplayUnits.Decode (expected)!.DateFormat, Is.EqualTo (format));
		}
	[Test]
	public void UnknownDateFormatIsPreservedWhenChangingOtherUnits ()
		{
		var units = RainPointDisplayUnits.Decode ("88FETAIL")!;
		units.Fahrenheit = true;
		Assert.That (units.Encode ("88FETAIL"), Is.EqualTo ("8AFETAIL"));
		}
	[Test]
	public async Task CurrencyWriteUsesFreshCatalogAndMinimalPatch ()
		{
		using var handler = new ScriptedHandler ();
		using var http = new HttpClient (handler, false);
		using var client = new RainPointCloudClient (http);
		handler.Reply ("""{"code":0,"data":{"token":"fixture","tokenExpired":3600}}""");
		await client.LoginAsync ("fixture@example.invalid", "fixture", "44");
		handler.Reply (AdministrationTests.HOME);
		var home = await client.GetHomeAsync (5);
		handler.Reply (OPTIONS);
		var options = await client.GetHomeOptionsAsync ();
		Assert.That (options.WeatherTypes, Has.Count.EqualTo (2));
		handler.Reply (OPTIONS);
		handler.Reply (AdministrationTests.HOME);
		handler.Reply ("{\"code\":0}");
		await client.SetHomeCurrencyAsync (home, options.Currencies.Single ());
		Assert.That (handler.Requests.Last ().Body, Is.EqualTo ("{\"hid\":5,\"currency\":1}"));
		}
	[Test]
	public void WeatherTypeBitmaskUsesCatalogIndicesAndRetainsHighBit ()
		{
		var types = new[] { new RainPointWeatherType { Code = 0, Name = "Sunny" }, new RainPointWeatherType { Code = 3, Name = "Rain" }, new RainPointWeatherType { Code = 31, Name = "Future type" } };
		var condition = RainPointSceneCondition.WeatherTypes (types);
		Assert.That (condition.Wire.Value1, Is.EqualTo ("09000080"));
		Assert.That (condition.WeatherTypeCodes, Is.EqualTo (new[] { 0, 3, 31 }));
		Assert.Throws<ArgumentException> (() => RainPointSceneCondition.WeatherTypes ([types[0], types[0]]));
		}

	}
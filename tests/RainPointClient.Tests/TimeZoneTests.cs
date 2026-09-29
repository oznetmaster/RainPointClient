// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using NUnit.Framework;
namespace RainPointClient.Tests;

[TestFixture]
public sealed class TimeZoneTests
	{
	[TestCase ("Europe/London", true)]
	[TestCase ("GMT Standard Time", false)]
	public async Task OnlyCatalogIanaNamesCanBeWritten (string name, bool valid)
		{
		using var handler = new ScriptedHandler ();
		using var http = new HttpClient (handler, false);
		using var client = new RainPointCloudClient (http);
		handler.Reply ("""{"code":0,"data":{"token":"fixture","tokenExpired":3600}}""");
		await client.LoginAsync ("fixture@example.invalid", "fixture", "44");
		handler.Reply (AdministrationTests.HOME);
		var home = await client.GetHomeAsync (5);
		handler.Reply ("""{"code":0,"data":{"version":"1","zone":[{"country":"UK","zone":"Europe/London"}]}}""");
		if (valid)
			{
			handler.Reply (AdministrationTests.HOME);
			handler.Reply ("{\"code\":0}");
			await client.SetHomeTimeZoneAsync (home, name);
			Assert.That (handler.Requests.Last ().Body, Is.EqualTo ("{\"hid\":5,\"zoneName\":\"Europe/London\"}"));
			}
		else
			{
			await Assert.ThrowsAsync<ArgumentException> (async () => await client.SetHomeTimeZoneAsync (home, name));
			Assert.That (handler.Requests, Has.Count.EqualTo (3));
			}
		}
	}
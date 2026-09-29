// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Linq;
using System.Text;
using System.Text.Json;

using NUnit.Framework;

using RainPointClient.Protocol;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class ConfigurationPushTests
	{
	internal static byte[] Frame (string body = "42|update|100", string user = "0000000123", string code = "04", bool wrapped = false)
		{
		string frame = "#P260929120000" + user + code + body + "#";
		return Encoding.UTF8.GetBytes (wrapped ? JsonSerializer.Serialize (new
			{
			method = "thing.service.property.set",
			@params = new
				{
				param = frame
				}
			}) : frame);
		}
	[TestCase (false)]
	[TestCase (true)]
	public void ConfigurationNotificationIsScopedAndSeparateFromStatus (bool wrapped)
		{
		var value = PushDecoder.DecodeConfiguration (Frame (wrapped: wrapped), 42, 123);
		Assert.That (value?.HomeId, Is.EqualTo (42));
		Assert.That (value?.Revision, Is.EqualTo (100));
		Assert.That (PushDecoder.Decode (Frame (wrapped: wrapped), PushTests.Hub (), PushTests.Now), Is.Null);
		}
	[TestCase (false, 42L, 118187304292L)]
	[TestCase (true, 42L, 118187304292L)]
	[TestCase (false, 1L, 0L)]
	[TestCase (true, 1L, 0L)]
	public void EmptyDescriptionStillInvalidatesTheMatchingHome (bool wrapped, long home, long revision)
		{
		var payload = Frame ($"{home}||{revision}", wrapped: wrapped);
		var value = PushDecoder.DecodeConfiguration (payload, home, 123);
		Assert.That (value?.HomeId, Is.EqualTo (home));
		Assert.That (value?.Revision, Is.EqualTo (revision));
		Assert.That (PushDecoder.DecodeConfiguration (payload, home + 1, 123), Is.Null);
		Assert.That (PushDecoder.DecodeConfiguration (payload, home, 456), Is.Null);
		}
	[TestCase ("43|update|100", "0000000123", "04")]
	[TestCase ("42|update|100", "0000000456", "04")]
	[TestCase ("42|update|100", "0000000123", "01")]
	[TestCase ("42|update|-1", "0000000123", "04")]
	[TestCase ("42|update|100|extra", "0000000123", "04")]
	[TestCase ("42|update|9223372036854775808", "0000000123", "04")]
	public void WrongRecipientHomeKindAndMalformedNotificationsAreIgnored (string body, string user, string code) =>
		Assert.That (PushDecoder.DecodeConfiguration (Frame (body, user, code), 42, 123), Is.Null);
	[Test]
	public void MissingAccountAndInvalidEncodingCannotInvalidateConfiguration ()
		{
		Assert.That (PushDecoder.DecodeConfiguration (Frame (), 42, null), Is.Null);
		Assert.That (PushDecoder.DecodeConfiguration (new byte[] { 0xff }, 42, 123), Is.Null);
		Assert.That (PushDecoder.DecodeConfiguration (new byte[8193], 42, 123), Is.Null);
		}
	[TestCase ("Lawn|Beds|Tap", "Lawn", "Beds", "Tap")]
	[TestCase ("|Beds|", "", "Beds", "")]
	[TestCase ("Lawn", "Lawn", "", "")]
	[TestCase (null, "", "", "")]
	[TestCase ("A & B|庭|Tap|unused", "A & B", "庭", "Tap")]
	public void AttributedZoneNamesPreservePortOrderAndMissingEntries (string? names, string one, string two, string three)
		{
		var value = JsonSerializer.Deserialize<RainPointDevice> (JsonSerializer.Serialize (new
			{
			addr = 1,
			model = "HTV345FRF",
			portDescribe = names
			}))!;
		Assert.That (value.ZoneNames, Is.EqualTo (new[] { one, two, three }));
		Assert.That (JsonSerializer.Serialize (value), Does.Not.Contain ("ZoneNames"));
		Assert.That (JsonSerializer.Deserialize<RainPointDevice> ("{\"addr\":1,\"model\":\"unknown\",\"portDescribe\":\"A|B|C\"}")!.ZoneNames, Is.Empty);
		}
	}
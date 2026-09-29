// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol/compatibility reference: https://github.com/funkadelic/ha-rainpoint
// Additional reference: https://github.com/macher91/homgar-homeassistant
// Independently written C# implementation. See ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace RainPointClient.Protocol;

internal sealed class PushEnvelope
	{
	[JsonPropertyName ("method"), JsonRequired] public string Method { get; set; } = string.Empty;
	[JsonPropertyName ("params"), JsonRequired] public PushParameters Parameters { get; set; } = new ();
	}
internal sealed class PushParameters
	{
	[JsonPropertyName ("param"), JsonRequired] public string Parameter { get; set; } = string.Empty;
	}
internal sealed class PushValue
	{
	[JsonPropertyName ("value"), JsonRequired] public string Value { get; set; } = string.Empty;
	[JsonPropertyName ("time"), JsonRequired]
	public long Time
		{
		get; set;
		}
	}
internal sealed class PushReading
	{
	internal bool? Connected
		{
		get; set;
		}
	internal DateTimeOffset? ConnectionChanged
		{
		get; set;
		}
	internal List<RainPointTimerStatus> Timers { get; } = [];
	}

internal sealed class PushTimerValues
	{
	[JsonIgnore] public Dictionary<int, PushValue?> Timers { get; } = [];
	}

internal static class PushDecoder
	{
	private static readonly JsonSerializerOptions Json = new () { AllowDuplicateProperties = false, MaxDepth = 16 };
	private static readonly Encoding Utf8 = new UTF8Encoding (false, true);

	// The official app treats command 04 as a home-configuration invalidation and rereads that home.
	// Validate recipient and home independently; never interpret these notifications as valve feedback.
	internal static RainPointConfigurationChange? DecodeConfiguration (byte[] payload, long homeId, long? accountId)
		{
		if (payload.Length is 0 or > 8192 || homeId <= 0 || accountId is not > 0)
			return null;
		try
			{
			string text = Utf8.GetString (payload).Trim ();
			if (text.StartsWith ("{", StringComparison.Ordinal))
				{
				PushEnvelope? envelope = JsonSerializer.Deserialize<PushEnvelope> (text, Json);
				if (envelope?.Method != "thing.service.property.set" || envelope.Parameters is null)
					return null;
				text = envelope.Parameters.Parameter;
				}
			if (string.IsNullOrEmpty (text) || text.Length < 31 || !text.StartsWith ("#P", StringComparison.Ordinal)
				|| !text.EndsWith ("#", StringComparison.Ordinal) || text.Substring (24, 2) != "04"
				|| text.Substring (2, 22).Any (c => c is < '0' or > '9')
				|| !long.TryParse (text.Substring (14, 10), NumberStyles.None, CultureInfo.InvariantCulture, out long recipient)
				|| recipient != accountId)
				return null;
			// The description is optional; live rename notifications contain an empty middle field.
			string[] fields = text.Substring (26, text.Length - 27).Split ('|');
			if (fields.Length != 3
				|| !long.TryParse (fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out long home) || home != homeId
				|| !long.TryParse (fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out long revision))
				return null;
			return new RainPointConfigurationChange (home, revision);
			}
		catch (Exception error) when (error is JsonException or DecoderFallbackException or ArgumentOutOfRangeException) { return null; }
		}

	internal static PushReading? Decode (byte[] payload, RainPointHub hub, DateTimeOffset now)
		{
		if (payload.Length is 0 or > 8192)
			return null;
		try
			{
			string text = Utf8.GetString (payload).Trim ();
			if (text.StartsWith ("{", StringComparison.Ordinal))
				{
				PushEnvelope? envelope = JsonSerializer.Deserialize<PushEnvelope> (text, Json);
				if (envelope?.Method != "thing.service.property.set" || envelope.Parameters is null)
					return null;
				text = envelope.Parameters.Parameter;
				}
			if (string.IsNullOrEmpty (text) || !text.EndsWith ("#", StringComparison.Ordinal))
				return null;
			string[] sections = text.Substring (0, text.Length - 1).Split ('|');
			if (sections.Length != 4 || sections[0].Length != 32 || !sections[0].StartsWith ("#P", StringComparison.Ordinal)
				 || sections[0].Substring (2).Any (c => c is < '0' or > '9')
				 || sections[0].Substring (14, 4) != "0000"
				 || !long.TryParse (sections[0].Substring (26), NumberStyles.None, CultureInfo.InvariantCulture, out long id) || id != hub.Id
				 || !long.TryParse (sections[2], NumberStyles.None, CultureInfo.InvariantCulture, out long stamp)
				 || string.IsNullOrEmpty (sections[3]) || sections[3].Any (c => c is < '0' or > '9'))
				return null;
			DateTimeOffset? changed = Timestamp (stamp, now);
			if (!changed.HasValue)
				return null;
			PushReading result = new ();
			if (sections[1] is "0" or "1")
				{
				result.Connected = sections[1] == "1";
				result.ConnectionChanged = changed;
				return result;
				}
			PushTimerValues? values = JsonSerializer.Deserialize<PushTimerValues> (sections[1], TimerOptions (hub));
			if (values is null)
				return null;
			foreach (RainPointDevice timer in hub.Devices.Where (item => item.SupportedZoneCount.HasValue))
				{
				if (!values.Timers.TryGetValue (timer.Address, out PushValue? value) || value is null)
					continue;
				DateTimeOffset? time = Timestamp (value.Time, now);
				if (!time.HasValue)
					continue;
				RainPointTimerStatus status = TimerDecoder.Decode (timer.Address, timer.SupportedZoneCount!.Value, value.Value, time);
				if (status.Availability == TimerReadingAvailability.Decoded)
					result.Timers.Add (status);
				}
			return result.Timers.Count == 0 ? null : result;
			}
		catch (Exception error) when (error is JsonException or DecoderFallbackException or ArgumentOutOfRangeException) { return null; }
		}

	private static JsonSerializerOptions TimerOptions (RainPointHub hub)
		{
		// Datapoint names depend on discovery. STJ skips unrelated metadata such as numeric update
		// flags while deserializing known timer names into attributed models; no DOM or token parser.
		DefaultJsonTypeInfoResolver resolver = new ();
		resolver.Modifiers.Add (info =>
			 {
				 if (info.Type != typeof (PushTimerValues))
					 return;
				 info.Properties.Clear ();
				 foreach (int address in hub.Devices.Where (item => item.SupportedZoneCount.HasValue).Select (item => item.Address).Distinct ())
					 {
					 JsonPropertyInfo property = info.CreateJsonPropertyInfo (typeof (PushValue), "D" + address.ToString ("D2", CultureInfo.InvariantCulture));
					 property.Set = (target, value) => ((PushTimerValues)target).Timers[address] = (PushValue?)value;
					 info.Properties.Add (property);
					 }
			 });
		return new JsonSerializerOptions { AllowDuplicateProperties = false, MaxDepth = 16, TypeInfoResolver = resolver };
		}

	internal static DateTimeOffset? Timestamp (long value, DateTimeOffset now)
		{
		if (value <= 0)
			return null;
		try
			{
			DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeMilliseconds (value);
			return timestamp > now.AddMinutes (5) ? null : timestamp;
			}
		catch (ArgumentOutOfRangeException) { return null; }
		}
	}
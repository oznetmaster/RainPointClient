// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RainPointClient.Protocol;

// The endpoint returns either data.state or a direct data string. Keep this union internal;
// object properties are deserialized through attributed models, without a JSON DOM.
/// <summary>
/// Internal command data representation or processing contract for the RainPoint protocol.
/// </summary>
[JsonConverter (typeof (CommandDataConverter))]
internal sealed class CommandData
	{
	/// <summary>
	/// Stores the reading for command data.
	/// </summary>
	internal CommandState? Reading
		{
		get; set;
		}
	/// <summary>
	/// Stores the unavailable for command data.
	/// </summary>
	internal TimerReadingAvailability? Unavailable
		{
		get; set;
		}
	}

/// <summary>
/// Internal command state representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class CommandState
	{
	/// <summary>
	/// Stores the state protocol field for command state.
	/// </summary>
	[JsonPropertyName ("state")]
	public string? State
		{
		get; set;
		}

	/// <summary>
	/// Stores the timestamp protocol field for command state.
	/// </summary>
	[JsonPropertyName ("timestamp"), JsonConverter (typeof (OptionalCommandTimestampConverter))]
	public long? Timestamp
		{
		get; set;
		}
	}

/// <summary>
/// Internal command data converter representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class CommandDataConverter : JsonConverter<CommandData>
	{
	/// <inheritdoc/>
	public override CommandData? Read (ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
		if (reader.TokenType == JsonTokenType.String)
			return new CommandData { Reading = new CommandState { State = reader.GetString () } };
		if (reader.TokenType == JsonTokenType.StartObject)
			{
			// Read a copy so an invalid optional field cannot lose the acknowledgement or auth code.
			Utf8JsonReader candidate = reader;
			try
				{
				CommandState? state = JsonSerializer.Deserialize<CommandState> (ref candidate, options);
				reader = candidate;
				return new CommandData { Reading = state };
				}
			catch (JsonException)
				{
				reader.Skip ();
				return new CommandData { Unavailable = TimerReadingAvailability.Malformed };
				}
			}
		reader.Skip ();
		return new CommandData { Unavailable = TimerReadingAvailability.UnsupportedFormat };
		}

	/// <inheritdoc/>
	public override void Write (Utf8JsonWriter writer, CommandData value, JsonSerializerOptions options) =>
		 throw new NotSupportedException ("Command response models are read-only.");
	}

/// <summary>
/// Internal optional command timestamp converter representation or processing contract for the RainPoint protocol.
/// </summary>
internal sealed class OptionalCommandTimestampConverter : JsonConverter<long?>
	{
	/// <inheritdoc/>
	public override long? Read (ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
		if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64 (out long number))
			return number;
		if (reader.TokenType == JsonTokenType.String && long.TryParse (reader.GetString (),
				  NumberStyles.Integer, CultureInfo.InvariantCulture, out long text))
			return text;
		reader.Skip ();
		return null;
		}

	/// <inheritdoc/>
	public override void Write (Utf8JsonWriter writer, long? value, JsonSerializerOptions options) =>
		 throw new NotSupportedException ("Command response models are read-only.");
	}
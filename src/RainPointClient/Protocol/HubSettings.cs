// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

namespace RainPointClient.Protocol;

/// <summary>
/// Internal hub settings representation or processing contract for the RainPoint protocol.
/// </summary>
internal static class HubSettings
	{
	/// <summary>
	/// Reads the recognized automatic time-broadcast bit from hub settings.
	/// </summary>
	/// <param name="parameter">The encoded hub-settings field, or null when not reported.</param>
	/// <returns>The reported setting, or null when the field is absent or unsupported.</returns>
	internal static bool? ReadBroadcast (string? parameter)
		{
		string[]? fields = parameter?.Split ('|');
		return fields?.Length > 1 ? fields[1] switch
			{
				"1" => true,
				"0" => false,
				_ => null
				} : null;
		}

	/// <summary>
	/// Changes the recognized automatic time-broadcast bit while preserving unrelated settings.
	/// </summary>
	/// <param name="parameter">The encoded hub-settings field, or null when not reported.</param>
	/// <param name="enabled">Whether the selected feature or saved plan should be enabled.</param>
	/// <returns>The updated field, or null when the input cannot be safely edited.</returns>
	internal static string? SetBroadcast (string? parameter, bool enabled)
		{
		if (!ReadBroadcast (parameter).HasValue)
			{
			return null;
			}
		string[] fields = parameter!.Split ('|');
		fields[1] = enabled ? "1" : "0";
		return string.Join ("|", fields);
		}
	}
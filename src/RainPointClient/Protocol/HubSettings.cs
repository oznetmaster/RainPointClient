// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Protocol references and third-party notices: see ATTRIBUTIONS.md and THIRD-PARTY-NOTICES.md.

namespace RainPointClient.Protocol;

internal static class HubSettings
	{
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
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
// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using RainPointClient;

if (args.Length != 2 || args[0] != "--read-only")
	{
	Console.WriteLine ("Usage: RainPointClient.Probe --read-only <private-settings.json>");
	Console.WriteLine ("Signs in and reads discovery/status only; never sends watering commands.");
	return 2;
	}

using CancellationTokenSource cancellation = new ();
Console.CancelKeyPress += (_, eventArgs) =>
{
	eventArgs.Cancel = true;
	cancellation.Cancel ();
};

try
	{
	using FileStream settingsStream = File.OpenRead (args[1]);
	ProbeSettings settings = await JsonSerializer.DeserializeAsync<ProbeSettings> (settingsStream, cancellationToken: cancellation.Token)
		 ?? throw new InvalidOperationException ("Settings are missing.");
	using RainPointCloudClient client = new ();
	await client.LoginAsync (settings.Email, settings.Password, settings.AreaCode, cancellation.Token);
	foreach (RainPointHome home in await client.GetHomesAsync (cancellation.Token))
		{
		Console.WriteLine ("Home: " + home.Name);
		foreach (RainPointHub hub in await client.GetHubsAsync (home.Id, cancellation.Token))
			{
			Console.WriteLine ("  Hub: " + hub.Model + " / " + hub.Name);
			foreach (RainPointDevice device in hub.Devices)
				{
				Console.WriteLine ("    Device: " + device.Model + " / " + device.Name);
				if (!device.SupportedZoneCount.HasValue)
					{
					continue;
					}

				RainPointTimerStatus status = await client.GetTimerStatusAsync (hub, device.Address, cancellation.Token);
				Console.WriteLine ("      Reading: " + status.Availability + "; RSSI: " + status.SignalStrengthDbm);
				foreach (RainPointZoneStatus zone in status.Zones)
					{
					string state = zone.IsOpen.HasValue ? (zone.IsOpen.Value ? "open" : "closed") : "unknown";
					Console.WriteLine ("      Zone " + zone.Zone + ": " + state + "; configured duration: " + zone.ConfiguredRunDuration);
					Console.WriteLine ("        Last usage (litres): " + zone.LastWaterUsageLitres + "; underlying count: " + zone.LastWaterUsageCounts);
					}
				}
			}
		}

	return 0;
	}
catch (OperationCanceledException)
	{
	Console.Error.WriteLine ("Cancelled.");
	return 3;
	}
catch (RainPointException error)
	{
	Console.Error.WriteLine (error.Message);
	return 1;
	}
catch (Exception error) when (error is IOException or JsonException or ArgumentException or InvalidOperationException)
	{
	Console.Error.WriteLine ("Check the private settings file and account country code. No credentials have been printed.");
	return 1;
	}
catch (System.Net.Http.HttpRequestException)
	{
	Console.Error.WriteLine ("The cloud connection failed.");
	return 1;
	}

internal sealed class ProbeSettings
	{
	[JsonPropertyName ("email"), JsonRequired]
	public string Email { get; set; } = string.Empty;

	[JsonPropertyName ("password"), JsonRequired]
	public string Password { get; set; } = string.Empty;

	[JsonPropertyName ("areaCode"), JsonRequired]
	public string AreaCode { get; set; } = string.Empty;
	}
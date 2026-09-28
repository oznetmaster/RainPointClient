using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture, NonParallelizable, Category ("Live")]
public sealed class CommandFeedbackLiveTests
	{
	[Test, Explicit ("Sends one stop to zone 1 only, after a closed baseline. Never opens a valve or edits configuration.")]
	public async Task StopAlreadyClosedZone1ReportsOptionalFeedback ()
		{
		string? path = Environment.GetEnvironmentVariable ("RAINPOINT_LIVE_SETTINGS");
		if (string.IsNullOrWhiteSpace (path))
			{
			Assert.Ignore ("Set RAINPOINT_LIVE_SETTINGS to an ignored private settings file.");
			}
		using RainPointCloudClient client = new ();
		using CancellationTokenSource timeout = new (TimeSpan.FromSeconds (120));
		try
			{
			Account account = JsonSerializer.Deserialize<Account> (File.ReadAllText (path!))!;
			await client.LoginAsync (account.Email, account.Password, account.AreaCode, timeout.Token);
			List<RainPointHub> hubs = [];
			foreach (RainPointHome home in await client.GetHomesAsync (timeout.Token))
				{
				hubs.AddRange ((await client.GetHubsAsync (home.Id, timeout.Token))
					 .Where (hub => hub.Model is "HWG023WBRF" or "HWG023WBRF-V2"
						 && hub.Devices.Any (device => device.Model == "HTV345FRF")));
				}
			Assert.That (hubs, Has.Count.EqualTo (1), "The fixture requires exactly one matching hub.");
			RainPointDevice[] timers = hubs[0].Devices.Where (device => device.Model == "HTV345FRF").ToArray ();
			Assert.That (timers, Has.Length.EqualTo (1), "The fixture requires exactly one matching timer.");

			RainPointTimerStatus baseline = await client.GetTimerStatusAsync (hubs[0], timers[0].Address, timeout.Token);
			Assert.That (baseline.Availability, Is.EqualTo (TimerReadingAvailability.Decoded));
			Assert.That (baseline.Zones.Single (item => item.Zone == 1).IsOpen, Is.False,
				 "Zone 1 must already report closed; no command was sent.");
			RainPointWateringCommandResult result = await client.StopWateringAsync (hubs[0], timers[0].Address, 1, timeout.Token);
			Assert.That (result.Outcome, Is.AnyOf (RainPointCommandOutcome.Accepted, RainPointCommandOutcome.AlreadyInRequestedStateOrTransitioning));
			Assert.That (result.RequestedZone, Is.EqualTo (1));
			Assert.That (result.Status.Address, Is.EqualTo (timers[0].Address));
			TestContext.Progress.WriteLine ($"Zone 1 stop acknowledgement: {result.Outcome}; response availability: {result.Status.Availability}; server timestamp present: {result.ResponseTimestamp.HasValue}.");
			foreach (RainPointZoneStatus zone in result.Status.Zones)
				TestContext.Progress.WriteLine ($"Response zone {zone.Zone}: reported open={zone.IsOpen?.ToString () ?? "unknown"}, last litres={zone.LastWaterUsageLitres?.ToString () ?? "unknown"}.");
			TestContext.Progress.WriteLine ("One stop only. No start, replay, schedule or configuration write; response is not physical-flow confirmation.");

			}
		catch (Exception error) when (error is not AssertionException && error is not SuccessException && error is not IgnoreException)
			{
			Assert.Fail ("Zone-1 stop-response check failed (" + error.GetType ().Name + "). " + (error is RainPointException protocol ? protocol.Message : "Account details and response bodies are omitted."));
			}
		finally
			{
			if (client.HasValidSession)
				{
				using CancellationTokenSource cleanup = new (TimeSpan.FromSeconds (15));
				try
					{
					await client.LogoutAsync (cleanup.Token);
					}
				catch (Exception error) when (error is not OutOfMemoryException)
					{
					TestContext.Progress.WriteLine ("Remote logout did not complete; client will be disposed.");
					}
				}
			}
		}

	private sealed class Account
		{
		[JsonPropertyName ("email")] public string Email { get; set; } = string.Empty;
		[JsonPropertyName ("password")] public string Password { get; set; } = string.Empty;
		[JsonPropertyName ("areaCode")] public string AreaCode { get; set; } = "44";
		}
	}
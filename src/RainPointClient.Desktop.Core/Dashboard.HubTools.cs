using System;
using System.Linq;
using System.Threading.Tasks;
namespace RainPointClient.Desktop.Core;

public sealed partial class Dashboard
	{
	public System.Collections.ObjectModel.ObservableCollection<RainPointProductModel> ProductModels { get; } = new ();
	public string CatalogMessage { get; private set; } = "Catalog metadata does not establish hardware support.";
	public System.Collections.Generic.IReadOnlyList<int> RfChannels { get; } = Array.AsReadOnly (new[] { 1, 2, 3 });
	private int? _rfChannel;
	public int? RfChannel
		{
		get => _rfChannel;
		set
			{
			if (!CanEdit)
				return;
			_rfChannel = value;
			Changed ();
			}
		}
	public bool CanSelectRfChannel => CanRefresh && _hubSettings?.RfChannel is >= 1 and <= 3;
	public bool CanSaveRfChannel => CanSelectRfChannel && _rfChannel is >= 1 and <= 3 && _rfChannel != _hubSettings?.RfChannel;
	public string SavedRfChannel { get; private set; } = "Unknown";
	public Task LoadProductCatalogAsync ()
		{
		if (!CanRefresh)
			return Task.CompletedTask;
		return RunAsync (async token =>
		 {
			 ProductModels.Clear ();
			 CatalogMessage = "Loading product metadata…";
			 try
				 {
				 var catalog = await _client.GetProductCatalogAsync (token);
				 foreach (var model in catalog.Models)
					 ProductModels.Add (model);
				 CatalogMessage = $"{ProductModels.Count} model variants · catalog version {catalog.Version?.ToString () ?? "unknown"}. Catalog presence does not establish client support.";
				 }
			 catch { CatalogMessage = "Catalog could not be loaded. Retry explicitly."; throw; }
		 }, "Product catalog read failed.");
		}
	private RainPointHub? _hubSettings;
	private bool _broadcast;
	public bool CanSaveBroadcast => CanRefresh && _hubSettings?.AutomaticTimeBroadcastEnabled.HasValue == true;
	public bool AutomaticTimeBroadcast
		{
		get => _broadcast;
		set
			{
			if (!CanEdit)
				return;
			_broadcast = value;
			Changed ();
			}
		}
	public string HubToolsMessage { get; private set; } = "Load the selected hub's settings or check firmware.";
	public string SavedBroadcast { get; private set; } = "Unknown";
	public string HubFirmware { get; private set; } = "Not checked";
	public string TimerFirmware { get; private set; } = "Not checked";
	public bool CanCheckTimerFirmware => CanRefresh && _timer is not null;
	private void ClearHubTools ()
		{
		ClearAdministration ();
		ClearProfiles ();
		_hubSettings = null;
		_broadcast = false;
		_rfChannel = null;
		SavedRfChannel = "Unknown";
		ProductModels.Clear ();
		CatalogMessage = "Catalog metadata does not establish hardware support.";
		SavedBroadcast = "Unknown";
		HubFirmware = TimerFirmware = "Not checked";
		HubToolsMessage = "Load the selected hub's settings or check firmware.";
		}
	public Task LoadHubSettingsAsync ()
		{
		if (!CanRefresh)
			return Task.CompletedTask;
		return RunAsync (async token =>
		 {
			 _hubSettings = null;
			 SavedBroadcast = "Unknown";
			 var matches = (await _client.GetHubsAsync (_hub!.HomeId, token)).Where (h => h.Id == _hub.Id && h.DeviceName == _hub.DeviceName && h.ProductKey == _hub.ProductKey).ToArray ();
			 if (matches.Length != 1)
				 throw new InvalidOperationException ("Hub identity changed.");
			 ShowBroadcast (matches[0]);
			 HubToolsMessage = "Hub settings read from the cloud.";
		 }, "Hub settings could not be loaded. Reload or sign in again.");
		}
	private void ShowBroadcast (RainPointHub hub)
		{
		_hubSettings = hub;
		SavedRfChannel = hub.RfChannel?.ToString () ?? "Unknown";
		_rfChannel = hub.RfChannel is >= 1 and <= 3 ? hub.RfChannel : null;
		_broadcast = hub.AutomaticTimeBroadcastEnabled == true;
		SavedBroadcast = hub.AutomaticTimeBroadcastEnabled switch
			{
				true => "Enabled",
				false => "Disabled",
				_ => "Not reported or unsupported"
				};
		}
	public Task SaveRfChannelAsync ()
		{
		if (!CanSaveRfChannel)
			return Task.CompletedTask;
		var expected = _hubSettings!;
		int channel = _rfChannel!.Value;
		return RunAsync (async token =>
		 {
			 _hubSettings = null;
			 HubToolsMessage = "Saving RF channel…";
			 try
				 {
				 await _client.SetRfChannelAsync (expected, channel, token);
				 var current = (await _client.GetHubsAsync (expected.HomeId, token)).Single (h => h.Id == expected.Id && h.DeviceName == expected.DeviceName && h.ProductKey == expected.ProductKey && h.Model == expected.Model);
				 ShowBroadcast (current);
				 if (current.RfChannel != channel)
					 {
					 _hubSettings = null;
					 HubToolsMessage = "Cloud accepted the channel change, but read-back differs. Reload; no write was retried.";
					 }
				 else
					 HubToolsMessage = "RF channel matches the cloud read-back. Communication with paired devices is not confirmed.";
				 }
			 catch
				 {
				 _hubSettings = null;
				 HubToolsMessage = "Channel change or read-back did not complete; its outcome may be unknown. Reload; no write was retried.";
				 throw;
				 }
		 }, "RF channel operation failed. See the hub message.");
		}
	public Task SaveBroadcastAsync ()
		{
		if (!CanSaveBroadcast)
			return Task.CompletedTask;
		var hub = _hub!;
		bool enabled = _broadcast;
		return RunAsync (async token =>
		 {
			 _hubSettings = null;
			 HubToolsMessage = "Saving automatic time broadcast…";
			 bool accepted = false;
			 try
				 {
				 await _client.SetAutomaticTimeBroadcastAsync (hub, enabled, token);
				 accepted = true;
				 var current = (await _client.GetHubsAsync (hub.HomeId, token)).Single (h => h.Id == hub.Id && h.DeviceName == hub.DeviceName && h.ProductKey == hub.ProductKey);
				 ShowBroadcast (current);
				 bool matches = current.AutomaticTimeBroadcastEnabled == enabled;
				 if (!matches)
					 _hubSettings = null;
				 HubToolsMessage = matches ? "Automatic time broadcast matches the cloud read-back." : "Cloud accepted the change, but read-back does not match. Reload; no write was retried.";
				 }
			 catch
				 {
				 _hubSettings = null;
				 HubToolsMessage = accepted ? "Cloud accepted the change but read-back failed. Reload; no write was retried." : "Save did not complete; its outcome may be unknown. Reload; no write was retried.";
				 throw;
				 }
		 }, "Hub settings operation failed. See the hub message.");
		}
	public Task BroadcastTimeAsync ()
		{
		if (!CanRefresh)
			return Task.CompletedTask;
		return RunAsync (async token =>
		 {
			 HubToolsMessage = "Requesting time broadcast…";
			 try
				 {
				 var outcome = await _client.BroadcastTimeAsync (_hub!, token);
				 HubToolsMessage = outcome == RainPointCommandOutcome.Accepted ? "Cloud accepted the time broadcast. RF delivery is not confirmed." : "Time broadcast was already requested or transitioning. RF delivery is not confirmed.";
				 }
			 catch { HubToolsMessage = "Time broadcast outcome is unknown. It was not retried."; throw; }
		 }, "Time broadcast failed. See the hub message.");
		}
	public Task CheckFirmwareAsync (bool timer)
		{
		if (!(timer ? CanCheckTimerFirmware : CanRefresh))
			return Task.CompletedTask;
		return RunAsync (async token =>
		 {
			 if (timer)
				 TimerFirmware = "Not checked";
			 else
				 HubFirmware = "Not checked";
			 var firmware = timer ? await _client.GetTimerFirmwareAsync (_hub!, _timer!.Address, token) : await _client.GetHubFirmwareAsync (_hub!, token);
			 string text = "Installed: " + firmware.InstalledVersion + (firmware.AvailableUpdate is { } offer ? " · Available: " + offer.Version + "\n" + offer.ReleaseNotes : " · No update offered");
			 if (timer)
				 TimerFirmware = text;
			 else
				 HubFirmware = text;
			 HubToolsMessage = "Firmware check completed. This app does not install firmware.";
		 }, "Firmware could not be checked. Retry explicitly or sign in again.");
		}
	}
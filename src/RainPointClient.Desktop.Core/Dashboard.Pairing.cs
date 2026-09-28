using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
namespace RainPointClient.Desktop.Core;

public sealed partial class Dashboard
	{
	private RainPointHomeDetails? _pairingHome;
	private RainPointHub? _pairingObservedHub, _pairingSearchHub;
	public ObservableCollection<RainPointProductModel> PairingModels { get; } = new ();
	public ObservableCollection<RainPointDevice> PairedDevices { get; } = new ();
	private RainPointProductModel? _pairingModel;
	public RainPointProductModel? PairingModel
		{
		get => _pairingModel; set
			{
			if (!CanEdit)
				return;
			_pairingModel = value;
			Changed ();
			}
		}
	private RainPointDevice? _pairingChild;
	public RainPointDevice? PairingChild
		{
		get => _pairingChild; set
			{
			if (!CanEdit)
				return;
			_pairingChild = value;
			Changed ();
			}
		}
	public bool CanReadPairing => CanEdit && _client.HasValidSession && _home is not null && _hub is not null;
	public bool CanRemoveHub => CanReadPairing && _pairingHome is not null && _pairingObservedHub?.Id == _hub!.Id && _pairingObservedHub.HomeId == _home!.Id;
	public bool CanRemoveChild => CanRemoveHub && PairingChild is not null && PairedDevices.Contains (PairingChild);
	public bool CanStartPairing => CanRemoveHub && PairingModel is not null && PairingModels.Contains (PairingModel);
	public bool CanCancelPairing => CanEdit && _client.HasValidSession && _pairingSearchHub is not null;
	public string PairingMessage { get; private set; } = "Load the selected hub before pairing or removal.";
	private void ClearPairing ()
		{
		_pairingHome = null;
		_pairingObservedHub = null;
		_pairingModel = null;
		_pairingChild = null;
		PairingModels.Clear ();
		PairedDevices.Clear ();
		PairingMessage = "Load the selected hub before pairing or removal.";
		}
	public Task LoadPairingAsync () => RunAsync (async token =>
	{
		var selected = _hub ?? throw new InvalidOperationException ();
		ClearPairing ();
		var home = await _client.GetHomeAsync (selected.HomeId, token);
		var hub = (await _client.GetHubsAsync (home.Id, token)).Single (h => h.Id == selected.Id);
		var catalog = await _client.GetProductCatalogAsync (token);
		_pairingHome = home;
		_pairingObservedHub = hub;
		foreach (var model in catalog.Models.Where (m => m.Model is "HTV345FRF" or "HCS005FRF" or "HCS021FRF" && m.IsHub != true))
			PairingModels.Add (model);
		foreach (var child in hub.Devices)
			PairedDevices.Add (child);
		PairingMessage = "Pairing state read. Removing a device requires pairing it again to restore access.";
	}, "Pairing state could not be read. Reload before an operation.");
	public Task StartPairingAsync ()
		{
		if (!CanStartPairing)
			return Task.CompletedTask;
		var home = _pairingHome!;
		var hub = _pairingObservedHub!;
		var model = PairingModel!;
		return RunAsync (async token =>
		{
			_pairingHome = null;
			_pairingSearchHub = hub;
			PairingMessage = "Pairing request submitted or uncertain. Cancel search remains available.";
			var window = await _client.StartDevicePairingAsync (home, hub, model, token);
			PairingMessage = "Search accepted for " + window.TotalSeconds + " seconds. Put the new device in pairing mode, then reload to check discovery.";
		}, "Pairing failed or its outcome is unknown. It was not retried. Cancel search if necessary, then reload.");
		}
	public Task CancelPairingAsync () => RunAsync (async token =>
	{
		var hub = _pairingSearchHub ?? throw new InvalidOperationException ();
		await _client.CancelDevicePairingAsync (hub, token);
		_pairingSearchHub = null;
		PairingMessage = "Search cancellation accepted. Reload to see any devices already paired.";
	}, "Search cancellation failed or its outcome is unknown. Check the hub and retry cancellation explicitly if needed.");
	public Task RemovePairedDeviceAsync (bool removeHub)
		{
		if (removeHub ? !CanRemoveHub : !CanRemoveChild)
			return Task.CompletedTask;
		var home = _pairingHome!;
		var hub = _pairingObservedHub!;
		var child = PairingChild;
		return RunAsync (async token =>
		{
			await StopMonitorCoreAsync ();
			_pairingHome = null;
			try
				{
				if (removeHub)
					await _client.RemoveHubAsync (home, hub, token);
				else
					await _client.RemoveDeviceAsync (home, hub, child!.Address, token);
				Message = "Removal accepted. Select the home again to rediscover its devices.";
				}
			finally { ClearHub (); Hubs.Clear (); }
		}, "Removal failed or its outcome is unknown. It was not retried. Select the home again to reconcile device discovery.");
		}
	}
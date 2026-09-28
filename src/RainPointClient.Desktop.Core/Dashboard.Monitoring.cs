using System;
using System.Threading.Tasks;

namespace RainPointClient.Desktop.Core;

public sealed partial class Dashboard
	{
	private RainPointMonitor? _monitor;
	private RainPointStatusUpdate? _monitorUpdate;
	public bool IsMonitoring => _monitor is not null;
	public bool CanToggleMonitoring => CanEdit && (IsMonitoring || CanRefresh);
	public string MonitorButtonText => IsMonitoring ? "Stop live feedback" : "Start live feedback";
	public string MonitorText { get; private set; } = "Live feedback is off.";

	// The host posts actions to its UI thread. Queued callbacks from previous monitors are discarded.
	public Task StartMonitoringAsync (Action<Action> dispatch, RainPointMonitorOptions? options = null) => RunAsync (token =>
	{
		if (dispatch is null)
			throw new ArgumentNullException (nameof (dispatch));
		if (_hub is null)
			throw new InvalidOperationException ("Select a hub.");
		if (_monitor is not null)
			return Task.CompletedTask;
		RainPointMonitor monitor = new (_client, _hub, options);
		_monitorUpdate = null;
		_monitor = monitor;
		monitor.StatusReceived += (_, update) => dispatch (() =>
		 {
			 if (ReferenceEquals (_monitor, monitor) && !_closing)
				 ApplyMonitorUpdate (update);
		 });
		monitor.StateChanged += (_, _) => dispatch (() =>
		 {
			 if (!ReferenceEquals (_monitor, monitor) || _closing)
				 return;
			 MonitorText = monitor.State switch
				 {
					 RainPointMonitorState.PushConnected => "Live feedback connected · polling remains active as a fallback.",
					 RainPointMonitorState.AuthenticationRequired => "Live feedback needs sign-in. Use Renew session or sign in again.",
					 RainPointMonitorState.Reconnecting => "Live feedback reconnecting · polling remains active.",
					 RainPointMonitorState.Stopped => "Live feedback stopped.",
					 _ => "Reading status · connecting live feedback…"
					 };
			 Changed ();
		 });
		MonitorText = "Starting live feedback…";
		_ = ObserveMonitorAsync (monitor, dispatch, monitor.RunAsync (token));
		return Task.CompletedTask;
	}, "Live feedback could not be started.");

	private async Task ObserveMonitorAsync (RainPointMonitor monitor, Action<Action> dispatch, Task running)
		{
		try
			{
			await running.ConfigureAwait (false);
			}
		catch (Exception error) when (error is not OutOfMemoryException) { }
		dispatch (() =>
		{
			if (!ReferenceEquals (_monitor, monitor) || _closing)
				return;
			_monitor = null;
			MonitorText = "Live feedback stopped. Start it again to retry.";
			Changed ();
		});
		}

	public Task StopMonitoringAsync () => RunAsync (_ => StopMonitorCoreAsync (), "Live feedback could not be stopped cleanly.");

	private async Task StopMonitorCoreAsync ()
		{
		RainPointMonitor? monitor = _monitor;
		_monitor = null;
		if (monitor is not null)
			await monitor.StopAsync ();
		MonitorText = "Live feedback is off.";
		Changed ();
		}

	public Task SelectHubAsync (RainPointHub? hub) => RunAsync (async _ =>
	{
		await StopMonitorCoreAsync ();
		SelectHubCore (hub);
	}, "The selected hub could not be changed.");

	private void ApplyMonitorUpdate (RainPointStatusUpdate update)
		{
		if (_monitorUpdate is not null && update.Revision <= _monitorUpdate.Revision)
			return;
		_monitorUpdate = update;
		_snapshot = update.Status;
		LastReceivedAt = update.ReceivedAt;
		ReceivedText = $"Last update via {update.Source}: {update.ReceivedAt.ToLocalTime ():yyyy-MM-dd HH:mm:ss zzz}";
		HubText = $"{_hub?.Model} · cloud connection: {Flag (update.Status.IsConnected, "connected", "disconnected")} · Wi-Fi: {Signal (update.Status.WifiSignalStrengthDbm)} · firmware {_hub?.FirmwareVersion ?? "unknown"}";
		ShowTimer ();
		Message = "Feedback received. Device timestamps identify retained readings; cloud feedback can lag physical operation.";
		Changed ();
		}
	}
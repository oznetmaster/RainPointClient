using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RainPointClient.Desktop.Core;

// A UI-thread-owned reference host. Transport and protocol logic remain in the client.
public sealed partial class Dashboard : INotifyPropertyChanged
	{
	private readonly RainPointCloudClient _client;
	private readonly CancellationTokenSource _lifetime = new ();
	private TaskCompletionSource<bool>? _idle;
	private bool _closing;
	private bool _disposed;
	private RainPointHome? _home;
	private RainPointHub? _hub;
	private RainPointDevice? _timer;
	private RainPointHubStatus? _snapshot;
	private bool _armed;
	private int _controlZone = 1;
	private string _durationMinutes = "1";

	public Dashboard (RainPointCloudClient client)
		{
		_client = client ?? throw new ArgumentNullException (nameof (client));
		PlanDraft.PropertyChanged += (_, _) => Changed ();
		foreach (var month in SeasonMonths)
			month.PropertyChanged += (_, _) => Changed ();
		}

	public event PropertyChangedEventHandler? PropertyChanged;
	public ObservableCollection<RainPointHome> Homes { get; } = new ();
	public ObservableCollection<RainPointHub> Hubs { get; } = new ();
	public ObservableCollection<RainPointDevice> Timers { get; } = new ();
	public ObservableCollection<ZoneRow> Zones { get; } = new ();
	public bool IsBusy
		{
		get; private set;
		}
	public bool CanEdit => !IsBusy && !_closing;
	public bool CanConnect => CanEdit;
	public bool CanDisconnect => CanEdit && (_client.HasValidSession || Homes.Count > 0);
	public bool CanRefresh => CanEdit && _client.HasValidSession && _hub is not null;
	public bool CanRenew => CanEdit && _client.CanRefreshSession;
	public bool CanStart => !IsRecoveringSession && CanRefresh && _timer is not null && _armed && ValidManualSettings ();
	// Stop remains available after a failed refresh or command; it does not depend on reported state.
	public bool CanStop => !IsRecoveringSession && CanRefresh && _timer is not null && _armed;
	public string Message { get; private set; } = "Sign in, then select a home, hub and timer.";
	public string CommandMessage { get; private set; } = "No command sent.";
	public string SessionText => _client.HasValidSession ? "Signed in" : "Signed out or session expired";
	public string HubText { get; private set; } = "No hub reading.";
	public string TimerText { get; private set; } = "No timer reading.";
	public string ReceivedText { get; private set; } = "No successful status read.";
	public DateTimeOffset? LastReceivedAt
		{
		get; private set;
		}
	public RainPointHome? SelectedHome => _home;
	public RainPointHub? SelectedHub => _hub;
	public RainPointDevice? SelectedTimer => _timer;

	public int[] ControlZones => Enumerable.Range (1, _timer?.SupportedZoneCount ?? 0).ToArray ();
	public int ControlZone
		{
		get => _controlZone;
		set
			{
			if (!CanEdit || !ControlZones.Contains (value) || value == _controlZone)
				return;
			_controlZone = value;
			_armed = false;
			CommandMessage = "No command sent for this selection.";
			Changed ();
			}
		}
	public string StartLabel => $"Start zone {_controlZone}";
	public string StopLabel => $"Stop zone {_controlZone}";
	public string ArmLabel => $"Enable zone {_controlZone} controls for the selected timer";

	public bool ControlsArmed
		{
		get => _armed;
		set
			{
			if (!CanEdit)
				return;
			_armed = value && _timer is not null;
			Changed ();
			}
		}

	public string DurationMinutes
		{
		get => _durationMinutes;
		set
			{
			if (!CanEdit)
				return;
			_durationMinutes = value;
			Changed ();
			}
		}

	// True means this attempt authenticated, even if subsequent home discovery failed.
	public async Task<bool> ConnectAsync (string email, string password, string countryCode)
		{
		bool authenticated = false;
		await RunAsync (async token =>
			{
				await StopSessionRecoveryCoreAsync ();
				await StopMonitorCoreAsync ();
				ClearAccount ();
				await _client.LoginAsync (email.Trim (), password, countryCode.Trim (), token);
				authenticated = true;
				foreach (RainPointHome home in await _client.GetHomesAsync (token))
					Homes.Add (home);
				Message = Homes.Count == 0 ? "This account has no homes." : "Signed in. Select a home.";
			}, "Sign-in or home discovery failed. Check the account details and any login cooldown, then try explicitly again.");
		return authenticated;
		}

	public Task SelectHomeAsync (RainPointHome? home) => RunAsync (async token =>
	{
		if (home is not null && !Homes.Contains (home))
			throw new ArgumentException ("Select a discovered home.");
		await StopMonitorCoreAsync ();
		ClearHub ();
		Hubs.Clear ();
		_home = home;
		if (home is null)
			return;
		foreach (RainPointHub hub in await _client.GetHubsAsync (home.Id, token))
			Hubs.Add (hub);
		Message = Hubs.Count == 0 ? "No hubs found in this home." : "Select a hub and timer.";
	}, "Device discovery failed. Select the home again to retry.");

	public void SelectHub (RainPointHub? hub)
		{
		if (!CanEdit)
			return;
		if (_monitor is not null)
			throw new InvalidOperationException ("Use SelectHubAsync while monitoring.");
		SelectHubCore (hub);
		}

	private void SelectHubCore (RainPointHub? hub)
		{
		if (hub is not null && !Hubs.Contains (hub))
			throw new ArgumentException ("Select a discovered hub.");
		ClearHub ();
		_hub = hub;
		if (hub is not null)
			{
			foreach (RainPointDevice timer in hub.Devices.Where (device => device.SupportedZoneCount.HasValue))
				Timers.Add (timer);
			HubText = $"{hub.Model} · firmware {hub.FirmwareVersion ?? "unknown"}";
			Message = Timers.Count == 0 ? "No supported timers on this hub." : "Select a timer, then refresh status.";
			}
		Changed ();
		}

	public void SelectTimer (RainPointDevice? timer)
		{
		if (!CanEdit)
			return;
		if (timer is not null && !Timers.Contains (timer))
			throw new ArgumentException ("Select a discovered timer.");
		_timer = timer;
		_controlZone = 1;
		ClearSettings ();
		ClearHistory ();
		ClearPlans ();
		ClearCalendar ();
		ClearHubTools ();
		_armed = false;
		CommandMessage = "No command sent for this selection.";
		ShowTimer ();
		Changed ();
		}

	public Task RefreshAsync () => RunAsync (async token =>
	{
		if (_hub is null)
			throw new InvalidOperationException ("Select a hub.");
		if (_monitor is not null)
			{
			await _monitor.RefreshAsync (token);
			if (_monitor.Current is { } update)
				ApplyMonitorUpdate (update);
			return;
			}
		RainPointHubStatus result = await _client.GetHubStatusAsync (_hub, token);
		_snapshot = result;
		LastReceivedAt = DateTimeOffset.Now;
		ReceivedText = "Last successful read: " + LastReceivedAt.Value.ToString ("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.CurrentCulture);
		HubText = $"{_hub.Model} · cloud connection: {Flag (result.IsConnected, "connected", "disconnected")} · Wi-Fi: {Signal (result.WifiSignalStrengthDbm)} · firmware {_hub.FirmwareVersion ?? "unknown"}";
		ShowTimer ();
		Message = "Status received. Cloud readings can lag physical operation by 30 seconds or more.";
	}, "Status refresh failed. Displayed readings are from the last successful read; use their timestamps.");

	public Task RenewAsync () => RunAsync (async token =>
	{
		await _client.RefreshSessionAsync (token);
		Message = "Session renewed.";
	}, "Session renewal failed. Sign in again if the session is no longer valid.");

	public Task DisconnectAsync () => RunAsync (async token =>
	{
		try
			{
			await StopMonitorCoreAsync ();
			await StopSessionRecoveryCoreAsync ();
			await _client.LogoutAsync (token);
			}
		finally { ClearAccount (); }
		Message = "Signed out. Signing out does not stop an active watering run.";
	}, "Remote sign-out failed. The local session and displayed account were cleared.");

	public Task StartSelectedZoneAsync () => ControlAsync (true);
	public Task StopSelectedZoneAsync () => ControlAsync (false);

	private Task ControlAsync (bool start)
		{
		if (!(start ? CanStart : CanStop))
			return Task.CompletedTask;
		RainPointHub hub = _hub!;
		RainPointDevice timer = _timer!;
		int zone = _controlZone;
		return RunAsync (async token =>
		{
			CommandMessage = $"Sending zone {zone} {(start ? "start" : "stop")}…";
			Changed ();
			try
				{
				RainPointWateringCommandResult result = start
						? await StartManualAsync (hub, timer.Address, zone, token)
						: await _client.StopWateringAsync (hub, timer.Address, zone, token);
				CommandMessage = $"{DateTimeOffset.Now:HH:mm:ss}: zone {zone} {(start ? "start" : "stop")} — "
						+ (result.Outcome == RainPointCommandOutcome.Accepted ? "cloud accepted." : "already requested or transitioning.")
						+ " Physical operation is not confirmed." + DescribeCommandFeedback (result);
				Message = "Refresh status to observe feedback. The command has not changed the displayed reading.";
				}
			catch
				{
				CommandMessage = $"The command outcome is unknown. It may have reached the valve. It was not retried; inspect the valve and explicitly stop zone {zone} if needed.";
				throw;
				}
		}, "Command failed or was interrupted. Check the command outcome below.");
		}

	private static string DescribeCommandFeedback (RainPointWateringCommandResult result)
		{
		if (result.Status.Availability != TimerReadingAvailability.Decoded)
			return result.Status.Availability == TimerReadingAvailability.NotReported
				 ? " No status was included in the response." : " Response status could not be decoded.";
		RainPointZoneStatus? zone = result.Status.Zones.FirstOrDefault (item => item.Zone == result.RequestedZone);
		if (zone is null)
			return " No status was included for this zone.";
		ZoneRow row = new (zone);
		string stamp = result.ResponseTimestamp?.ToLocalTime ().ToString ("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture) ?? "unknown";
		return $" Response only: {row.State}; last usage {row.Usage}; configured duration {row.Duration}; server time {stamp}. Monitored readings are unchanged.";
		}


	private async Task RunAsync (Func<CancellationToken, Task> operation, string failure)
		{
		if (!CanEdit)
			return;
		IsBusy = true;
		_idle = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		Changed ();
		try
			{
			await operation (_lifetime.Token);
			}
		catch (Exception error) when (error is not OutOfMemoryException)
			{
			// Do not expose response bodies, account identifiers or credentials in UI errors.
			Message = failure;
			if (!_client.HasValidSession)
				_armed = false;
			}
		finally
			{
			IsBusy = false;
			Changed ();
			_idle.TrySetResult (true);
			}
		}

	public async Task CloseAsync ()
		{
		if (_disposed)
			return;
		_closing = true;
		_lifetime.Cancel ();
		if (IsBusy && _idle is not null)
			await _idle.Task;
		if (_disposed)
			return;
		await StopMonitorCoreAsync ();
		await StopSessionRecoveryCoreAsync ();
		_disposed = true;
		_client.Dispose ();
		_lifetime.Dispose ();
		}

	private void ClearAccount ()
		{
		ClearAccountVerification ();
		_pairingSearchHub = null;
		ClearHub ();
		_home = null;
		Hubs.Clear ();
		Homes.Clear ();
		}

	private void ClearHub ()
		{
		ClearPairing ();
		_hub = null;
		_timer = null;
		_controlZone = 1;
		ClearSettings ();
		ClearHistory ();
		ClearPlans ();
		ClearCalendar ();
		ClearHubTools ();
		_snapshot = null;
		_monitorUpdate = null;
		_armed = false;
		LastReceivedAt = null;
		Timers.Clear ();
		Zones.Clear ();
		HubText = "No hub reading.";
		TimerText = "No timer reading.";
		ReceivedText = "No successful status read.";
		CommandMessage = "No command sent for this selection.";
		}

	private void ShowTimer ()
		{
		Zones.Clear ();
		RainPointTimerStatus? status = _snapshot?.Timers.SingleOrDefault (item => item.Address == _timer?.Address);
		TimerText = _timer is null ? "No timer selected." : $"{_timer.Model} · RF address {_timer.Address} · firmware {_timer.FirmwareVersion ?? "unknown"}";
		if (status is null)
			{
			if (_timer is not null)
				for (int zone = 1; zone <= _timer.SupportedZoneCount; zone++)
					Zones.Add (new ZoneRow (zone));
			return;
			}
		TimerText += $"\nReading: {status.Availability} · RF: {Signal (status.SignalStrengthDbm)} · battery: {Flag (status.IsBatteryLow, "low", "normal")}";
		TimerText += "\nCloud data changed: " + (status.LastDataChange?.ToString ("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.CurrentCulture) ?? "unknown");
		TimerText += " · device report: " + (status.ReportedAtLocal?.ToString ("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture) ?? "unknown") + " (device local time)";
		RainPointTimerObservation? observation = _monitorUpdate?.Timers.SingleOrDefault (item => item.Status.Address == _timer?.Address);
		if (observation is not null)
			TimerText += $"\nAccepted via {observation.Source}: {observation.ReceivedAt.ToLocalTime ():yyyy-MM-dd HH:mm:ss zzz}";
		foreach (RainPointZoneStatus zone in status.Zones)
			Zones.Add (new ZoneRow (zone));
		if (status.Zones.Count == 0 && _timer is not null)
			for (int zone = 1; zone <= _timer.SupportedZoneCount; zone++)
				Zones.Add (new ZoneRow (zone));
		}

	private static string Flag (bool? value, string yes, string no) => value.HasValue ? value.Value ? yes : no : "unknown";
	private static string Signal (int? value) => value.HasValue ? value.Value.ToString (CultureInfo.CurrentCulture) + " dBm" : "unknown";
	private void Changed () => PropertyChanged?.Invoke (this, new PropertyChangedEventArgs (string.Empty));
	}
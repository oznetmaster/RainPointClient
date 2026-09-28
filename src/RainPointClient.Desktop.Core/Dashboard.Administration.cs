// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace RainPointClient.Desktop.Core;

public sealed partial class Dashboard
	{
	private RainPointHomeDetails? _administration;
	public ObservableCollection<RainPointRoom> Rooms { get; } = new ();
	public ObservableCollection<RainPointMember> Members { get; } = new ();
	public ObservableCollection<RainPointInvitation> Invitations { get; } = new ();
	private RainPointRoom? _selectedRoom;
	public RainPointRoom? SelectedRoom
		{
		get => _selectedRoom; set
			{
			_selectedRoom = value;
			RoomDeviceChoices.Clear ();
			_assignmentsRoomId = null;
			Changed ();
			}
		}
	public ObservableCollection<RoomDeviceChoice> RoomDeviceChoices { get; } = new ();
	private long? _assignmentsRoomId;
	public bool CanSaveRoomAssignments => CanWriteAdministration && SelectedRoom is not null && _assignmentsRoomId == SelectedRoom.Id;
	public string DeviceNameDraft { get; set; } = string.Empty;
	public RainPointMember? SelectedMember
		{
		get; set;
		}
	public RainPointInvitation? SelectedInvitation
		{
		get; set;
		}
	public string HomeNameDraft { get; set; } = string.Empty;
	public string RoomNameDraft { get; set; } = string.Empty;
	public string NewHomeName { get; set; } = string.Empty;
	public ObservableCollection<RainPointTimeZone> TimeZones { get; } = new ();
	public RainPointTimeZone? SelectedTimeZone
		{
		get; set;
		}
	public string CurrentTimeZone { get; private set; } = string.Empty;
	public string NewHomeTimeZone { get; set; } = "Europe/London";
	public string InviteEmail { get; set; } = string.Empty;
	public string LatitudeDraft { get; set; } = string.Empty;
	public string LongitudeDraft { get; set; } = string.Empty;
	public RainPointMemberRole MemberRoleDraft
		{
		get; set;
		}
	public RainPointMemberRole[] MemberRoles => (RainPointMemberRole[])Enum.GetValues (typeof (RainPointMemberRole));
	public RainPointDateFormat? DateFormat
		{
		get; set;
		}
	public RainPointDateFormat[] DateFormats => (RainPointDateFormat[])Enum.GetValues (typeof (RainPointDateFormat));
	public ObservableCollection<RainPointCurrency> Currencies { get; } = new ();
	public RainPointCurrency? SelectedCurrency
		{
		get; set;
		}
	public RainPointPressureUnit[] PressureUnits => (RainPointPressureUnit[])Enum.GetValues (typeof (RainPointPressureUnit));
	public bool TwelveHourClock
		{
		get; set;
		}
	public bool Fahrenheit
		{
		get; set;
		}
	public bool ImperialLength
		{
		get; set;
		}
	public bool ImperialVolume
		{
		get; set;
		}
	public RainPointPressureUnit PressureUnit
		{
		get; set;
		}
	public bool MobileNotifications
		{
		get; set;
		}
	public bool EmailNotifications
		{
		get; set;
		}
	public string AdministrationMessage { get; private set; } = "Load the selected home's settings.";
	public string NotificationMessage { get; private set; } = "Load the settings observed at sign-in.";
	public bool CanReadAdministration => CanEdit && _client.HasValidSession && _home is not null;
	public bool CanWriteAdministration => CanReadAdministration && _administration is not null;
	public bool CanWriteUnits => CanWriteAdministration && _administration?.DisplayUnits is not null;
	public bool CanReadAccount => CanEdit && _client.HasValidSession;
	public bool CanWriteNotifications => CanReadAccount && _client.NotificationPreferences is not null;
	private void ClearAdministration ()
		{
		ClearWeather ();
		ClearScenes ();
		Currencies.Clear ();
		SelectedCurrency = null;
		DateFormat = null;
		TimeZones.Clear ();
		SelectedTimeZone = null;
		CurrentTimeZone = string.Empty;
		_administration = null;
		Rooms.Clear ();
		Members.Clear ();
		Invitations.Clear ();
		SelectedRoom = null;
		SelectedMember = null;
		SelectedInvitation = null;
		HomeNameDraft = RoomNameDraft = LatitudeDraft = LongitudeDraft = InviteEmail = DeviceNameDraft = string.Empty;
		TwelveHourClock = Fahrenheit = ImperialLength = ImperialVolume = false;
		PressureUnit = RainPointPressureUnit.Pascal;
		MobileNotifications = EmailNotifications = false;
		AdministrationMessage = "Load the selected home's settings.";
		NotificationMessage = "Load the settings observed at sign-in.";
		}
	public void LoadNotificationPreferences ()
		{
		if (!CanReadAccount)
			return;
		var observed = _client.NotificationPreferences;
		MobileNotifications = observed?.MobileEnabled == true;
		EmailNotifications = observed?.EmailEnabled == true;
		NotificationMessage = observed is null ? "Sign in again to obtain notification settings." : "Settings observed at sign-in; delivery also depends on the phone's notification permissions.";
		Changed ();
		}
	public Task SaveNotificationPreferencesAsync ()
		{
		if (!CanWriteNotifications)
			return Task.CompletedTask;
		var expected = _client.NotificationPreferences!;
		bool mobile = MobileNotifications, email = EmailNotifications;
		return RunAsync (async token =>
		{
			NotificationMessage = "Saving; sign in again after this attempt to verify the stored setting.";
			await _client.SetNotificationPreferencesAsync (expected, mobile, email, token);
			NotificationMessage = "Cloud accepted the change. Sign in again to verify it; delivery has not been tested.";
		}, "Notification update failed or its outcome is uncertain. Sign in again before another attempt.");
		}
	public Task LoadAdministrationAsync ()
		{
		if (!CanReadAdministration)
			return Task.CompletedTask;
		long id = _home!.Id;
		return RunAsync (async token =>
		{
			ClearAdministration ();
			var home = await _client.GetHomeAsync (id, token);
			var members = await _client.GetMembersAsync (id, token);
			_administration = home;
			HomeNameDraft = home.Name;
			CurrentTimeZone = home.TimeZoneName ?? "Unknown";
			LatitudeDraft = home.Latitude?.ToString (CultureInfo.InvariantCulture) ?? string.Empty;
			LongitudeDraft = home.Longitude?.ToString (CultureInfo.InvariantCulture) ?? string.Empty;
			var units = home.DisplayUnits;
			TwelveHourClock = units?.TwelveHourClock == true;
			Fahrenheit = units?.Fahrenheit == true;
			ImperialLength = units?.ImperialLength == true;
			ImperialVolume = units?.ImperialVolume == true;
			PressureUnit = units?.Pressure ?? RainPointPressureUnit.Pascal;
			DateFormat = units?.DateFormat;
			foreach (var room in home.Rooms)
				Rooms.Add (room);
			foreach (var member in members)
				Members.Add (member);
			AdministrationMessage = "Home and members read. Every edit requires a fresh load afterward.";
		}, "Home administration could not be loaded.");
		}
	private Task SaveAdministrationAsync (Func<RainPointHomeDetails, CancellationToken, Task> action)
		{
		if (!CanWriteAdministration)
			return Task.CompletedTask;
		var expected = _administration!;
		return RunAsync (async token =>
		{
			_administration = null;
			AdministrationMessage = "Change requested; reload afterward to verify the result.";
			await action (expected, token);
			AdministrationMessage = "Cloud accepted the change. Reload to verify it before another edit.";
		}, "Administration change failed or its outcome is uncertain. Reload before another attempt.");
		}
	public Task SaveHomeNameAsync ()
		{
		string name = HomeNameDraft;
		return SaveAdministrationAsync ((h, t) => _client.RenameHomeAsync (h, name, t));
		}
	public Task SaveDisplayUnitsAsync ()
		{
		var units = new RainPointDisplayUnits { TwelveHourClock = TwelveHourClock, Fahrenheit = Fahrenheit, ImperialLength = ImperialLength, ImperialVolume = ImperialVolume, Pressure = PressureUnit, DateFormat = DateFormat };
		return SaveAdministrationAsync ((h, t) => _client.SetHomeDisplayUnitsAsync (h, units, t));
		}
	public Task SaveHomeLocationAsync ()
		{
		if (!decimal.TryParse (LatitudeDraft, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal lat) || !decimal.TryParse (LongitudeDraft, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal lon))
			{
			AdministrationMessage = "Enter numeric latitude and longitude.";
			Changed ();
			return Task.CompletedTask;
			}
		return SaveAdministrationAsync ((h, t) => _client.SetHomeLocationAsync (h, lat, lon, t));
		}
	public Task AddRoomAsync ()
		{
		string name = RoomNameDraft;
		return SaveAdministrationAsync ((h, t) => _client.CreateRoomAsync (h, name, t));
		}
	public Task RenameRoomAsync ()
		{
		if (SelectedRoom is null || !Rooms.Contains (SelectedRoom))
			return Task.CompletedTask;
		long id = SelectedRoom.Id;
		string name = RoomNameDraft;
		return SaveAdministrationAsync ((h, t) => _client.RenameRoomAsync (h, id, name, t));
		}
	public Task DeleteRoomAsync ()
		{
		if (SelectedRoom is null || !Rooms.Contains (SelectedRoom))
			return Task.CompletedTask;
		long id = SelectedRoom.Id;
		return SaveAdministrationAsync ((h, t) => _client.DeleteRoomAsync (h, id, t));
		}
	public Task RenameSelectedHubAsync ()
		{
		var hub = _hub;
		string name = DeviceNameDraft;
		return hub is null ? Task.CompletedTask : SaveAdministrationAsync ((h, t) => _client.RenameHubAsync (h, hub, name, t));
		}
	public Task RenameSelectedTimerAsync ()
		{
		var hub = _hub;
		var timer = _timer;
		string name = DeviceNameDraft;
		return hub is null || timer is null ? Task.CompletedTask : SaveAdministrationAsync ((h, t) => _client.RenameDeviceAsync (h, hub, timer.Address, name, t));
		}
	public Task LoadRoomAssignmentsAsync ()
		{
		var home = _administration;
		var room = SelectedRoom;
		if (!CanWriteAdministration || home is null || room is null || !Rooms.Contains (room))
			return Task.CompletedTask;
		return RunAsync (async token =>
		{
			RoomDeviceChoices.Clear ();
			_assignmentsRoomId = null;
			var assigned = await _client.GetRoomDevicesAsync (home, room.Id, token);
			var hubs = await _client.GetHubsAsync (home.Id, token);
			void Add (RainPointRoomDevice item, string label) => RoomDeviceChoices.Add (new (item, label, assigned.Any (a => a.HubId == item.HubId && a.DeviceId == item.DeviceId && a.Zone == item.Zone)));
			foreach (var hub in hubs)
				{
				Add (new (hub.Id), hub.Name + " (hub)");
				foreach (var device in hub.Devices.Where (d => d.Id is > 0))
					{
					Add (new (hub.Id, device.Id), device.Name + " (whole device)");
					for (int zone = 1; zone <= (device.SupportedZoneCount ?? 0); zone++)
						Add (new (hub.Id, device.Id, zone), device.Name + " · zone " + zone);
					}
				}
			if (RoomDeviceChoices.Count (c => c.IsSelected) != assigned.Count)
				{
				RoomDeviceChoices.Clear ();
				throw new System.InvalidOperationException ("An assigned device is no longer available. Reload the home.");
				}
			_assignmentsRoomId = room.Id;
			AdministrationMessage = "Room assignments loaded. Saving replaces this room's assignment list.";
		}, "Room assignments could not be loaded.");
		}
	public Task SaveRoomAssignmentsAsync ()
		{
		if (!CanSaveRoomAssignments)
			return Task.CompletedTask;
		long id = SelectedRoom!.Id;
		var assignments = RoomDeviceChoices.Where (c => c.IsSelected).Select (c => c.Assignment).ToArray ();
		return SaveAdministrationAsync ((h, t) => _client.SetRoomDevicesAsync (h, id, assignments, t));
		}
	public Task LoadTimeZonesAsync () => !CanReadAdministration ? Task.CompletedTask : RunAsync (async token => { TimeZones.Clear (); SelectedTimeZone = null; foreach (var zone in await _client.GetTimeZonesAsync (token)) TimeZones.Add (zone); }, "Time zones could not be loaded.");
	public Task SaveTimeZoneAsync ()
		{
		var zone = SelectedTimeZone;
		return zone is null || !TimeZones.Contains (zone) ? Task.CompletedTask : SaveAdministrationAsync ((h, t) => _client.SetHomeTimeZoneAsync (h, zone.Name, t));
		}
	public Task LoadCurrenciesAsync () => !CanReadAdministration ? Task.CompletedTask : RunAsync (async token => { Currencies.Clear (); SelectedCurrency = null; foreach (var currency in (await _client.GetHomeOptionsAsync (token)).Currencies) Currencies.Add (currency); }, "Currency catalog could not be loaded.");
	public Task SaveCurrencyAsync ()
		{
		var currency = SelectedCurrency;
		return currency is null || !Currencies.Contains (currency) ? Task.CompletedTask : SaveAdministrationAsync ((h, t) => _client.SetHomeCurrencyAsync (h, currency, t));
		}
	public Task SendInvitationAsync ()
		{
		string email = InviteEmail;
		return SaveAdministrationAsync ((h, t) => _client.InviteMemberAsync (h, email, t));
		}
	public Task ChangeMemberRoleAsync ()
		{
		var member = SelectedMember;
		var role = MemberRoleDraft;
		return member is null || !Members.Contains (member) ? Task.CompletedTask : SaveAdministrationAsync ((h, t) => _client.SetMemberRoleAsync (h, member, role, t));
		}
	public Task RemoveMemberAsync ()
		{
		var member = SelectedMember;
		return member is null || !Members.Contains (member) ? Task.CompletedTask : SaveAdministrationAsync ((h, t) => _client.RemoveMemberAsync (h, member, t));
		}
	public Task TransferOwnershipAsync ()
		{
		var member = SelectedMember;
		return member is null || !Members.Contains (member) ? Task.CompletedTask : SaveAdministrationAsync ((h, t) => _client.TransferHomeOwnershipAsync (h, member, t));
		}
	public Task DeleteHomeAsync () => SaveAdministrationAsync ((h, t) => _client.DeleteHomeAsync (h, t));
	public Task LeaveHomeAsync () => SaveAdministrationAsync ((h, t) => _client.LeaveHomeAsync (h, t));
	public Task CreateHomeAsync ()
		{
		if (!CanReadAccount)
			return Task.CompletedTask;
		string name = NewHomeName, zone = NewHomeTimeZone;
		return RunAsync (async token =>
		{
			await _client.CreateHomeAsync (name, zone, Array.Empty<string> (), token);
			await ReloadHomeListCoreAsync (token);
			AdministrationMessage = "Home created. Select it in the home list.";
		}, "Home creation failed or its outcome is uncertain. Reload homes before trying again.");
		}
	public Task ReloadHomesAsync () => !CanReadAccount ? Task.CompletedTask : RunAsync (ReloadHomeListCoreAsync, "Home discovery failed.");
	private async Task ReloadHomeListCoreAsync (CancellationToken token)
		{
		await StopMonitorCoreAsync ();
		ClearHub ();
		Hubs.Clear ();
		Homes.Clear ();
		_home = null;
		foreach (var home in await _client.GetHomesAsync (token))
			Homes.Add (home);
		}
	public Task LoadInvitationsAsync () => !CanReadAccount ? Task.CompletedTask : RunAsync (async token =>
	{
		Invitations.Clear ();
		SelectedInvitation = null;
		foreach (var invitation in await _client.GetInvitationsAsync (token))
			Invitations.Add (invitation);
		AdministrationMessage = Invitations.Count == 0 ? "No pending invitations reported." : "Select an invitation to accept or decline.";
	}, "Invitation discovery failed.");
	public Task RespondToInvitationAsync (bool accept)
		{
		var invitation = SelectedInvitation;
		if (!CanReadAccount || invitation is null || !Invitations.Contains (invitation))
			return Task.CompletedTask;
		return RunAsync (async token =>
		{
			Invitations.Remove (invitation);
			SelectedInvitation = null;
			await _client.RespondToInvitationAsync (invitation, accept, token);
			AdministrationMessage = "Invitation response accepted. Reload homes and invitations.";
		}, "Invitation response failed or is uncertain. Reload invitations before another attempt.");
		}
	}
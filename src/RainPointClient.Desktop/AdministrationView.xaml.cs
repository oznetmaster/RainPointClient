// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Windows;
using System.Windows.Controls;

using RainPointClient.Desktop.Core;
namespace RainPointClient.Desktop;

public partial class AdministrationView : UserControl
	{
	public AdministrationView () => InitializeComponent ();
	private bool Confirm (string message) => MessageBox.Show (Window.GetWindow (this), message, "Home administration", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
	private async void ReloadHomes_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.ReloadHomesAsync ();
		}
	private async void LoadHome_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.LoadAdministrationAsync ();
		}
	private async void RenameHome_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.SaveHomeNameAsync ();
		}
	private async void Location_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.SaveHomeLocationAsync ();
		}
	private async void Units_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.SaveDisplayUnitsAsync ();
		}
	private async void AddRoom_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.AddRoomAsync ();
		}
	private async void RenameRoom_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.RenameRoomAsync ();
		}
	private async void DeleteRoom_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d && Confirm ("Delete the selected room?"))
			await d.DeleteRoomAsync ();
		}
	private async void Role_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d && Confirm ("Change the selected member’s privileges?"))
			await d.ChangeMemberRoleAsync ();
		}
	private async void RemoveMember_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d && Confirm ("Remove the selected member from this home?"))
			await d.RemoveMemberAsync ();
		}
	private async void Transfer_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d && Confirm ("Transfer ownership of this home to the selected member?"))
			await d.TransferOwnershipAsync ();
		}
	private async void Invite_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d && Confirm ("Send a home invitation to the entered email address?"))
			await d.SendInvitationAsync ();
		}
	private async void Invitations_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.LoadInvitationsAsync ();
		}
	private async void Accept_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d && Confirm ("Accept this home invitation?"))
			await d.RespondToInvitationAsync (true);
		}
	private async void Decline_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d && Confirm ("Decline this home invitation?"))
			await d.RespondToInvitationAsync (false);
		}
	private async void SaveNotifications_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.SaveNotificationPreferencesAsync ();
		}
	private async void CreateHome_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.CreateHomeAsync ();
		}
	private async void LeaveHome_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d && Confirm ("Leave this home and lose access to its devices?"))
			await d.LeaveHomeAsync ();
		}
	private async void DeleteHome_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d && Confirm ("Delete this home? This cannot be undone here."))
			await d.DeleteHomeAsync ();
		}
	private void Notifications_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			d.LoadNotificationPreferences ();
		}
	private async void RoomAssignments_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.LoadRoomAssignmentsAsync ();
		}
	private async void SaveAssignments_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.SaveRoomAssignmentsAsync ();
		}
	private async void HubName_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.RenameSelectedHubAsync ();
		}
	private async void TimeZones_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.LoadTimeZonesAsync ();
		}
	private async void TimeZone_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d && Confirm ("Change the home time zone? This changes when local-time schedules run."))
			await d.SaveTimeZoneAsync ();
		}
	private async void Currencies_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.LoadCurrenciesAsync ();
		}
	private async void Currency_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.SaveCurrencyAsync ();
		}
	private async void TimerName_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.RenameSelectedTimerAsync ();
		}
	}
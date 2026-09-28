// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using RainPointClient.Desktop.Core;

namespace RainPointClient.Desktop;

public partial class MainWindow : Window
	{
	private readonly Dashboard _dashboard;
	private readonly CredentialStore? _credentials;
	private readonly DispatcherTimer _poll = new () { Interval = TimeSpan.FromSeconds (15) };
	private bool _closed;
	private bool _closing;
	private bool _restoringOptions = true;
	private bool _startupAttempted;
	private string? _authenticatedEmail;
	private string? _authenticatedCountry;

	public MainWindow () : this (CreateDesktopDashboard (), CredentialStore.ForCurrentUser ()) { }
	private static Dashboard CreateDesktopDashboard ()
		{
		var dashboard = new Dashboard (new RainPointCloudClient ());
		string? key = Environment.GetEnvironmentVariable ("RAINPOINT_WEATHER_ACCESS_KEY");
		string? secret = Environment.GetEnvironmentVariable ("RAINPOINT_WEATHER_ACCESS_SECRET");
		if (!string.IsNullOrWhiteSpace (key) && !string.IsNullOrEmpty (secret))
			{
			try
				{
				dashboard.ConfigureWeather (new RainPointWeatherAccess (key, secret));
				}
			catch (ArgumentException) { /* Leave weather disabled when optional configuration is invalid. */ }
			}
		return dashboard;
		}

	// Injected-dashboard tests have no access to the user's real saved account.
	public MainWindow (Dashboard dashboard) : this (dashboard, null) { }

	internal MainWindow (Dashboard dashboard, CredentialStore? credentials)
		{
		_dashboard = dashboard;
		_credentials = credentials;
		InitializeComponent ();
		_restoringOptions = false;
		DataContext = dashboard;
		AccountView.BeforeCredentialChange = PrepareCredentialChange;
		_poll.Tick += Poll_Tick;
		_poll.Start ();
		Closing += OnClosing;
		Loaded += OnLoaded;
		}

	private async void SignIn_Click (object sender, RoutedEventArgs e)
		=> await SignInAsync ();

	private async Task SignInAsync ()
		{
		if (!_dashboard.CanConnect)
			return;
		string email = Email.Text.Trim ();
		string country = Country.Text.Trim ();
		string password = Password.Password;
		Password.Clear ();
		if (password.Length == 0 && RememberAccount.IsChecked == true && _credentials is not null)
			{
			try
				{
				SavedAccount? saved = _credentials.Load ();
				if (saved is not null && string.Equals (saved.Email, email, StringComparison.OrdinalIgnoreCase) && saved.CountryCode == country)
					password = saved.Password;
				}
			catch (Exception error) when (IsStorageError (error))
				{
				SavedAccountMessage.Text = "Saved credentials could not be read. Enter the password again or forget the saved account.";
				return;
				}
			}
		bool authenticated = await _dashboard.ConnectAsync (email, password, country);
		if (!authenticated || _closing)
			return;
		_authenticatedEmail = email;
		_authenticatedCountry = country;
		if (_credentials is null)
			{
			await ConfigureRecoveryAsync ();
			return;
			}
		try
			{
			if (RememberAccount.IsChecked == true)
				{
				_credentials.Save (new SavedAccount { Email = email, Password = password, CountryCode = country, AutomaticSignIn = AutomaticSignIn.IsChecked == true });
				SavedAccountMessage.Text = "Credentials saved, encrypted for your Windows account.";
				}
			else
				{
				_credentials.Forget ();
				SavedAccountMessage.Text = "Credentials are not saved.";
				}
			}
		catch (Exception error) when (IsStorageError (error))
			{
			SavedAccountMessage.Text = "Signed in, but saved credentials could not be updated. Check access to your local app-data folder.";
			}
		await ConfigureRecoveryAsync ();
		}

	private async void Recovery_Changed (object sender, RoutedEventArgs e)
		{
		if (!_restoringOptions && !_closing)
			await ConfigureRecoveryAsync ();
		}
	private async Task ConfigureRecoveryAsync ()
		{
		await _dashboard.StopSessionRecoveryAsync ();
		if (AutomaticRenewal.IsChecked != true || _authenticatedEmail is null || !_dashboard.CanDisconnect)
			return;
		string email = _authenticatedEmail, country = _authenticatedCountry!;
		await _dashboard.StartSessionRecoveryAsync (action => { _ = Dispatcher.BeginInvoke (action); }, async token =>
		 {
			 return await Dispatcher.InvokeAsync (() =>
		 {
			 token.ThrowIfCancellationRequested ();
			 if (_closing || ReconnectSaved.IsChecked != true || RememberAccount.IsChecked != true || _credentials is null)
				 return null;
			 var saved = _credentials.Load ();
			 return saved is not null && string.Equals (saved.Email, email, StringComparison.OrdinalIgnoreCase) && saved.CountryCode == country ? new RainPointCredentials (saved.Email, saved.Password, saved.CountryCode) : null;
		 });
		 });
		}

	private async void OnLoaded (object sender, RoutedEventArgs e) => await InitializeAccountAsync ();

	internal async Task InitializeAccountAsync ()
		{
		if (_startupAttempted || _closing)
			return;
		_startupAttempted = true;
		if (_credentials is null)
			return;
		SavedAccount? saved;
		try
			{
			saved = _credentials.Load ();
			}
		catch (Exception error) when (IsStorageError (error))
			{
			SavedAccountMessage.Text = "Saved credentials could not be read for this Windows account. Enter them again or choose Forget saved account.";
			return;
			}
		if (saved is null)
			return;
		_restoringOptions = true;
		try
			{
			Email.Text = saved.Email;
			Country.Text = saved.CountryCode;
			Password.Password = saved.Password;
			RememberAccount.IsChecked = true;
			AutomaticSignIn.IsChecked = saved.AutomaticSignIn;
			}
		finally { _restoringOptions = false; }
		SavedAccountMessage.Text = "Saved credentials loaded for this Windows account.";
		if (saved.AutomaticSignIn)
			await SignInAsync ();
		}

	private void RememberAccount_Changed (object sender, RoutedEventArgs e)
		{
		if (_restoringOptions)
			return;
		if (RememberAccount.IsChecked == true)
			{
			SavedAccountMessage.Text = "Enter your password and sign in to save credentials.";
			return;
			}
		ForgetCredentials (false);
		}

	private void AutomaticSignIn_Changed (object sender, RoutedEventArgs e)
		{
		if (_restoringOptions || _credentials is null)
			return;
		try
			{
			SavedAccount? saved = _credentials.Load ();
			if (saved is null)
				return;
			saved.AutomaticSignIn = AutomaticSignIn.IsChecked == true;
			_credentials.Save (saved);
			SavedAccountMessage.Text = saved.AutomaticSignIn ? "Automatic sign-in enabled for the saved account." : "Automatic sign-in disabled. Saved credentials are retained.";
			}
		catch (Exception error) when (IsStorageError (error))
			{
			SavedAccountMessage.Text = "The automatic sign-in preference could not be saved. Forget the saved account or correct local file access before restarting.";
			}
		}

	private void ForgetAccount_Click (object sender, RoutedEventArgs e) => ForgetCredentials (true);

	private void ForgetCredentials (bool clearFields)
		{
		try
			{
			_credentials?.Forget ();
			_restoringOptions = true;
			try
				{
				RememberAccount.IsChecked = false;
				AutomaticSignIn.IsChecked = false;
				if (clearFields)
					{
					Email.Clear ();
					Password.Clear ();
					}
				}
			finally { _restoringOptions = false; }
			SavedAccountMessage.Text = "Saved credentials removed. Your current cloud session is unchanged.";
			}
		catch (Exception error) when (IsStorageError (error))
			{
			SavedAccountMessage.Text = "Saved credentials could not be removed. Check local file access and try Forget again.";
			}
		}

	private void PrepareCredentialChange ()
		{
		// Fail before the cloud write if the obsolete saved secret cannot be removed.
		_credentials?.Forget ();
		_authenticatedEmail = _authenticatedCountry = null;
		_restoringOptions = true;
		try
			{
			RememberAccount.IsChecked = false;
			AutomaticSignIn.IsChecked = false;
			ReconnectSaved.IsChecked = false;
			Password.Clear ();
			}
		finally { _restoringOptions = false; }
		SavedAccountMessage.Text = "Saved credentials removed before the password operation. Sign in explicitly afterwards.";
		}

	private static bool IsStorageError (Exception error) => error is System.IO.IOException
		or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException
		or System.Text.Json.JsonException or System.Security.SecurityException;

	private async void Home_SelectionChanged (object sender, SelectionChangedEventArgs e)
		{
		if (_dashboard.CanEdit && !ReferenceEquals (Home.SelectedItem, _dashboard.SelectedHome))
			await _dashboard.SelectHomeAsync (Home.SelectedItem as RainPointHome);
		}

	private async void Hub_SelectionChanged (object sender, SelectionChangedEventArgs e)
		{
		if (_dashboard.CanEdit && !ReferenceEquals (Hub.SelectedItem, _dashboard.SelectedHub))
			await _dashboard.SelectHubAsync (Hub.SelectedItem as RainPointHub);
		}

	private void Timer_SelectionChanged (object sender, SelectionChangedEventArgs e)
		{
		if (_dashboard.CanEdit && !ReferenceEquals (Timer.SelectedItem, _dashboard.SelectedTimer))
			_dashboard.SelectTimer (Timer.SelectedItem as RainPointDevice);
		}

	private async void LiveFeedback_Click (object sender, RoutedEventArgs e)
		{
		AutoRefresh.IsChecked = false;
		if (_dashboard.IsMonitoring)
			await _dashboard.StopMonitoringAsync ();
		else
			await _dashboard.StartMonitoringAsync (action => { _ = Dispatcher.BeginInvoke (action); });
		}

	private async void Refresh_Click (object sender, RoutedEventArgs e) => await RefreshAsync ();
	private async void Renew_Click (object sender, RoutedEventArgs e) => await _dashboard.RenewAsync ();
	private async void SignOut_Click (object sender, RoutedEventArgs e)
		{
		AutoRefresh.IsChecked = false;
		_authenticatedEmail = _authenticatedCountry = null;
		AutomaticSignIn.IsChecked = false;
		await _dashboard.DisconnectAsync ();
		}
	private async void Start_Click (object sender, RoutedEventArgs e) => await _dashboard.StartSelectedZoneAsync ();
	private async void Stop_Click (object sender, RoutedEventArgs e) => await _dashboard.StopSelectedZoneAsync ();
	private async void Poll_Tick (object? sender, EventArgs e)
		{
		if (AutoRefresh.IsChecked == true && !_dashboard.IsMonitoring && _dashboard.CanRefresh)
			await RefreshAsync ();
		}

	private async Task RefreshAsync ()
		{
		DateTimeOffset? previous = _dashboard.LastReceivedAt;
		await _dashboard.RefreshAsync ();
		if (previous == _dashboard.LastReceivedAt)
			AutoRefresh.IsChecked = false;
		}

	private async void OnClosing (object? sender, CancelEventArgs e)
		{
		if (_closed)
			return;
		e.Cancel = true;
		if (_closing)
			return;
		_closing = true;
		_poll.Stop ();
		await _dashboard.CloseAsync ();
		_closed = true;
		_ = Dispatcher.BeginInvoke (new Action (Close));
		}
	}
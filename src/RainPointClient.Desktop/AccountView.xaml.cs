using System;
using System.Windows;
using System.Windows.Controls;

using RainPointClient.Desktop.Core;
namespace RainPointClient.Desktop;

public partial class AccountView : UserControl
	{
	internal Action? BeforeCredentialChange
		{
		get; set;
		}
	private Dashboard Model => (Dashboard)DataContext;
	public AccountView ()
		{
		InitializeComponent ();
		}
	private async void Nickname_Click (object sender, RoutedEventArgs e) => await Model.SetAccountNicknameAsync (Nickname.Text);
	private async void Photo_Click (object sender, RoutedEventArgs e) => await Model.SetAccountPhotoAsync (PhotoUrl.Text);
	private async void Request_Click (object sender, RoutedEventArgs e) => await Model.SendAccountCodeAsync ();
	private async void Verify_Click (object sender, RoutedEventArgs e)
		{
		string code = VerificationCode.Password;
		VerificationCode.Clear ();
		await Model.VerifyAccountCodeAsync (code);
		}
	private async void Instructions_Click (object sender, RoutedEventArgs e) => await Model.GetAccountRegistrationEmailAsync ();
	private async void CheckEmail_Click (object sender, RoutedEventArgs e) => await Model.CheckAccountRegistrationEmailAsync ();
	private async void Complete_Click (object sender, RoutedEventArgs e)
		{
		string password = NewAccountPassword.Password;
		NewAccountPassword.Clear ();
		await Model.CompleteAccountAsync (password, RegistrationCountry.Text, AgreementAccepted.IsChecked == true, BeforeCredentialChange);
		}
	private async void Change_Click (object sender, RoutedEventArgs e)
		{
		string oldPassword = OldPassword.Password, newPassword = ChangedPassword.Password;
		OldPassword.Clear ();
		ChangedPassword.Clear ();
		await Model.ChangeAccountPasswordAsync (oldPassword, newPassword, BeforeCredentialChange);
		}
	}
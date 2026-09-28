// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Windows;
using System.Windows.Controls;

using RainPointClient.Desktop.Core;
namespace RainPointClient.Desktop;

public partial class PairingView : UserControl
	{
	public PairingView ()
		{
		InitializeComponent ();
		}
	internal Func<string, bool>? ConfirmForTest
		{
		get; set;
		}
	private bool Confirm (string message) => ConfirmForTest?.Invoke (message) ?? MessageBox.Show (Window.GetWindow (this), message, "Device pairing", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
	private Dashboard Model => (Dashboard)DataContext;
	private async void Load_Click (object sender, RoutedEventArgs e) => await Model.LoadPairingAsync ();
	private async void Start_Click (object sender, RoutedEventArgs e)
		{
		if (Confirm ("Start RF pairing on the selected hub?"))
			await Model.StartPairingAsync ();
		}
	private async void Cancel_Click (object sender, RoutedEventArgs e) => await Model.CancelPairingAsync ();
	private async void RemoveChild_Click (object sender, RoutedEventArgs e)
		{
		if (Confirm ("Remove the selected child and clear its sensor associations? You must pair it again to restore access."))
			await Model.RemovePairedDeviceAsync (false);
		}
	private async void RemoveHub_Click (object sender, RoutedEventArgs e)
		{
		if (Confirm ("Remove this hub and all its children from the home? You must set them up again to restore access."))
			await Model.RemovePairedDeviceAsync (true);
		}
	}
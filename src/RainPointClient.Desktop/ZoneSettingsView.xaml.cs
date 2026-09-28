// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Windows;
using System.Windows.Controls;

using RainPointClient.Desktop.Core;

namespace RainPointClient.Desktop;

public partial class ZoneSettingsView : UserControl
	{
	public ZoneSettingsView () => InitializeComponent ();
	private async void LoadMoistureRule_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard dashboard)
			await dashboard.LoadMoistureRuleAsync ();
		}
	private async void SaveMoistureRule_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard dashboard)
			await dashboard.SaveMoistureRuleAsync ();
		}
	private async void LoadSoilSensors_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard dashboard)
			await dashboard.LoadSoilSensorsAsync ();
		}
	private async void SaveSoilSensor_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard dashboard)
			await dashboard.SaveSoilSensorAsync ();
		}
	private async void LoadSettings_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard dashboard)
			await dashboard.LoadSettingsAsync ();
		}
	private async void SaveSetting_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard dashboard)
			await dashboard.SaveSettingAsync ();
		}
	}
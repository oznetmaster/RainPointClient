// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Windows;
using System.Windows.Controls;

using RainPointClient.Desktop.Core;
namespace RainPointClient.Desktop;

public partial class ScenesView : UserControl
	{
	public ScenesView () => InitializeComponent ();
	private Dashboard? Dashboard => DataContext as Dashboard;
	private static bool Confirm (string message) => MessageBox.Show (message, "Smart Scene", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
	private async void Load_Click (object s, RoutedEventArgs e)
		{
		if (Dashboard is { } d)
			await d.LoadScenesAsync ();
		}
	private async void Detail_Click (object s, RoutedEventArgs e)
		{
		if (Dashboard is { } d)
			await d.LoadSelectedSceneAsync ();
		}
	private async void History_Click (object s, RoutedEventArgs e)
		{
		if (Dashboard is { } d)
			await d.LoadSceneHistoryAsync ();
		}
	private async void NextHistory_Click (object s, RoutedEventArgs e)
		{
		if (Dashboard is { } d)
			await d.NextSceneHistoryAsync ();
		}
	private void Edit_Click (object s, RoutedEventArgs e) => Dashboard?.EditSelectedScene ();
	private void New_Click (object s, RoutedEventArgs e) => Dashboard?.NewSceneDraft ();
	private async void Enable_Click (object s, RoutedEventArgs e)
		{
		if (Dashboard is { } d && Confirm ("Enable this scene and allow its actions, including any watering or messages, to run automatically?"))
			await d.SetSelectedSceneEnabledAsync (true);
		}
	private async void Disable_Click (object s, RoutedEventArgs e)
		{
		if (Dashboard is { } d && Confirm ("Disable this scene? This will not stop a valve already running."))
			await d.SetSelectedSceneEnabledAsync (false);
		}
	private async void Delete_Click (object s, RoutedEventArgs e)
		{
		if (Dashboard is { } d && Confirm ("Permanently delete this scene?"))
			await d.DeleteSelectedSceneAsync ();
		}
	private async void Save_Click (object s, RoutedEventArgs e)
		{
		if (Dashboard is { } d && Confirm ("Save this scene definition and allow its actions to run automatically? Saving is not a disabled-draft operation."))
			await d.SaveNewSceneAsync ();
		}
	private async void WeatherTypes_Click (object s, RoutedEventArgs e)
		{
		if (Dashboard is { } d)
			await d.LoadSceneWeatherTypesAsync ();
		}
	private void WeatherType_Click (object s, RoutedEventArgs e) => Dashboard?.AddSceneWeatherTypeCondition ();
	private void Weather_Click (object s, RoutedEventArgs e) => Dashboard?.AddSceneWeatherCondition ();
	private void Time_Click (object s, RoutedEventArgs e) => Dashboard?.AddSceneTimeCondition ();
	private void Once_Click (object s, RoutedEventArgs e) => Dashboard?.AddSceneOnceCondition ();
	private void Notify_Click (object s, RoutedEventArgs e) => Dashboard?.AddSceneNotification ();
	private void Delay_Click (object s, RoutedEventArgs e) => Dashboard?.AddSceneRainDelay ();
	private void RemoveCondition_Click (object s, RoutedEventArgs e) => Dashboard?.RemoveSceneCondition ();
	private void RemoveAction_Click (object s, RoutedEventArgs e) => Dashboard?.RemoveSceneAction ();
	}
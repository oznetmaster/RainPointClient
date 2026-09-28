using System.Windows;
using System.Windows.Controls;

using RainPointClient.Desktop.Core;

namespace RainPointClient.Desktop;

public partial class HistoryView : UserControl
	{
	public HistoryView () => InitializeComponent ();
	private async void LoadUsage_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard dashboard)
			await dashboard.LoadUsageAsync ();
		}
	private async void LoadEvents_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard dashboard)
			await dashboard.LoadEventsAsync ();
		}
	private async void LoadOlderEvents_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard dashboard)
			await dashboard.LoadOlderEventsAsync ();
		}
	}
using System.Windows;
using System.Windows.Controls;

using RainPointClient.Desktop.Core;
namespace RainPointClient.Desktop;

public partial class HubToolsView : UserControl
	{
	public HubToolsView () => InitializeComponent ();
	private async void RfChannel_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.SaveRfChannelAsync ();
		}
	private async void Catalog_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.LoadProductCatalogAsync ();
		}
	private async void Load_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.LoadHubSettingsAsync ();
		}
	private async void Save_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.SaveBroadcastAsync ();
		}
	private async void Broadcast_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.BroadcastTimeAsync ();
		}
	private async void HubFirmware_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.CheckFirmwareAsync (false);
		}
	private async void TimerFirmware_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.CheckFirmwareAsync (true);
		}
	}
using System.Windows;
using System.Windows.Controls;

using RainPointClient.Desktop.Core;
namespace RainPointClient.Desktop;

public partial class ProfileView : UserControl
	{
	public ProfileView () => InitializeComponent ();
	private async void Load_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.LoadProfileAsync ();
		}
	private async void Save_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.SaveProfileAsync ();
		}
	private async void Recommendation_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.LoadRecommendationsAsync ();
		}
	private async void Prepare_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.PrepareRecommendedPlanAsync ();
		}
	}
using System.Windows;
using System.Windows.Controls;

using RainPointClient.Desktop.Core;

namespace RainPointClient.Desktop;

public partial class PlansView : UserControl
	{
	public PlansView () => InitializeComponent ();
	private async void LoadPlans_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.LoadPlansAsync ();
		}
	private void NewPlan_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			d.NewPlan ();
		}
	private async void SavePlan_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.SavePlanAsync ();
		}
	private async void TogglePlan_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.TogglePlanAsync ();
		}
	private async void DeletePlan_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.DeletePlanAsync ();
		}
	}
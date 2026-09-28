using System.Windows;
using System.Windows.Controls;

using RainPointClient.Desktop.Core;
namespace RainPointClient.Desktop;

public partial class CalendarView : UserControl
	{
	public CalendarView () => InitializeComponent ();
	private async void Load_Click (object sender, RoutedEventArgs e)
		{
		if (DataContext is Dashboard d)
			await d.LoadCalendarAsync ();
		}
	private void Selection_Changed (object sender, SelectionChangedEventArgs e)
		{
		if (sender is Calendar { SelectedDate: { } date } calendar)
			calendar.DisplayDate = date;
		}
	}
// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using NUnit.Framework;

using RainPointClient.Desktop;
using RainPointClient.Desktop.Core;

namespace RainPointClient.Desktop.Tests;

[TestFixture, Apartment (ApartmentState.STA), NonParallelizable]
public sealed class SettingsWindowTests
	{
	private const string Port = "58020a001e0000800000000000d7,/,aux,646464646464646464646464,tail";
	private static readonly string Parameter = string.Join ("|", Enumerable.Repeat (Port, 3));

	[TestCase (1, "Default duration", "11", "", "11 minutes")]
	[TestCase (2, "Default duration", "11", "", "11 minutes")]
	[TestCase (3, "Default duration", "11", "", "11 minutes")]
	[TestCase (1, "Misting intervals", "15", "45", "On: 15 seconds · Off: 45 seconds")]
	[TestCase (2, "Misting intervals", "15", "45", "On: 15 seconds · Off: 45 seconds")]
	[TestCase (3, "Misting intervals", "15", "45", "On: 15 seconds · Off: 45 seconds")]
	[TestCase (1, "Flow calibration", "-2", "", "-2%")]
	[TestCase (2, "Flow calibration", "-2", "", "-2%")]
	[TestCase (3, "Flow calibration", "-2", "", "-2%")]
	public void EditorBindingsValidateSaveAndShowReadBack (int zone, string kind, string first, string second, string expected)
		{
		using Host host = new ();
		host.SignInAndSelect ();
		host.Control<ComboBox> ("SettingsZonePicker").SelectedItem = zone;
		host.Settle ();
		Assert.That (host.Control<Button> ("SaveSetting").IsEnabled, Is.False);
		host.ReplyDiscovery (Parameter);
		host.Click ("LoadSettings");
		Assert.That (host.Control<TextBlock> ("SavedDuration").Text, Is.EqualTo ("10 minutes"));
		host.Control<ComboBox> ("SettingPicker").SelectedItem = kind;
		host.Settle ();
		Assert.That (host.Control<StackPanel> ("SecondValuePanel").Visibility, Is.EqualTo (kind == "Misting intervals" ? Visibility.Visible : Visibility.Collapsed));
		host.Control<TextBox> ("SettingValue").Text = "invalid";
		host.Settle ();
		Assert.That (host.Control<Button> ("SaveSetting").IsEnabled, Is.False);
		host.Control<TextBox> ("SettingValue").Text = first;
		host.Control<TextBox> ("SettingSecondValue").Text = second;
		host.Settle ();
		Assert.That (host.Control<Button> ("SaveSetting").IsEnabled, Is.True);
		string field = kind switch
			{
				"Default duration" => "94020a001e0000800000000000d7",
				"Misting intervals" => "58020f002d0000800000000000d7",
				_ => "58020a001e00008000000000fed7"
				};
		string[] ports = Parameter.Split ('|');
		ports[zone - 1] = field + Port.Substring (Port.IndexOf (','));
		string after = string.Join ("|", ports);
		host.ReplyDiscovery (Parameter);
		host.Network.Reply ("{\"code\":0}");
		host.ReplyDiscovery (after);
		host.Click ("SaveSetting");
		Assert.That (host.Control<TextBlock> ("SettingsMessage").Text, Does.Contain ("matches the cloud read-back"));
		string control = kind == "Default duration" ? "SavedDuration" : kind == "Misting intervals" ? "SavedMisting" : "SavedCalibration";
		Assert.That (host.Control<TextBlock> (control).Text, Is.EqualTo (expected));
		Assert.That (host.Network.PostPaths.Count (x => x == "/app/device/sub/update"), Is.EqualTo (1));
		Assert.That (host.Network.PostPaths.Any (x => x.Contains ("controlWorkMode")), Is.False);
		host.Render ("rainpoint-settings-" + kind.Replace (" ", "-") + "-zone" + zone + ".png");
		}

	[TestCase (2)]
	[TestCase (3)]
	public void ZoneSelectionClearsOldValuesUntilReload (int zone)
		{
		using Host host = new ();
		host.SignInAndSelect ();
		host.ReplyDiscovery (Parameter);
		host.Click ("LoadSettings");
		host.Control<ComboBox> ("SettingsZonePicker").SelectedItem = zone;
		host.Settle ();
		Assert.That (host.Control<TextBlock> ("SavedCalibration").Text, Is.EqualTo ("Unknown"));
		Assert.That (host.Control<TextBox> ("SettingValue").IsEnabled, Is.False);
		host.ReplyDiscovery (Parameter);
		host.Click ("LoadSettings");
		Assert.That (host.Control<TextBlock> ("SavedDuration").Text, Is.EqualTo ("10 minutes"));
		Assert.That (host.Control<Button> ("SaveSetting").IsEnabled, Is.True);
		Assert.That (host.Control<TextBox> ("SettingValue").IsEnabled, Is.True);
		Assert.That (host.Network.PostPaths, Has.Count.EqualTo (1), "Only the fixture sign-in may POST.");
		}

	[TestCase (1, false)]
	[TestCase (2, false)]
	[TestCase (3, false)]
	[TestCase (1, true)]
	[TestCase (2, true)]
	[TestCase (3, true)]
	public void SeasonalAndRainDelayControlsSaveInEveryZone (int zone, bool rain)
		{
		using Host host = new ();
		host.SignInAndSelect ();
		host.Control<ComboBox> ("SettingsZonePicker").SelectedItem = zone;
		host.Settle ();
		host.ReplyDiscovery (Parameter);
		host.Click ("LoadSettings");
		host.Control<ComboBox> ("SettingPicker").SelectedItem = rain ? "Rain delay" : "Seasonal adjustment";
		host.Settle ();
		string[] ports = Parameter.Split ('|');
		if (rain)
			{
			host.Control<TextBox> ("SettingValue").Text = "2020-01-01 00:00:00";
			ports[zone - 1] = Port.Replace ("58020a001e0000800000000000d7", "58020a001e0000800000420000d7");
			}
		else
			{
			var months = host.Control<ItemsControl> ("SeasonMonths");
			months.Measure (new Size (900, 400));
			months.Arrange (new Rect (0, 0, 900, 400));
			months.UpdateLayout ();
			host.Settle ();
			var inputs = Descendants<TextBox> (months).ToArray ();
			Assert.That (inputs, Has.Length.EqualTo (12));
			inputs[11].Text = "invalid";
			host.Settle ();
			Assert.That (host.Control<Button> ("SaveSetting").IsEnabled, Is.False);
			foreach (var input in inputs)
				input.Text = "90";
			ports[zone - 1] = Port.Replace ("646464646464646464646464", "5a5a5a5a5a5a5a5a5a5a5a5a");
			}
		host.Settle ();
		Assert.That (host.Control<Button> ("SaveSetting").IsEnabled, Is.True);
		host.ReplyDiscovery (Parameter);
		host.Network.Reply ("{\"code\":0}");
		host.ReplyDiscovery (string.Join ("|", ports));
		host.Click ("SaveSetting");
		Assert.That (host.Control<TextBlock> ("SettingsMessage").Text, Does.Contain ("matches the cloud read-back"));
		Assert.That (host.Control<TextBlock> (rain ? "SavedRainDelay" : "SavedSeasonal").Text, Does.Contain (rain ? "2020-01-01" : "December 90%"));
		host.Render ("rainpoint-" + (rain ? "rain-delay" : "seasonal") + "-zone" + zone + ".png");
		}
	private static IEnumerable<T> Descendants<T> (DependencyObject root) where T : DependencyObject
		{
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount (root); i++)
			{
			var child = VisualTreeHelper.GetChild (root, i);
			if (child is T value)
				yield return value;
			foreach (var descendant in Descendants<T> (child))
				yield return descendant;
			}
		}

	private sealed class Host : IDisposable
		{
		internal readonly FixtureNetwork Network = new ();
		private readonly HttpClient _http;
		private readonly Dashboard _dashboard;
		private readonly MainWindow _window;
		private readonly SynchronizationContext? _previous;
		private ZoneSettingsView View => (ZoneSettingsView)_window.FindName ("ZoneSettings");
		internal Host ()
			{
			_http = new (Network, false);
			_dashboard = new (new RainPointCloudClient (_http));
			_window = new (_dashboard);
			_previous = SynchronizationContext.Current;
			SynchronizationContext.SetSynchronizationContext (new DispatcherSynchronizationContext (_window.Dispatcher));
			Settle ();
			}
		internal T Control<T> (string name) where T : FrameworkElement => (T)View.FindName (name);
		internal void Click (string name)
			{
			Control<Button> (name).RaiseEvent (new RoutedEventArgs (Button.ClickEvent));
			Settle ();
			}
		internal void SignInAndSelect ()
			{
			Network.Reply ("""{"code":0,"data":{"token":"fixture","tokenExpired":3600}}""");
			Network.Reply ("""{"code":0,"data":[{"hid":5,"homeName":"Test garden"}]}""");
			((TextBox)_window.FindName ("Email")).Text = "fixture@example.invalid";
			((PasswordBox)_window.FindName ("Password")).Password = "fixture";
			((Button)_window.FindName ("SignIn")).RaiseEvent (new RoutedEventArgs (Button.ClickEvent));
			Settle ();
			ReplyDiscovery (Parameter);
			((ComboBox)_window.FindName ("Home")).SelectedIndex = 0;
			Settle ();
			((ComboBox)_window.FindName ("Hub")).SelectedIndex = 0;
			Settle ();
			((ComboBox)_window.FindName ("Timer")).SelectedIndex = 0;
			Settle ();
			((TabControl)_window.FindName ("WorkbenchTabs")).SelectedIndex = 1;
			Settle ();
			}
		internal void ReplyDiscovery (string parameter) => Network.Reply (JsonSerializer.Serialize (new
			{
			code = 0,
			data = new[]{new{mid=101,name="Garden hub",deviceName="fixture",productKey="fixture",model="HWG023WBRF",subDevices=new[]
	 {new{sid=42,addr=2,model="HTV345FRF",name="Garden timer",portNumber=3,softVer="130",param=parameter}}}}
			}));
		internal void Settle ()
			{
			DispatcherFrame frame = new ();
			DateTime deadline = DateTime.UtcNow.AddSeconds (10);
			DispatcherTimer pump = new (DispatcherPriority.ApplicationIdle)
				{
				Interval = TimeSpan.FromMilliseconds (10)
				};
			pump.Tick += (_, _) => { if (!_dashboard.IsBusy || DateTime.UtcNow >= deadline) frame.Continue = false; };
			pump.Start ();
			try
				{
				Dispatcher.PushFrame (frame);
				}
			finally { pump.Stop (); }
			Assert.That (_dashboard.IsBusy, Is.False, "The simulated UI operation timed out.");
			}
		internal void Render (string filename)
			{
			FrameworkElement content = (FrameworkElement)_window.Content;
			content.Measure (new Size (1100, 1500));
			content.Arrange (new Rect (0, 0, 1100, 1500));
			content.UpdateLayout ();
			Assert.That (View.ActualWidth, Is.GreaterThan (600));
			Assert.That (View.ActualHeight, Is.GreaterThan (400));
			RenderTargetBitmap bitmap = new ((int)Math.Ceiling (View.ActualWidth), (int)Math.Ceiling (View.ActualHeight), 96, 96, PixelFormats.Pbgra32);
			bitmap.Render (View);
			PngBitmapEncoder encoder = new ();
			encoder.Frames.Add (BitmapFrame.Create (bitmap));
			string path = Path.Combine (TestContext.CurrentContext.WorkDirectory, Path.GetFileNameWithoutExtension (filename) + "-" + Guid.NewGuid ().ToString ("N") + Path.GetExtension (filename));
			using (FileStream stream = File.Create (path))
				encoder.Save (stream);
			TestContext.AddTestAttachment (path, "Offline zone-settings rendering with simulated cloud data");
			}
		public void Dispose ()
			{
			try
				{
				_window.Close ();
				Task closed = _dashboard.CloseAsync ();
				Settle ();
				closed.GetAwaiter ().GetResult ();
				}
			finally { SynchronizationContext.SetSynchronizationContext (_previous); _http.Dispose (); Network.Dispose (); }
			}
		}
	private sealed class FixtureNetwork : HttpMessageHandler
		{
		private readonly Queue<string> _responses = new ();
		internal List<string> PostPaths { get; } = new ();
		internal void Reply (string json) => _responses.Enqueue (json);
		protected override Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			if (request.Method == HttpMethod.Post)
				PostPaths.Add (request.RequestUri!.AbsolutePath);
			if (_responses.Count == 0)
				throw new InvalidOperationException ("Unexpected offline request.");
			return Task.FromResult (new HttpResponseMessage (System.Net.HttpStatusCode.OK) { Content = new StringContent (_responses.Dequeue ()) });
			}
		}
	}
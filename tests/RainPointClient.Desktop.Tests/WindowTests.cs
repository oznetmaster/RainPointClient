using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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
public sealed class WindowTests
	{
	[Test]
	public void SignedOutWindowLoadsBindingsRendersAndCannotSendCommands ()
		{
		using RejectNetwork handler = new ();
		using HttpClient http = new (handler);
		Dashboard dashboard = new (new RainPointCloudClient (http));
		MainWindow window = new (dashboard);
		try
			{
			// Render offscreen. No visible window, emulator, credentials or hardware is needed.
			_ = window.Dispatcher.Invoke (DispatcherPriority.ApplicationIdle, new Action (() => { }));
			window.Measure (new Size (window.Width, window.Height));
			window.Arrange (new Rect (0, 0, window.Width, window.Height));
			window.UpdateLayout ();
			using (Assert.EnterMultipleScope ())
				{
				Assert.That (((Button)window.FindName ("StartSelectedZone")).IsEnabled, Is.False);
				Assert.That (((Button)window.FindName ("StopSelectedZone")).IsEnabled, Is.False);
				Assert.That (((Button)window.FindName ("SignIn")).IsEnabled, Is.True);
				Assert.That (((TextBlock)window.FindName ("StatusMessage")).Text, Does.Contain ("Sign in"));
				Assert.That (((PasswordBox)window.FindName ("Password")).Password, Is.Empty);
				Assert.That (handler.RequestCount, Is.Zero);
				}
			FrameworkElement content = (FrameworkElement)window.Content;
			content.Measure (new Size (window.Width, window.Height));
			content.Arrange (new Rect (0, 0, window.Width, window.Height));
			content.UpdateLayout ();
			RenderTargetBitmap bitmap = new ((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32);
			bitmap.Render (content);
			PngBitmapEncoder encoder = new ();
			encoder.Frames.Add (BitmapFrame.Create (bitmap));
			string path = Path.Combine (TestContext.CurrentContext.WorkDirectory, "rainpoint-desktop-signed-out-" + Guid.NewGuid ().ToString ("N") + ".png");
			using (FileStream stream = File.Create (path))
				encoder.Save (stream);
			TestContext.AddTestAttachment (path, "Offline signed-out WPF rendering");
			}
		finally
			{
			window.Close ();
			dashboard.CloseAsync ().GetAwaiter ().GetResult ();
			}
		}

	[TestCase (1, 0)]
	[TestCase (2, 0)]
	[TestCase (3, 0)]
	[TestCase (1, 1)]
	[TestCase (2, 1)]
	[TestCase (3, 1)]
	[TestCase (1, 2)]
	[TestCase (2, 2)]
	[TestCase (3, 2)]
	public void DiscoverySelectionsAndControlGatesWorkThroughWindowBindings (int zone, int mode)
		{
		using FixtureNetwork handler = new ();
		using HttpClient http = new (handler);
		Dashboard dashboard = new (new RainPointCloudClient (http));
		MainWindow window = new (dashboard);
		SynchronizationContext? previousContext = SynchronizationContext.Current;
		SynchronizationContext.SetSynchronizationContext (new DispatcherSynchronizationContext (window.Dispatcher));
		void Settle ()
			{
			DispatcherFrame frame = new ();
			DateTime deadline = DateTime.UtcNow.AddSeconds (10);
			DispatcherTimer pump = new (DispatcherPriority.ApplicationIdle)
				{
				Interval = TimeSpan.FromMilliseconds (10)
				};
			pump.Tick += (_, _) =>
			{
				if (!dashboard.IsBusy || DateTime.UtcNow >= deadline)
					frame.Continue = false;
			};
			pump.Start ();
			try
				{
				Dispatcher.PushFrame (frame);
				}
			finally { pump.Stop (); }
			Assert.That (dashboard.IsBusy, Is.False, "The simulated UI operation did not complete within ten seconds.");
			}
		try
			{
			Settle ();
			((TextBox)window.FindName ("Email")).Text = "fixture@example.invalid";
			((PasswordBox)window.FindName ("Password")).Password = "fixture-password";
			((Button)window.FindName ("SignIn")).RaiseEvent (new RoutedEventArgs (Button.ClickEvent));
			Settle ();
			Assert.That (dashboard.Homes, Has.Count.EqualTo (1));
			((ComboBox)window.FindName ("Home")).SelectedIndex = 0;
			Settle ();
			Assert.That (dashboard.Hubs, Has.Count.EqualTo (1));
			((ComboBox)window.FindName ("Hub")).SelectedIndex = 0;
			Settle ();
			((ComboBox)window.FindName ("Timer")).SelectedIndex = 0;
			Settle ();
			((Button)window.FindName ("RefreshStatus")).RaiseEvent (new RoutedEventArgs (Button.ClickEvent));
			Settle ();
			((ComboBox)window.FindName ("ControlZonePicker")).SelectedItem = zone;
			Settle ();
			Assert.That (dashboard.ControlZone, Is.EqualTo (zone));
			((ComboBox)window.FindName ("ManualModePicker")).SelectedIndex = mode;
			Settle ();
			Assert.That (dashboard.ManualMode, Is.EqualTo (mode));
			((CheckBox)window.FindName ("EnableControl")).IsChecked = true;
			Settle ();
			Assert.That (((Button)window.FindName ("StartSelectedZone")).IsEnabled, Is.True);
			((TextBox)window.FindName ("Duration")).Text = "0";
			Settle ();
			using (Assert.EnterMultipleScope ())
				{
				Assert.That (((Button)window.FindName ("StartSelectedZone")).IsEnabled, Is.False);
				Assert.That (((Button)window.FindName ("StopSelectedZone")).IsEnabled, Is.True);
				Assert.That (((PasswordBox)window.FindName ("Password")).Password, Is.Empty);
				Assert.That (dashboard.Zones, Has.Count.EqualTo (3));
				Assert.That (dashboard.Zones[0].State, Is.EqualTo ("Reported closed"));
				Assert.That (handler.RequestCount, Is.EqualTo (4));
				}
			((TextBox)window.FindName ("Duration")).Text = mode == 2 ? "5" : "1";
			Settle ();
			handler.Reply ("{\"code\":0}");
			((Button)window.FindName ("StartSelectedZone")).RaiseEvent (new RoutedEventArgs (Button.ClickEvent));
			Settle ();
			Assert.That (handler.LastBody, Does.Contain ("\"port\":" + zone).And.Contain ("\"duration\":" + (mode == 2 ? 5 : 60))
					 .And.Contain ("\"mode\":" + (mode + 1)));
			if (mode > 0)
				{
				Assert.That (handler.LastBody, Does.Contain (mode == 1 ? "0A001400" : "01000100"));
				((TextBox)window.FindName ("ManualBurst")).Text = "0";
				Settle ();
				Assert.That (((Button)window.FindName ("StartSelectedZone")).IsEnabled, Is.False);
				Assert.That (((Button)window.FindName ("StopSelectedZone")).IsEnabled, Is.True);
				}
			handler.Reply ("{\"code\":0,\"data\":{\"state\":\"11#" + (0x18 + zone).ToString ("X2") + "D801\"}}");
			((Button)window.FindName ("StopSelectedZone")).RaiseEvent (new RoutedEventArgs (Button.ClickEvent));
			Settle ();
			Assert.That (handler.LastBody, Does.Contain ("\"port\":" + zone).And.Contain ("\"mode\":0"));
			Assert.That (((TextBlock)window.FindName ("CommandFeedback")).Text,
				 Does.Contain ("Response only: Reported open").And.Contain ("server time unknown").And.Contain ("not confirmed"));
			Assert.That (dashboard.Zones[0].State, Is.EqualTo ("Reported closed"));
			((ComboBox)window.FindName ("ControlZonePicker")).SelectedItem = zone == 1 ? 2 : 1;
			Settle ();
			Assert.That (((CheckBox)window.FindName ("EnableControl")).IsChecked, Is.False);
			Assert.That (((Button)window.FindName ("StartSelectedZone")).IsEnabled, Is.False);

			FrameworkElement content = (FrameworkElement)window.Content;
			content.Measure (new Size (1100, 1450));
			content.Arrange (new Rect (0, 0, 1100, 1450));
			content.UpdateLayout ();
			RenderTargetBitmap bitmap = new (1100, 1450, 96, 96, PixelFormats.Pbgra32);
			bitmap.Render (content);
			PngBitmapEncoder encoder = new ();
			encoder.Frames.Add (BitmapFrame.Create (bitmap));
			string path = Path.Combine (TestContext.CurrentContext.WorkDirectory, "rainpoint-desktop-simulated-" + Guid.NewGuid ().ToString ("N") + ".png");
			using (FileStream stream = File.Create (path))
				encoder.Save (stream);
			TestContext.AddTestAttachment (path, "Offline simulated device rendering; not live hardware");
			}
		finally
			{
			window.Close ();
			Task closed = dashboard.CloseAsync ();
			Settle ();
			closed.GetAwaiter ().GetResult ();
			SynchronizationContext.SetSynchronizationContext (previousContext);
			}
		}

	private sealed class FixtureNetwork : HttpMessageHandler
		{
		private readonly Queue<string> _responses = new (new[]
		{
				"""{"code":0,"data":{"token":"fixture","tokenExpired":3600}}""",
				"""{"code":0,"data":[{"hid":42,"homeName":"Test garden"}]}""",
				"""{"code":0,"data":[{"mid":101,"name":"Garden hub","deviceName":"fixture","productKey":"fixture","model":"HWG023WBRF-V2","softVer":"1.1.1041","subDevices":[{"addr":1,"name":"Garden timer","model":"HTV345FRF","softVer":"130"}]}]}""",
				"""{"code":0,"data":[{"mid":101,"status":[{"id":"connected","value":"1"},{"id":"state","value":"0,-38"},{"id":"D01","value":"11#17E1BC0018DC0119D8001AD8001BD800299F0E0000002A9F000000002B9F00000000","time":1700000000000}]}]}"""
		  });
		public int RequestCount
			{
			get; private set;
			}
		internal void Reply (string body) => _responses.Enqueue (body);
		public string? LastBody
			{
			get; private set;
			}
		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			RequestCount++;
			LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync ();
			return new HttpResponseMessage (System.Net.HttpStatusCode.OK) { Content = new StringContent (_responses.Dequeue ()) };
			}
		}

	private sealed class RejectNetwork : HttpMessageHandler
		{
		public int RequestCount
			{
			get; private set;
			}
		protected override Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			RequestCount++;
			throw new InvalidOperationException ("The UI smoke test must not contact the cloud.");
			}
		}
	}
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
public sealed class CalendarWindowTests
	{
	private const string Port = "58020a001e0000800000000000d7,/,aux,646464646464646464646464,tail";
	private static readonly string Parameter = string.Join ("|", Enumerable.Repeat (Port, 3));

	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public void CalendarLoadsSelectedZoneAndDateNavigationNeverWrites (int zone)
		{
		using Host host = new ();
		Assert.That (host.Control<Button> ("LoadCalendar").IsEnabled, Is.False);
		host.SignInAndSelect ();
		host.Control<ComboBox> ("CalendarZonePicker").SelectedItem = zone;
		host.Settle ();
		host.Control<Calendar> ("PlanCalendar").SelectedDate = new DateTime (2026, 9, 24);
		host.Settle ();
		host.ReplyDiscovery (Parameter.Replace (",/,aux", ",80004a3c0000000000/,aux"));
		host.Click ("LoadCalendar");
		var grid = host.Control<DataGrid> ("CalendarRows");
		Assert.That (grid.Items.Count, Is.EqualTo (1));
		Assert.That (((CalendarRow)grid.Items[0]).Zone, Is.EqualTo (zone));
		Assert.That (((CalendarRow)grid.Items[0]).Start, Is.EqualTo ("08:00"));
		Assert.That (host.Control<TextBlock> ("CalendarNext").Text, Does.Contain ("2026-09-24 08:00"));
		host.Control<Calendar> ("PlanCalendar").SelectedDate = new DateTime (2026, 10, 1);
		host.Settle ();
		Assert.That (grid.Items.Count, Is.EqualTo (1));
		Assert.That (host.Control<TextBlock> ("CalendarNext").Text, Does.Contain ("2026-10-01 08:00"));
		Assert.That (host.Control<Calendar> ("PlanCalendar").DisplayDate.Month, Is.EqualTo (10));
		Assert.That (host.Network.PostPaths, Is.EqualTo (new[] { "/auth/basic/app/login" }));
		host.Render ($"rainpoint-calendar-zone{zone}.png");
		host.Control<ComboBox> ("CalendarZonePicker").SelectedItem = zone == 3 ? 1 : zone + 1;
		host.Settle ();
		Assert.That (grid.Items.Count, Is.Zero);
		Assert.That (host.Control<TextBlock> ("CalendarNext").Text, Does.Contain ("unknown"));
		}

	private sealed class Host : IDisposable
		{
		internal readonly FixtureNetwork Network = new ();
		private readonly HttpClient _http;
		private readonly Dashboard _dashboard;
		private readonly MainWindow _window;
		private readonly SynchronizationContext? _previous;
		private CalendarView View => (CalendarView)_window.FindName ("CalendarView");
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
			((TabControl)_window.FindName ("WorkbenchTabs")).SelectedIndex = 5;
			Settle ();
			}
		internal void ReplyDiscovery (string parameter) => Network.Reply (JsonSerializer.Serialize (new
			{
			code = 0,
			data = new[]{new{mid=101,param=parameter,name="Garden hub",deviceName="fixture",productKey="fixture",model="HWG023WBRF",subDevices=new[]
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
			TestContext.AddTestAttachment (path, "Offline calendar rendering with simulated cloud data");
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
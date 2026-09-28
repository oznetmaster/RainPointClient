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
public sealed class HistoryWindowTests
	{
	private const string Port = "58020a001e0000800000000000d7,/,aux,646464646464646464646464,tail";
	private static readonly string Parameter = string.Join ("|", Enumerable.Repeat (Port, 3));

	[TestCase ("Daily", "ymd", 20260923, "2026-09-23")]
	[TestCase ("Monthly", "ym", 202609, "2026-09")]
	public void UsageBindingsValidateDatesAndRenderReportedBuckets (string period, string key, int date, string displayDate)
		{
		using Host host = new ();
		Assert.That (host.Control<Button> ("LoadUsage").IsEnabled, Is.False);
		host.SignInAndSelect ();
		host.Control<ComboBox> ("UsagePeriodPicker").SelectedItem = period;
		host.Control<TextBox> ("UsageFrom").Text = "bad";
		host.Settle ();
		Assert.That (host.Control<Button> ("LoadUsage").IsEnabled, Is.False);
		host.Control<TextBox> ("UsageFrom").Text = "2026-09-01";
		host.Control<TextBox> ("UsageThrough").Text = "2026-09-24";
		host.Settle ();
		Assert.That (host.Control<Button> ("LoadUsage").IsEnabled, Is.True);
		host.Network.Reply ("{\"code\":0,\"data\":[{\"" + key + "\":" + date + ",\"val\":151}]}");
		host.Click ("LoadUsage");
		DataGrid rows = host.Control<DataGrid> ("UsageRows");
		Assert.That (rows.Items.Count, Is.EqualTo (1));
		Assert.That (((UsageHistoryRow)rows.Items[0]).Period, Is.EqualTo (displayDate));
		Assert.That (host.Control<TextBlock> ("UsageSummary").Text, Does.Contain (15.1m.ToString ("0.0")));
		Assert.That (host.Network.PostPaths, Has.Count.EqualTo (1));
		host.Render ("rainpoint-history-" + period + ".png");
		host.Control<ComboBox> ("HistoryZonePicker").SelectedItem = 2;
		host.Settle ();
		Assert.That (rows.Items.Count, Is.Zero);
		}

	[Test]
	public void EventControlsLoadOlderDeduplicateSelectDetailsAndResetFilters ()
		{
		using Host host = new ();
		host.SignInAndSelect ();
		host.Control<TextBox> ("EventsFrom").Text = "invalid";
		host.Settle ();
		Assert.That (host.Control<Button> ("LoadEvents").IsEnabled, Is.False);
		host.Control<TextBox> ("EventsFrom").Text = "2026-09-01 00:00:00";
		host.Control<TextBox> ("EventsBefore").Text = "2026-09-24 00:00:00";
		host.Settle ();
		host.Network.Reply (EventPage (50, 0));
		host.Click ("LoadEvents");
		Assert.That (host.Control<DataGrid> ("EventRows").Items.Count, Is.EqualTo (50));
		Assert.That (host.Control<Button> ("LoadOlderEvents").IsEnabled, Is.True);
		host.Control<DataGrid> ("EventRows").SelectedIndex = 10;
		host.Settle ();
		object selected = host.Control<DataGrid> ("EventRows").SelectedItem;
		host.Network.Reply (EventPage (2, 49));
		host.Click ("LoadOlderEvents");
		DataGrid rows = host.Control<DataGrid> ("EventRows");
		Assert.That (rows.Items.Count, Is.EqualTo (51));
		Assert.That (rows.SelectedItem, Is.SameAs (selected), "Loading older events should preserve the selected event.");
		Assert.That (host.Control<Button> ("LoadOlderEvents").IsEnabled, Is.False);
		rows.SelectedIndex = 50;
		host.Settle ();
		Assert.That (host.Control<TextBlock> ("EventDetails").Text, Does.Contain ("Unknown (999)").And.Contain ("61 seconds").And.Contain ("GMT+01:00"));
		Assert.That (host.Network.PostPaths, Has.Count.EqualTo (1));
		host.Render ("rainpoint-history-events.png");
		host.Control<ComboBox> ("EventFilterPicker").SelectedItem = "Watering";
		host.Settle ();
		Assert.That (rows.Items.Count, Is.Zero);
		Assert.That (host.Control<TextBlock> ("EventDetails").Text, Does.StartWith ("Select an event"));
		}

	private static string EventPage (int count, int start) => JsonSerializer.Serialize (new
		{
		code = 0,
		data = Enumerable.Range (start, count).Select (i => new
			{
			eid = "event-" + i,
			mid = 101,
			addr = 2,
			port = 1,
			code = i == 50 ? 999 : 1,
			timestamp = 1790180653212 - i * 1000L,
			time = "2026-09-23T17:23:34",
			timezone = "GMT+01:00",
			rule = new[] { new { type = "3", value = "14" }, new { type = "4", value = "61" } }
			}).ToArray ()
		});

	private sealed class Host : IDisposable
		{
		internal readonly FixtureNetwork Network = new ();
		private readonly HttpClient _http;
		private readonly Dashboard _dashboard;
		private readonly MainWindow _window;
		private readonly SynchronizationContext? _previous;
		private HistoryView View => (HistoryView)_window.FindName ("History");
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
			((TabControl)_window.FindName ("WorkbenchTabs")).SelectedIndex = 2;
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
			Settle ();
			content.UpdateLayout ();
			foreach (string gridName in new[] { "UsageRows", "EventRows" })
				foreach (DataGridColumn column in Control<DataGrid> (gridName).Columns)
					Assert.That (column.ActualWidth, Is.GreaterThanOrEqualTo (column.MinWidth), "History columns must remain readable.");
			Assert.That (View.ActualWidth, Is.GreaterThan (600));
			Assert.That (View.ActualHeight, Is.GreaterThan (400));
			RenderTargetBitmap bitmap = new ((int)Math.Ceiling (View.ActualWidth), (int)Math.Ceiling (View.ActualHeight), 96, 96, PixelFormats.Pbgra32);
			bitmap.Render (View);
			PngBitmapEncoder encoder = new ();
			encoder.Frames.Add (BitmapFrame.Create (bitmap));
			string path = Path.Combine (TestContext.CurrentContext.WorkDirectory, Path.GetFileNameWithoutExtension (filename) + "-" + Guid.NewGuid ().ToString ("N") + Path.GetExtension (filename));
			using (FileStream stream = File.Create (path))
				encoder.Save (stream);
			TestContext.AddTestAttachment (path, "Offline history rendering with simulated cloud data");
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
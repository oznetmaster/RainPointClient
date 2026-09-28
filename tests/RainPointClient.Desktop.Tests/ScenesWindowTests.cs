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
public sealed class ScenesWindowTests
	{
	private const string Port = "58020a001e0000800000000000d7,/,,646464646464646464646464,tail";
	private static readonly string Parameter = string.Join ("|", Enumerable.Repeat (Port, 3));


	[Test]
	public void ReadOnlySceneScreenRendersWithoutCommands ()
		{
		using Host host = new ();
		host.SignInAndSelect ();
		host.Network.Reply ("""{"code":0,"data":[{"id":12,"sceneName":"Rain forecast","open":0,"enable":1}]}""");
		host.ReplyDiscovery (Parameter);
		host.Network.Reply ("""{"code":0,"data":{"models":[]}}""");
		host.Click ("LoadScenes");
		Assert.That (host.Control<ComboBox> ("ScenePicker").Items.Count, Is.EqualTo (1));
		host.Control<ComboBox> ("ScenePicker").SelectedIndex = 0;
		host.Settle ();
		host.Network.Reply ("""{"code":0,"data":[{"id":12,"sceneName":"Rain forecast","open":0,"enable":1}]}""");
		host.Network.Reply ("""{"code":0,"data":{"id":12,"sceneName":"Rain forecast","open":0,"enable":1,"conditions":[{"type":1,"code":4,"enable":1,"contrast":2,"value1":"32000000"}],"actions":[{"type":1,"code":1,"enable":1,"param":"fixture@example.invalid","value":"Rain expected"}]}}""");
		host.Click ("LoadScene");
		Assert.That (host.Control<ListBox> ("Conditions").Items.Count, Is.EqualTo (1));
		Assert.That (host.Network.PostPaths, Has.Count.EqualTo (1));
		host.Render ("rainpoint-scenes.png");
		}

	[Test]
	public void WeekdayAndOnceEditorsUseHomeLocalTimeWithoutNetworkWrites ()
		{
		using Host host = new ();
		host.SignInAndSelect ();
		host.Click ("NewScene");
		host.Control<ComboBox> ("TimeRecurrence").SelectedItem = RainPointSceneRepeat.Weekdays;
		host.Control<ComboBox> ("TimeKind").SelectedItem = RainPointSceneTime.Sunset;
		host.Settle ();
		// Materialize the actual WPF template and operate its checkbox binding.
		var days = host.Control<ItemsControl> ("TimeWeekdays");
		days.Measure (new Size (1000, 100));
		days.Arrange (new Rect (0, 0, 1000, 100));
		days.UpdateLayout ();
		var friday = Descendants<CheckBox> (days).Single (c => c.DataContext is PlanWeekday d && d.Name == "Friday");
		friday.IsChecked = true;
		host.Settle ();
		host.Click ("AddTimeCondition");
		var time = (RainPointSceneCondition)host.Control<ListBox> ("Conditions").Items[0];
		Assert.That (time.Weekdays, Is.EqualTo (RainPointSceneWeekdays.Friday));
		Assert.That (time.Time, Is.EqualTo (RainPointSceneTime.Sunset));
		host.Control<TextBox> ("OnceTime").Text = "2026-10-04 09:35";
		host.Click ("AddOnceCondition");
		var once = (RainPointSceneCondition)host.Control<ListBox> ("Conditions").Items[1];
		Assert.That (once.AtLocal, Is.EqualTo (new DateTime (2026, 10, 4, 9, 35, 0)));
		Assert.That (once.AtLocal!.Value.Kind, Is.EqualTo (DateTimeKind.Unspecified));
		Assert.That (host.Network.PostPaths, Has.Count.EqualTo (1), "Draft editing must only have the original sign-in POST.");
		}
	private static IEnumerable<T> Descendants<T> (DependencyObject parent) where T : DependencyObject
		{
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount (parent); i++)
			{
			var child = VisualTreeHelper.GetChild (parent, i);
			if (child is T match)
				yield return match;
			foreach (var nested in Descendants<T> (child))
				yield return nested;
			}
		}

	[Test]
	public void HistorySelectionShowsTypedActionResultsWithoutWrites ()
		{
		using Host host = new ();
		host.SignInAndSelect ();
		host.Network.Reply ("""{"code":0,"data":{"total":1,"records":[{"id":"fixture","hid":5,"sceneMainId":12,"triggerTime":1700000000000,"result":"[{\"aid\":91,\"mid\":101,\"addr\":2,\"rt\":9876}]"}]}}""");
		host.Click ("LoadSceneHistory");
		var grid = host.Control<DataGrid> ("SceneHistoryGrid");
		Assert.That (grid.Items.Count, Is.EqualTo (1));
		grid.SelectedIndex = 0;
		host.Settle ();
		var actions = host.Control<DataGrid> ("SceneHistoryActions");
		Assert.That (((RainPointSceneActionResult)actions.Items[0]).ResultCode, Is.EqualTo (9876));
		Assert.That (((RainPointSceneActionResult)actions.Items[0]).Outcome, Is.EqualTo (RainPointSceneActionOutcome.VendorError));
		host.Render ("rainpoint-scene-history.png");
		Assert.That (actions.Columns.All (column => column.ActualWidth >= 90), Is.True, "History column labels and values must remain readable after layout.");
		Assert.That (host.Control<Button> ("NextSceneHistory").IsEnabled, Is.False);
		Assert.That (host.Network.PostPaths, Has.Count.EqualTo (1));
		}
	private sealed class Host : IDisposable
		{
		internal readonly FixtureNetwork Network = new ();
		private readonly HttpClient _http;
		private readonly Dashboard _dashboard;
		private readonly MainWindow _window;
		private readonly SynchronizationContext? _previous;
		private ScenesView View => (ScenesView)_window.FindName ("ScenesView");
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
			((TabControl)_window.FindName ("WorkbenchTabs")).SelectedIndex = 9;
			Settle ();
			}
		internal void ReplyDiscovery (string parameter) => Network.Reply (JsonSerializer.Serialize (new
			{
			code = 0,
			data = new[]{new{mid=101,name="Garden hub",deviceName="fixture",productKey="fixture",model="HWG023WBRF",subDevices=new object[]
	 {new{sid=42,addr=2,model="HTV345FRF",name="Garden timer",portNumber=3,softVer="130",param=parameter},new{sid=43,addr=3,model="HCS021FRF",name="Flowerbed sensor"}}}}
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
			content.Measure (new Size (1100, 2600));
			content.Arrange (new Rect (0, 0, 1100, 2600));
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
			TestContext.AddTestAttachment (path, "Offline Smart Scenes rendering with simulated cloud data");
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
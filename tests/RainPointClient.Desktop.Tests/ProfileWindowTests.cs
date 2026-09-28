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
public sealed class ProfileWindowTests
	{
	private const string Port = "58020a001e0000800000000000d7,/,aux,646464646464646464646464,tail";
	private static readonly string Parameter = string.Join ("|", Enumerable.Repeat (Port, 3));

	private const string Catalog = """{"code":0,"data":[{"id":1,"langField":"@plan_config_plant_type","subType":[{"id":30,"langField":"@plan_config_cool_turf","defaultFlag":1},{"id":31,"langField":"@plan_config_alternative","defaultFlag":0}]},{"id":2,"langField":"@plan_config_soil_type","subType":[{"id":60,"langField":"@plan_config_loam","defaultFlag":1},{"id":61,"langField":"@plan_config_alternative","defaultFlag":0}]},{"id":3,"langField":"@plan_config_nozzle_type","subType":[{"id":90,"langField":"@plan_config_fixed","defaultFlag":1},{"id":91,"langField":"@plan_config_alternative","defaultFlag":0}]},{"id":4,"langField":"@plan_config_light_duration","subType":[{"id":120,"langField":"@plan_config_six_to_eight_hours","defaultFlag":1},{"id":121,"langField":"@plan_config_alternative","defaultFlag":0}]},{"id":5,"langField":"@plan_config_ground_slope","subType":[{"id":150,"langField":"@plan_config_level","defaultFlag":1},{"id":151,"langField":"@plan_config_alternative","defaultFlag":0}]}]}""";
	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public void ProfileControlsLoadSaveAndReadRecommendation (int zone)
		{
		using Host host = new ();
		Assert.That (host.Control<Button> ("SaveProfile").IsEnabled, Is.False);
		host.SignInAndSelect ();
		host.Control<ComboBox> ("ProfileZone").SelectedItem = zone;
		host.Settle ();
		host.Network.Reply (Catalog);
		host.ReplyDiscovery (Parameter);
		host.Click ("LoadProfile");
		Assert.That (host.Control<ItemsControl> ("ProfileCategories").Items.Count, Is.EqualTo (5));
		host.Control<CheckBox> ("RecommendationsEnabled").IsChecked = true;
		host.Settle ();
		host.Network.Reply (Catalog);
		host.ReplyDiscovery (Parameter);
		host.Network.Reply ("{\"code\":0}");
		string[] rows = Enumerable.Repeat ("{\"open\":0,\"target\":[30]}", 3).ToArray ();
		rows[zone - 1] = "{\"open\":1,\"target\":[30,60,90,120,150]}";
		host.ReplyDiscovery (Parameter, "[" + string.Join (",", rows) + "]");
		host.Click ("SaveProfile");
		Assert.That (host.Control<TextBlock> ("ProfileMessage").Text, Does.Contain ("matches cloud read-back"));
		host.ReplyDiscovery (Parameter);
		host.Network.Reply ("{\"code\":0,\"data\":[{\"day\":2,\"second\":300}]}");
		host.Click ("LoadRecommendations");
		Assert.That (host.Control<TextBlock> ("RecommendationText").Text, Does.Contain ("2 days").And.Contain ("5 minutes"));
		Assert.That (host.Control<Button> ("PrepareRecommendedPlan").IsEnabled, Is.True);
		host.Render ("rainpoint-profile-zone" + zone + ".png");
		}

	private sealed class Host : IDisposable
		{
		internal readonly FixtureNetwork Network = new ();
		private readonly HttpClient _http;
		private readonly Dashboard _dashboard;
		private readonly MainWindow _window;
		private readonly SynchronizationContext? _previous;
		private ProfileView View => (ProfileView)_window.FindName ("ProfileView");
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
			((TabControl)_window.FindName ("WorkbenchTabs")).SelectedIndex = 6;
			Settle ();
			}
		internal void ReplyDiscovery (string parameter, string profile = "[{\"open\":0,\"target\":[30]},{\"open\":0,\"target\":[30]},{\"open\":0,\"target\":[30]}]") => Network.Reply (JsonSerializer.Serialize (new
			{
			code = 0,
			data = new[]{new{mid=101,param=parameter,name="Garden hub",deviceName="fixture",productKey="fixture",model="HWG023WBRF",subDevices=new[]
	 {new{sid=42,addr=2,model="HTV345FRF",name="Garden timer",portNumber=3,softVer="130",param=parameter,planJson=profile}}}}
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
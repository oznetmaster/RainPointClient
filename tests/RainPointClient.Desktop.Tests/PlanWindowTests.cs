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
public sealed class PlanWindowTests
	{
	private const string Port = "58020a001e0000800000000000d7,/,aux,646464646464646464646464,tail";
	private static readonly string Parameter = string.Join ("|", Enumerable.Repeat (Port, 3));

	private static string WithPlan (string record, int zone = 1)
		{
		string[] ports = Parameter.Split ('|');
		ports[zone - 1] = Port.Replace (",/,aux", "," + record + "/,aux");
		return string.Join ("|", ports);
		}

	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public void OnceRecordCannotBeEditedOrEnabledButCanBeDisabledAndDeleted (int zone)
		{
		using Host host = new ();
		host.SignInAndSelect ();
		host.Control<ComboBox> ("PlanZonePicker").SelectedItem = zone;
		host.Settle ();
		const string enabled = "8000403c000000390d", disabled = "0000403c000000390d";
		host.ReplyDiscovery (WithPlan (enabled, zone));
		host.Click ("LoadPlans");
		Assert.That (host.Control<TextBlock> ("PlanDetails").Text, Does.Contain ("Once"));
		Assert.That (host.Control<TextBlock> ("PlanValidation").Text, Does.Contain ("Once"));
		Assert.That (host.Control<Button> ("SavePlan").IsEnabled, Is.False);
		Assert.That (host.Control<ComboBox> ("PlanRepeat").Items.Cast<string> (), Does.Not.Contain ("Once"));
		Assert.That (host.Control<Button> ("TogglePlan").IsEnabled, Is.True);
		host.ReplyDiscovery (WithPlan (enabled, zone));
		host.Network.Reply ("{\"code\":0}");
		host.ReplyDiscovery (WithPlan (disabled, zone));
		host.Click ("TogglePlan");
		Assert.That (host.Control<Button> ("TogglePlan").IsEnabled, Is.False);
		int posts = host.Network.PostPaths.Count;
		host.Click ("TogglePlan");
		Assert.That (host.Network.PostPaths, Has.Count.EqualTo (posts));
		Assert.That (host.Control<Button> ("DeletePlan").IsEnabled, Is.True);
		host.ReplyDiscovery (WithPlan (disabled, zone));
		host.Network.Reply ("{\"code\":0}");
		host.ReplyDiscovery (Parameter);
		host.Click ("DeletePlan");
		Assert.That (host.Control<DataGrid> ("PlanRows").Items.Count, Is.Zero);
		}

	[TestCase (1, "Normal irrigation", "00004a3c0000000000", "120", "00004a780000000000")]
	[TestCase (2, "Normal irrigation", "00004a3c0000000000", "120", "00004a780000000000")]
	[TestCase (3, "Normal irrigation", "00004a3c0000000000", "120", "00004a780000000000")]
	[TestCase (1, "Cycle and soak", "0000ca0a000000000005001e00", "12", "0000ca0c000000000005001e00")]
	[TestCase (2, "Cycle and soak", "0000ca0a000000000005001e00", "12", "0000ca0c000000000005001e00")]
	[TestCase (3, "Cycle and soak", "0000ca0a000000000005001e00", "12", "0000ca0c000000000005001e00")]
	[TestCase (1, "Misting", "00008a5802000000000a001400", "12", "00008ad002000000000a001400")]
	[TestCase (2, "Misting", "00008a5802000000000a001400", "12", "00008ad002000000000a001400")]
	[TestCase (3, "Misting", "00008a5802000000000a001400", "12", "00008ad002000000000a001400")]
	[TestCase (1, "Normal irrigation", "00004a3c0000000000", "120", "00004a780000000000", true)]
	[TestCase (2, "Normal irrigation", "00004a3c0000000000", "120", "00004a780000000000", true)]
	[TestCase (3, "Normal irrigation", "00004a3c0000000000", "120", "00004a780000000000", true)]
	[TestCase (1, "Cycle and soak", "0000ca0a000000000005001e00", "12", "0000ca0c000000000005001e00", true)]
	[TestCase (2, "Cycle and soak", "0000ca0a000000000005001e00", "12", "0000ca0c000000000005001e00", true)]
	[TestCase (3, "Cycle and soak", "0000ca0a000000000005001e00", "12", "0000ca0c000000000005001e00", true)]
	[TestCase (1, "Misting", "00008a5802000000000a001400", "12", "00008ad002000000000a001400", true)]
	[TestCase (2, "Misting", "00008a5802000000000a001400", "12", "00008ad002000000000a001400", true)]
	[TestCase (3, "Misting", "00008a5802000000000a001400", "12", "00008ad002000000000a001400", true)]
	public void ActualControlsCreateReplaceEnableAndDeleteWithReadBack (int zone, string mode, string original, string duration, string replacement, bool volume = false)
		{
		if (volume)
			{
			original = original.Substring (0, 10) + "0e00" + original.Substring (14);
			replacement = replacement.Substring (0, 10) + "0e00" + replacement.Substring (14);
			}
		using Host host = new ();
		Assert.That (host.Control<Button> ("SavePlan").IsEnabled, Is.False);
		host.SignInAndSelect ();
		host.Control<ComboBox> ("PlanZonePicker").SelectedItem = zone;
		host.Settle ();
		host.ReplyDiscovery (Parameter);
		host.Click ("LoadPlans");
		Assert.That (host.Control<CheckBox> ("PlanEnabled").IsChecked, Is.False);
		host.Control<ComboBox> ("PlanMode").SelectedItem = mode;
		host.Settle ();
		Assert.That (((FrameworkElement)host.Control<TextBox> ("PlanVolume").Parent).Visibility, Is.EqualTo (Visibility.Visible));
		if (volume)
			{
			host.Control<TextBox> ("PlanVolume").Text = "0.2";
			host.Settle ();
			Assert.That (host.Control<Button> ("SavePlan").IsEnabled, Is.False);
			host.Control<TextBox> ("PlanVolume").Text = "1.4";
			host.Settle ();
			}
		string valid = host.Control<TextBox> ("PlanDuration").Text;
		host.Control<TextBox> ("PlanDuration").Text = "invalid";
		host.Settle ();
		Assert.That (host.Control<Button> ("SavePlan").IsEnabled, Is.False);
		host.Control<TextBox> ("PlanDuration").Text = valid;
		host.Settle ();
		Assert.That (host.Control<Button> ("SavePlan").IsEnabled, Is.True);
		Assert.That (host.Control<WrapPanel> ("PlanCycles").Visibility, Is.EqualTo (mode == "Normal irrigation" ? Visibility.Collapsed : Visibility.Visible));
		host.ReplyDiscovery (Parameter);
		host.Network.Reply ("{\"code\":0}");
		host.ReplyDiscovery (WithPlan (original, zone));
		host.Click ("SavePlan");
		Assert.That (host.Control<TextBlock> ("PlansMessage").Text, Does.Contain ("matches the cloud read-back"));
		Assert.That (host.Control<DataGrid> ("PlanRows").Items.Count, Is.EqualTo (1));
		Assert.That (((PlanRow)host.Control<DataGrid> ("PlanRows").Items[0]).State, Is.EqualTo ("Disabled"));
		Assert.That (host.Control<TextBox> ("PlanVolume").Text, Is.EqualTo (volume ? "1.4" : string.Empty));
		host.Render ("rainpoint-plans-" + (volume ? "volume-" : "") + mode.Replace (" ", "-") + "-zone" + zone + ".png");
		host.Control<TextBox> ("PlanDuration").Text = duration;
		host.Settle ();
		host.ReplyDiscovery (WithPlan (original, zone));
		host.Network.Reply ("{\"code\":0}");
		host.ReplyDiscovery (WithPlan (replacement, zone));
		host.Click ("SavePlan");
		Assert.That (host.Control<TextBlock> ("PlansMessage").Text, Does.Contain ("matches the cloud read-back"));
		string enabled = "80" + replacement.Substring (2);
		host.ReplyDiscovery (WithPlan (replacement, zone));
		host.Network.Reply ("{\"code\":0}");
		host.ReplyDiscovery (WithPlan (enabled, zone));
		host.Click ("TogglePlan");
		Assert.That (((PlanRow)host.Control<DataGrid> ("PlanRows").Items[0]).State, Is.EqualTo ("Enabled"));
		Assert.That (host.Control<Button> ("TogglePlan").Content, Is.EqualTo ("Disable selected plan"));
		host.Click ("NewPlan");
		Assert.That (host.Control<CheckBox> ("PlanEnabled").IsChecked, Is.False);
		host.Control<DataGrid> ("PlanRows").SelectedIndex = 0;
		host.Settle ();
		host.ReplyDiscovery (WithPlan (enabled, zone));
		host.Network.Reply ("{\"code\":0}");
		host.ReplyDiscovery (Parameter);
		host.Click ("DeletePlan");
		Assert.That (host.Control<DataGrid> ("PlanRows").Items.Count, Is.Zero);
		Assert.That (host.Control<Button> ("TogglePlan").IsEnabled, Is.False);
		Assert.That (host.Network.PostPaths.Count (path => path == "/app/device/sub/update"), Is.EqualTo (4));
		Assert.That (host.Network.PostPaths.Any (path => path.Contains ("controlWorkMode")), Is.False);
		}

	[TestCase (2)]
	[TestCase (3)]
	public void ZoneChangesClearDraftUntilReload (int zone)
		{
		using Host host = new ();
		host.SignInAndSelect ();
		host.ReplyDiscovery (WithPlan ("80004a3c0000000000"));
		host.Click ("LoadPlans");
		Assert.That (host.Control<CheckBox> ("PlanEnabled").IsChecked, Is.True);
		host.Control<ComboBox> ("PlanZonePicker").SelectedItem = zone;
		host.Settle ();
		Assert.That (host.Control<DataGrid> ("PlanRows").Items.Count, Is.Zero);
		Assert.That (host.Control<CheckBox> ("PlanEnabled").IsChecked, Is.False);
		host.ReplyDiscovery (Parameter);
		host.Click ("LoadPlans");
		Assert.That (host.Control<Button> ("NewPlan").IsEnabled, Is.True);
		Assert.That (host.Control<Button> ("SavePlan").IsEnabled, Is.True);
		Assert.That (host.Control<TextBox> ("PlanStart").IsEnabled, Is.True);
		Assert.That (host.Network.PostPaths, Has.Count.EqualTo (1));
		}

	[Test]
	public void UnsupportedHourlyTimingIsDisplayedWithoutAnEditableDailyReplacement ()
		{
		using Host host = new ();
		host.SignInAndSelect ();
		host.ReplyDiscovery (WithPlan ("8200703c00"));
		host.Click ("LoadPlans");
		Assert.That (host.Control<TextBlock> ("PlanDetails").Text, Does.Contain ("Every 2 hours"));
		Assert.That (host.Control<TextBlock> ("PlanValidation").Text, Does.Contain ("Hourly recurrence"));
		Assert.That (host.Control<Button> ("SavePlan").IsEnabled, Is.False);
		Assert.That (host.Control<Button> ("TogglePlan").IsEnabled, Is.True);
		}

	private sealed class Host : IDisposable
		{
		internal readonly FixtureNetwork Network = new ();
		private readonly HttpClient _http;
		private readonly Dashboard _dashboard;
		private readonly MainWindow _window;
		private readonly SynchronizationContext? _previous;
		private PlansView View => (PlansView)_window.FindName ("PlanView");
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
			((TabControl)_window.FindName ("WorkbenchTabs")).SelectedIndex = 3;
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
			foreach (DataGridColumn column in Control<DataGrid> ("PlanRows").Columns)
				Assert.That (column.ActualWidth, Is.GreaterThanOrEqualTo (column.MinWidth));
			Assert.That (View.ActualWidth, Is.GreaterThan (600));
			Assert.That (View.ActualHeight, Is.GreaterThan (400));
			RenderTargetBitmap bitmap = new ((int)Math.Ceiling (View.ActualWidth), (int)Math.Ceiling (View.ActualHeight), 96, 96, PixelFormats.Pbgra32);
			bitmap.Render (View);
			PngBitmapEncoder encoder = new ();
			encoder.Frames.Add (BitmapFrame.Create (bitmap));
			string path = Path.Combine (TestContext.CurrentContext.WorkDirectory, Path.GetFileNameWithoutExtension (filename) + "-" + Guid.NewGuid ().ToString ("N") + Path.GetExtension (filename));
			using (FileStream stream = File.Create (path))
				encoder.Save (stream);
			TestContext.AddTestAttachment (path, "Offline saved-plan rendering with simulated cloud data");
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
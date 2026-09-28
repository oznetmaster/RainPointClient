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
public sealed class PairingWindowTests
	{

	private const string Parameter = "";
	[Test]
	public void PairingAndRemovalRequireConfirmationAndDoNotRunOnLoad ()
		{
		using Host host = new ();
		host.SignInAndSelect ();
		host.Network.Reply ("""{"code":0,"data":{"hid":"5","homeName":"Test garden"}}""");
		host.ReplyDiscovery (Parameter);
		host.Network.Reply ("""{"code":0,"data":{"models":[{"model":"HTV345FRF","modelCode":271}]}}""");
		host.Click ("LoadPairing");
		host.Control<ComboBox> ("PairingModels").SelectedIndex = 0;
		host.Control<ComboBox> ("PairedDevices").SelectedIndex = 0;
		host.Settle ();
		Assert.That (host.Control<Button> ("StartPairing").IsEnabled, Is.True);
		Assert.That (host.Control<Button> ("RemoveChild").IsEnabled, Is.True);
		int prompts = 0;
		host.View.ConfirmForTest = _ => { prompts++; return false; };
		host.Click ("StartPairing");
		host.Click ("RemoveChild");
		host.Click ("RemoveHub");
		Assert.That (prompts, Is.EqualTo (3));
		host.Render ("rainpoint-pairing.png");
		Assert.That (host.Network.PostPaths, Has.Count.EqualTo (1));
		}
	private sealed class Host : IDisposable
		{
		internal readonly FixtureNetwork Network = new ();
		private readonly HttpClient _http;
		private readonly Dashboard _dashboard;
		private readonly MainWindow _window;
		private readonly SynchronizationContext? _previous;
		internal PairingView View => (PairingView)_window.FindName ("PairingView");
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
			((TabControl)_window.FindName ("WorkbenchTabs")).SelectedIndex = 11;
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
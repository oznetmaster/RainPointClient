// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using NUnit.Framework;

using RainPointClient.Desktop;
using RainPointClient.Desktop.Core;

namespace RainPointClient.Desktop.Tests;

[TestFixture, Apartment (ApartmentState.STA), NonParallelizable]
public sealed class CredentialTests
	{
	private string _directory = null!;
	private string _path = null!;
	private CredentialStore _store = null!;

	[SetUp]
	public void SetUp ()
		{
		_directory = Path.Combine (TestContext.CurrentContext.WorkDirectory, "credential-fixture-" + Guid.NewGuid ().ToString ("N"));
		_path = Path.Combine (_directory, "account.dat");
		_store = new CredentialStore (_path);
		}

	[TearDown]
	public void TearDown ()
		{
		if (!Directory.Exists (_directory))
			return;
		foreach (string file in Directory.GetFiles (_directory))
			File.Delete (file);
		Directory.Delete (_directory);
		}

	private static SavedAccount Account (bool automatic = true) => new ()
		{
		Email = "fixture@example.invalid",
		Password = "fixture-secret-password",
		CountryCode = "44",
		AutomaticSignIn = automatic
		};

	[Test]
	public void DpapiRoundTripEncryptsEntireAccountAndAtomicReplacementPreservesPreference ()
		{
		_store.Save (Account ());
		byte[] disk = File.ReadAllBytes (_path);
		SavedAccount restored = new CredentialStore (_path).Load ()!;
		using (Assert.EnterMultipleScope ())
			{
			Assert.That (restored.Email, Is.EqualTo (Account ().Email));
			Assert.That (restored.Password == Account ().Password, Is.True, "The test password must round trip.");
			Assert.That (restored.CountryCode, Is.EqualTo ("44"));
			Assert.That (restored.AutomaticSignIn, Is.True);
			Assert.That (Encoding.UTF8.GetString (disk).Contains (Account ().Password), Is.False);
			Assert.That (Encoding.UTF8.GetString (disk).Contains (Account ().Email), Is.False);
			}
		restored.AutomaticSignIn = false;
		_store.Save (restored);
		Assert.That (_store.Load ()!.AutomaticSignIn, Is.False);
		Assert.That (Directory.GetFiles (_directory), Has.Length.EqualTo (1));
		_store.Forget ();
		_store.Forget ();
		Assert.That (_store.Load (), Is.Null);
		}

	[Test]
	public void MissingAccountDoesNotCreateFiles ()
		{
		_store.Forget ();
		Assert.That (_store.Load (), Is.Null);
		Assert.That (Directory.Exists (_directory), Is.False);
		}

	[Test]
	public void InvalidAccountCannotReplacePreviouslySavedCredentials ()
		{
		_store.Save (Account ());
		SavedAccount invalid = Account ();
		invalid.Password = string.Empty;
		Assert.Throws<InvalidDataException> (() => _store.Save (invalid));
		Assert.That (_store.Load ()!.Password == Account ().Password, Is.True);
		}

	[Test]
	public void TamperedCiphertextCannotBeLoaded ()
		{
		_store.Save (Account ());
		byte[] bytes = File.ReadAllBytes (_path);
		bytes[bytes.Length - 1] ^= 0xFF;
		File.WriteAllBytes (_path, bytes);
		Assert.Throws<System.Security.Cryptography.CryptographicException> (() => _store.Load ());
		}

	[TestCase (true, 2)]
	[TestCase (false, 0)]
	public void StartupHonoursPreferenceAndRunsOnlyOnce (bool automatic, int requests)
		{
		_store.Save (Account (automatic));
		WithWindow ((window, dashboard, handler, settle) =>
		{
			Task startup = window.InitializeAccountAsync ();
			settle (startup);
			settle (window.InitializeAccountAsync ());
			using (Assert.EnterMultipleScope ())
				{
				Assert.That (handler.RequestCount, Is.EqualTo (requests));
				Assert.That (((TextBox)window.FindName ("Email")).Text, Is.EqualTo (Account ().Email));
				Assert.That (dashboard.ControlsArmed, Is.False);
				Assert.That (dashboard.SelectedTimer, Is.Null);
				Assert.That (((PasswordBox)window.FindName ("Password")).Password.Length == 0, Is.EqualTo (automatic));
				}
		});
		}

	[Test]
	public void RejectedAutomaticSignInDoesNotRetryOrOverwriteSavedAccount ()
		{
		_store.Save (Account ());
		byte[] before = File.ReadAllBytes (_path);
		WithWindow ((window, dashboard, handler, settle) =>
		{
			handler.RejectLogin = true;
			settle (window.InitializeAccountAsync ());
			settle (window.InitializeAccountAsync ());
			Assert.That (handler.RequestCount, Is.EqualTo (1));
			Assert.That (dashboard.Homes, Is.Empty);
			Assert.That (File.ReadAllBytes (_path), Is.EqualTo (before));
		});
		}

	[Test]
	public void CorruptStoreLeavesManualSignInAvailableWithoutNetwork ()
		{
		Directory.CreateDirectory (_directory);
		File.WriteAllBytes (_path, new byte[] { 1, 2, 3 });
		WithWindow ((window, dashboard, handler, settle) =>
		{
			settle (window.InitializeAccountAsync ());
			Assert.That (handler.RequestCount, Is.Zero);
			Assert.That (dashboard.CanConnect, Is.True);
			Assert.That (((TextBlock)window.FindName ("SavedAccountMessage")).Text, Does.Contain ("could not be read"));
			((Button)window.FindName ("ForgetAccount")).RaiseEvent (new RoutedEventArgs (Button.ClickEvent));
			Assert.That (File.Exists (_path), Is.False);
		});
		}

	[Test]
	public void ManualSignInSavesAndSignOutDisablesStartupWithoutForgetting ()
		{
		WithWindow ((window, dashboard, handler, settle) =>
		{
			((TextBox)window.FindName ("Email")).Text = Account ().Email;
			((PasswordBox)window.FindName ("Password")).Password = Account ().Password;
			((Button)window.FindName ("SignIn")).RaiseEvent (new RoutedEventArgs (Button.ClickEvent));
			settle (null);
			Assert.That (_store.Load ()!.AutomaticSignIn, Is.True);
			((Button)window.FindName ("SignOut")).RaiseEvent (new RoutedEventArgs (Button.ClickEvent));
			settle (null);
			Assert.That (_store.Load ()!.AutomaticSignIn, Is.False);
			Assert.That (dashboard.Homes, Is.Empty);
			// Manual sign-in reuses the saved password only for the same email and country.
			((Button)window.FindName ("SignIn")).RaiseEvent (new RoutedEventArgs (Button.ClickEvent));
			settle (null);
			Assert.That (dashboard.Homes, Has.Count.EqualTo (1));
			Assert.That (handler.RequestCount, Is.EqualTo (5));
		});
		}

	[Test]
	public void DisablingRememberDeletesStoreAndDisablesAutomaticSignIn ()
		{
		_store.Save (Account (false));
		WithWindow ((window, _, handler, settle) =>
		{
			settle (window.InitializeAccountAsync ());
			((CheckBox)window.FindName ("RememberAccount")).IsChecked = false;
			Assert.That (File.Exists (_path), Is.False);
			Assert.That (((CheckBox)window.FindName ("AutomaticSignIn")).IsChecked, Is.False);
			Assert.That (handler.RequestCount, Is.Zero);
		});
		}

	private void WithWindow (Action<MainWindow, Dashboard, AccountHandler, Action<Task?>> test)
		{
		using AccountHandler handler = new ();
		using HttpClient http = new (handler);
		Dashboard dashboard = new (new RainPointCloudClient (http));
		MainWindow window = new (dashboard, _store);
		SynchronizationContext? previous = SynchronizationContext.Current;
		SynchronizationContext.SetSynchronizationContext (new DispatcherSynchronizationContext (window.Dispatcher));
		void Settle (Task? task)
			{
			DispatcherFrame frame = new ();
			DateTime deadline = DateTime.UtcNow.AddSeconds (10);
			DispatcherTimer pump = new (DispatcherPriority.ApplicationIdle)
				{
				Interval = TimeSpan.FromMilliseconds (10)
				};
			pump.Tick += (_, _) =>
			{
				if ((!dashboard.IsBusy && (task is null || task.IsCompleted)) || DateTime.UtcNow >= deadline)
					frame.Continue = false;
			};
			pump.Start ();
			try
				{
				Dispatcher.PushFrame (frame);
				}
			finally { pump.Stop (); }
			Assert.That (dashboard.IsBusy, Is.False);
			if (task is not null)
				{
				Assert.That (task.IsCompleted, Is.True);
				task.GetAwaiter ().GetResult ();
				}
			}
		try
			{
			Settle (null);
			test (window, dashboard, handler, Settle);
			}
		finally
			{
			Task closing = dashboard.CloseAsync ();
			Settle (closing);
			window.Close ();
			Settle (null);
			SynchronizationContext.SetSynchronizationContext (previous);
			}
		}

	private sealed class AccountHandler : HttpMessageHandler
		{
		internal int RequestCount
			{
			get; private set;
			}
		internal bool RejectLogin
			{
			get; set;
			}
		protected override Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			RequestCount++;
			string response = request.RequestUri!.AbsolutePath switch
				{
					"/auth/basic/app/login" => RejectLogin ? "{\"code\":1001}" : """{"code":0,"data":{"token":"fixture","tokenExpired":3600}}""",
					"/app/member/appHome/list" => """{"code":0,"data":[{"hid":42,"homeName":"Fixture home"}]}""",
					"/auth/basic/app/logOut" => "{\"code\":0}",
					_ => throw new InvalidOperationException ("Unexpected request in offline account test.")
					};
			return Task.FromResult (new HttpResponseMessage (HttpStatusCode.OK) { Content = new StringContent (response) });
			}
		}
	}
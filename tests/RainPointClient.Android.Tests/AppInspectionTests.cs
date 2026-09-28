using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using CrestronHomeNUnit.Android;

using NUnit.Framework;

namespace RainPointClient.Android.Tests;

// Standalone vendor-app inspection, not a processor/installed-driver workflow or certification gate.
[TestFixture, NonParallelizable, Category ("Android"), Category ("Live")]
public sealed class AppInspectionTests
	{
	[Test, Explicit ("Uses an existing emulator. Supply RAINPOINT_ANDROID_PLAN with reviewed navigation steps and private evidence paths.")]
	public async Task InspectReviewedPage ()
		{
		string? path = Environment.GetEnvironmentVariable ("RAINPOINT_ANDROID_PLAN");
		if (string.IsNullOrWhiteSpace (path))
			Assert.Ignore ("A private, explicit Android inspection plan is required.");
		Plan plan = JsonSerializer.Deserialize<Plan> (File.ReadAllText (path!))!;
		Assert.That (plan.Package, Is.AnyOf ("com.baldr.rainpointSmartPlus", "com.android.vending"));
		Assert.That (Path.IsPathFullyQualified (plan.EvidenceDirectory), Is.True);
		Assert.That (Directory.Exists (plan.EvidenceDirectory), Is.True);
		string run = Guid.NewGuid ().ToString ("N");
		using AndroidSessionLease lease = AndroidSessionLease.Acquire (plan.LockPath, run);
		using CancellationTokenSource timeout = new (TimeSpan.FromMinutes (2));
		AdbCommandTransport transport = new (plan.AdbExecutable, plan.Serial, TimeSpan.FromSeconds (25));
		AndroidDevice device = new (transport, plan.Package);
		bool completed = false;
		async Task Capture (string name)
			{
			AndroidHierarchy page = await device.CaptureAsync (timeout.Token);
			await File.WriteAllTextAsync (Path.Combine (plan.EvidenceDirectory, name + ".xml"), page.MaskedXml, timeout.Token);
			await File.WriteAllBytesAsync (Path.Combine (plan.EvidenceDirectory, name + ".png"), await device.CaptureScreenshotAsync (timeout.Token), timeout.Token);
			}
		try
			{
			await Capture ("before");
			foreach (Step step in plan.Steps)
				{
				void Validate (AndroidHierarchy page)
					{
					foreach (string label in step.RequireText)
						page.RequireUnique (new AndroidSelector (AndroidSelectorKind.Text, label));
					}
				switch (step.Action)
					{
					case "open-store":
						Assert.That (plan.Package, Is.EqualTo ("com.android.vending"));
						await transport.ExecuteAsync (["shell", "am", "start", "-a", "android.intent.action.VIEW", "-d", "https://play.google.com/store/apps/details?id=com.baldr.rainpointSmartPlus", "-p", plan.Package], timeout.Token);
						break;
					case "tap":
						Assert.That (step.RequireText, Is.Not.Empty, "A reviewed visible page guard is required before input.");
						await device.TapAsync (new AndroidSelector (step.SelectorKind, step.Selector), Validate, timeout.Token);
						break;
					case "back":
						Assert.That (step.RequireText, Is.Not.Empty);
						await device.BackAsync (Validate, timeout.Token);
						break;
					case "install-reviewed-1065":
						Assert.That (plan.Package, Is.EqualTo ("com.baldr.rainpointSmartPlus"));
						string directory = Environment.GetEnvironmentVariable ("RAINPOINT_ANDROID_APK_DIRECTORY") ?? string.Empty;
						Assert.That (Path.IsPathFullyQualified (directory) && Directory.Exists (directory), Is.True);
						(string Name, string Hash)[] splits =
							[
							("base.apk", "A0A97ED04FE2950408E88A773F345F38C32FDF1E536794579200E8EFDC411FEF"),
							("split_config.arm64_v8a.apk", "D746A7EB66C4FF499F25576671A8D6A0473DAB69A050F1D269E255F797A4660F"),
							("split_config.en.apk", "F3B426B5306FDFFD02D592CAA7379C2E69A10C9F48D03B402ECD465CB0D1A357"),
							("split_config.xxhdpi.apk", "C44D192089698584C3B2AB1D15B944EC513604A2C1A2D1CD17AFD824691CB145")
							];
						List<string> arguments = ["install-multiple", "-r"];
						foreach ((string name, string hash) in splits)
							{
							string split = Path.Combine (directory, name);
							using FileStream file = File.OpenRead (split);
							Assert.That (Convert.ToHexString (await SHA256.HashDataAsync (file, timeout.Token)), Is.EqualTo (hash));
							arguments.Add (split);
							}
						AdbCommandTransport splitInstaller = new (plan.AdbExecutable, plan.Serial, TimeSpan.FromSeconds (90));
						string splitResult = Encoding.UTF8.GetString (await splitInstaller.ExecuteAsync (arguments, timeout.Token));
						Assert.That (splitResult.TrimEnd ().EndsWith ("Success", StringComparison.Ordinal), Is.True, "Reviewed split APK installation failed.");
						break;
					case "install-reviewed-1047":
						Assert.That (plan.Package, Is.EqualTo ("com.baldr.rainpointSmartPlus"));
						Assert.That (Environment.GetEnvironmentVariable ("RAINPOINT_ANDROID_REPLACE_SIGNED_OUT"), Is.EqualTo ("1"),
							"Replacing the test installation clears only RainPoint app data. Confirm it has never been signed in.");
						string apk = Environment.GetEnvironmentVariable ("RAINPOINT_ANDROID_APK") ?? string.Empty;
						Assert.That (Path.IsPathFullyQualified (apk) && File.Exists (apk), Is.True);
						using (FileStream file = File.OpenRead (apk))
							Assert.That (Convert.ToHexString (await SHA256.HashDataAsync (file, timeout.Token)),
								Is.EqualTo ("E023DCC17BEF1CDB81058DDF1A5B3728C28FAA4B59AC442414FE3076DA5CBBBF"),
								"Only the reviewed 1.14.1047 APK with the verified publisher certificate is allowed.");
						string removed = Encoding.UTF8.GetString (await transport.ExecuteAsync (["uninstall", plan.Package], timeout.Token));
						Assert.That (removed.Trim (), Is.EqualTo ("Success"));
						AdbCommandTransport installer = new (plan.AdbExecutable, plan.Serial, TimeSpan.FromSeconds (90));
						string installed = Encoding.UTF8.GetString (await installer.ExecuteAsync (["install", apk], timeout.Token));
						Assert.That (installed.TrimEnd ().EndsWith ("Success", StringComparison.Ordinal), Is.True, "Reviewed APK installation failed.");
						break;
					case "open-app":
						Assert.That (plan.Package, Is.EqualTo ("com.baldr.rainpointSmartPlus"));
						await transport.ExecuteAsync (["shell", "am", "start", "-W", "-n", plan.Package + "/com.baldr.homgar.ui.activity.SplashActivity"], timeout.Token);
						break;
					case "package-diagnostics":
						Assert.That (plan.Package, Is.EqualTo ("com.baldr.rainpointSmartPlus"));
						await File.WriteAllBytesAsync (Path.Combine (plan.EvidenceDirectory, "package.txt"),
							await transport.ExecuteAsync (["shell", "dumpsys", "package", plan.Package], timeout.Token), timeout.Token);
						await File.WriteAllBytesAsync (Path.Combine (plan.EvidenceDirectory, "crash.txt"),
							await transport.ExecuteAsync (["logcat", "-b", "crash", "-d", "-t", "200"], timeout.Token), timeout.Token);
						string paths = Encoding.UTF8.GetString (await transport.ExecuteAsync (["shell", "pm", "path", plan.Package], timeout.Token));
						foreach (string line in paths.Split ('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
							{
							Assert.That (line.StartsWith ("package:/data/app/", StringComparison.Ordinal), Is.True);
							string remote = line["package:".Length..];
							string name = Path.GetFileName (remote);
							Assert.That (Regex.IsMatch (name, @"^[A-Za-z0-9_.-]+\.apk$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds (1)), Is.True);
							string destination = Path.Combine (plan.EvidenceDirectory, name);
							Assert.That (File.Exists (destination), Is.False);
							await transport.ExecuteAsync (["pull", remote, destination], timeout.Token);
							}
						break;
					case "capture":
						break;
					default:
						Assert.Fail ("Unknown inspection operation; no input sent.");
						break;
					}
				if (step.WaitSeconds > 0)
					await Task.Delay (TimeSpan.FromSeconds (Math.Min (step.WaitSeconds, 10)), timeout.Token);
				}
			await Capture ("after");
			completed = true;
			TestContext.Progress.WriteLine ("Inspection complete. Private evidence directory: " + plan.EvidenceDirectory);
			}
		finally
			{
			// Navigation is deliberately retained for the next reviewed inspection step. No device-state restoration is claimed.
			// An uncertain input/capture retains the shared reservation for reconciliation rather than replaying input.
			if (completed)
				lease.Release ();
			}
		}

	private sealed class Plan
		{
		[JsonPropertyName ("adbExecutable")] public string AdbExecutable { get; set; } = string.Empty;
		[JsonPropertyName ("serial")] public string Serial { get; set; } = string.Empty;
		[JsonPropertyName ("package")] public string Package { get; set; } = string.Empty;
		[JsonPropertyName ("lockPath")] public string LockPath { get; set; } = string.Empty;
		[JsonPropertyName ("evidenceDirectory")] public string EvidenceDirectory { get; set; } = string.Empty;
		[JsonPropertyName ("steps")] public Step[] Steps { get; set; } = [];
		}
	private sealed class Step
		{
		[JsonPropertyName ("action")] public string Action { get; set; } = "capture";
		[JsonPropertyName ("selectorKind"), JsonConverter (typeof (JsonStringEnumConverter<AndroidSelectorKind>))]
		public AndroidSelectorKind SelectorKind
			{
			get; set;
			}
		[JsonPropertyName ("selector")] public string Selector { get; set; } = string.Empty;
		[JsonPropertyName ("requireText")] public string[] RequireText { get; set; } = [];
		[JsonPropertyName ("waitSeconds")]
		public int WaitSeconds
			{
			get; set;
			}
		}
	}
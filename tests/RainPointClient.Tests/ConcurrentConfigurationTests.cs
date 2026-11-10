// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class ConcurrentConfigurationTests
	{
	[Test]
	public async Task SeparateZoneSnapshotsCannotOverwriteAnEarlierFullRecordWrite ()
		{
		using var handler = new TimerService ();
		using var http = new HttpClient (handler);
		using var client = new RainPointCloudClient (http);
		await client.LoginAsync ("test@example.invalid", "fake", "44");
		RainPointHub hub = (await client.GetHubsAsync (5)).Single ();
		RainPointScheduleSnapshot first = await client.GetTimerSchedulesAsync (hub, 2, 1);
		RainPointScheduleSnapshot second = await client.GetTimerSchedulesAsync (hub, 2, 2);
		Task write = client.SetTimerDefaultWateringDurationAsync (hub, first, TimeSpan.FromMinutes (5));
		try
			{
			await handler.Started.Task.WaitAsync (TimeSpan.FromSeconds (10));
			int before = handler.Reads;
			Task competing = client.SetTimerDefaultWateringDurationAsync (hub, second, TimeSpan.FromMinutes (7));
			Assert.That (handler.Reads, Is.EqualTo (before), "Validation must wait for the earlier shared-record write.");
			Assert.That (competing.IsCompleted, Is.False);
			_ = handler.Release.TrySetResult (true);
			await write.WaitAsync (TimeSpan.FromSeconds (10));
			await Assert.ThrowsAsync<RainPointException> (async () => await competing);
			Assert.That (handler.Writes, Is.EqualTo (1));
			RainPointScheduleSnapshot current = await client.GetTimerSchedulesAsync (hub, 2, 1);
			Assert.That (current.ZoneDefaults!.WateringDuration, Is.EqualTo (TimeSpan.FromMinutes (5)));
			}
		finally { _ = handler.Release.TrySetResult (true); }
		}

	private sealed class TimerService : HttpMessageHandler
		{
		private string _parameter = string.Join ("|", Enumerable.Repeat ("58020a001e00008000004200fed7aabb,/,aux,646464646464646464646464,tail", 3));
		internal int Reads, Writes;
		internal TaskCompletionSource<bool> Started { get; } = new (TaskCreationOptions.RunContinuationsAsynchronously);
		internal TaskCompletionSource<bool> Release { get; } = new (TaskCreationOptions.RunContinuationsAsynchronously);
		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			string path = request.RequestUri!.AbsolutePath;
			if (path.Contains ("login"))
				return Reply ("{\"code\":0,\"data\":{\"token\":\"fake-access\",\"tokenExpired\":3600}}");
			if (path.Contains ("sub/update"))
				{
				using JsonDocument body = JsonDocument.Parse (await request.Content!.ReadAsStringAsync ());
				_ = Interlocked.Increment (ref Writes);
				_ = Started.TrySetResult (true);
				await Release.Task;
				_parameter = body.RootElement.GetProperty ("param").GetString ()!;
				return Reply ("{\"code\":0}");
				}
			_ = Interlocked.Increment (ref Reads);
			return Reply (JsonSerializer.Serialize (new
				{
				code = 0,
				data = new[] { new { mid = 101, model = "HWG023WBRF", deviceName = "hub", productKey = "product", subDevices = new[] { new { sid = 42, addr = 2, model = "HTV345FRF", portNumber = 3, softVer = "130", param = _parameter } } } }
				}));
			}
		private static HttpResponseMessage Reply (string json) => new (HttpStatusCode.OK) { Content = new StringContent (json) };
		}
	}
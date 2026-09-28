// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using NUnit.Framework;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class ScriptedHandlerTests
	{
	[Test]
	public async Task DelayedBodyCaptureCannotSwapConcurrentResponses ()
		{
		using ScriptedHandler handler = new ();
		using HttpClient http = new (handler, false);
		handler.Reply ("first");
		handler.Reply ("second");
		using BlockingContent body = new ();
		Task<HttpResponseMessage> first = http.PostAsync ("https://fixture.invalid/first", body);
		try
			{
			Assert.That (await Task.WhenAny (body.Entered.Task, Task.Delay (5000)), Is.SameAs (body.Entered.Task));
			using HttpResponseMessage second = await http.PostAsync ("https://fixture.invalid/second", new StringContent ("ready"));
			Assert.That (await second.Content.ReadAsStringAsync (), Is.EqualTo ("second"));
			}
		finally { body.Release.TrySetResult (true); }
		using HttpResponseMessage response = await first;
		Assert.That (await response.Content.ReadAsStringAsync (), Is.EqualTo ("first"));
		Assert.That (handler.Requests[0].Path, Is.EqualTo ("/first"));
		}
	private sealed class BlockingContent : HttpContent
		{
		internal readonly TaskCompletionSource<bool> Entered = new (TaskCreationOptions.RunContinuationsAsynchronously);
		internal readonly TaskCompletionSource<bool> Release = new (TaskCreationOptions.RunContinuationsAsynchronously);
		protected override async Task SerializeToStreamAsync (Stream stream, TransportContext? context)
			{
			Entered.TrySetResult (true);
			await Release.Task;
			await stream.WriteAsync (new byte[] { 65 }, 0, 1);
			}
		protected override bool TryComputeLength (out long length)
			{
			length = 0;
			return false;
			}
		}
	}
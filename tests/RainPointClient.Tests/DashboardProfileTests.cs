// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using NUnit.Framework;

using RainPointClient.Desktop.Core;

namespace RainPointClient.Tests;

[TestFixture]
public sealed class DashboardProfileTests
	{
	private ScriptedHandler _handler = null!; private HttpClient _http = null!; private Dashboard _dashboard = null!;
	[SetUp]
	public void SetUp ()
		{
		_handler = new ();
		_http = new (_handler, false);
		_dashboard = new (new RainPointCloudClient (_http));
		}
	[TearDown]
	public async Task TearDown ()
		{
		await _dashboard.CloseAsync ();
		_http.Dispose ();
		_handler.Dispose ();
		}
	private void Discovery (string profile = ZoneProfileTests.Profile) => _handler.Reply (ZoneProfileTests.Discovery (profile));
	private async Task Select ()
		{
		_handler.Reply ("""{"code":0,"data":{"token":"fixture","tokenExpired":3600}}""");
		_handler.Reply ("""{"code":0,"data":[{"hid":5,"homeName":"Garden"}]}""");
		await _dashboard.ConnectAsync ("fixture@example.invalid", "fixture", "44");
		Discovery ();
		await _dashboard.SelectHomeAsync (_dashboard.Homes.Single ());
		_dashboard.SelectHub (_dashboard.Hubs.Single ());
		_dashboard.SelectTimer (_dashboard.Timers.Single ());
		}
	private async Task Load (string profile = ZoneProfileTests.Profile)
		{
		_handler.Reply (ZoneProfileTests.Catalog);
		Discovery (profile);
		await _dashboard.LoadProfileAsync ();
		}
	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public async Task ProfileEditorLoadsDefaultsSavesAndVerifiesAllZones (int zone)
		{
		await Select ();
		_dashboard.ProfileZone = zone;
		await Load ();
		Assert.That (_dashboard.ProfileCategories[0].Name, Is.EqualTo ("Plant Type"));
		Assert.That (_dashboard.ProfileCategories.All (c => c.Selected is not null), Is.True);
		_dashboard.RecommendationsEnabled = false;
		foreach (var category in _dashboard.ProfileCategories)
			category.Selected = category.Options.Last ();
		_handler.Reply (ZoneProfileTests.Catalog);
		Discovery ();
		_handler.Reply ("{\"code\":0}");
		string[] rows = { "{\"open\":1,\"target\":[30]}", "{\"open\":0,\"target\":[60]}", "{\"open\":1,\"target\":[]}" };
		rows[zone - 1] = "{\"open\":0,\"target\":[31,61]}";
		Discovery ("[" + string.Join (",", rows) + "]");
		await _dashboard.SaveProfileAsync ();
		Assert.That (_dashboard.ProfileMessage, Does.Contain ("matches cloud read-back"));
		Assert.That (_handler.Requests.Count (r => r.Path == "/app/device/sub/update"), Is.EqualTo (1));
		Assert.That (_handler.Requests.Any (r => r.Path.Contains ("controlWorkMode")), Is.False);
		_dashboard.ProfileZone = zone == 3 ? 1 : zone + 1;
		Assert.That (_dashboard.ProfileCategories, Is.Empty);
		Assert.That (_dashboard.CanSaveProfile, Is.False);
		}
	[TestCase ("[{},{}]")]
	[TestCase ("[{\"open\":1,\"target\":[999]},{},{}]")]
	public async Task UnreadableOrUnknownProfileCannotBeOverwritten (string profile)
		{
		await Select ();
		await Load (profile);
		await _dashboard.SaveProfileAsync ();
		Assert.That (_dashboard.CanSaveProfile, Is.False);
		Assert.That (_handler.Requests.Count (r => r.Path == "/app/device/sub/update"), Is.Zero);
		}
	[TestCase (false)]
	[TestCase (true)]
	public async Task UncertainOrMismatchedProfileCannotReplay (bool mismatch)
		{
		await Select ();
		await Load ();
		_dashboard.RecommendationsEnabled = false;
		_handler.Reply (ZoneProfileTests.Catalog);
		Discovery ();
		if (mismatch)
			{
			_handler.Reply ("{\"code\":0}");
			Discovery ();
			}
		else
			_handler.Steps.Enqueue ((_, _) => throw new HttpRequestException ("private"));
		await _dashboard.SaveProfileAsync ();
		await _dashboard.SaveProfileAsync ();
		Assert.That (_dashboard.CanSaveProfile, Is.False);
		Assert.That (_dashboard.ProfileMessage, Does.Contain ("no write was retried").And.Not.Contain ("private"));
		Assert.That (_handler.Requests.Count (r => r.Path == "/app/device/sub/update"), Is.EqualTo (1));
		}
	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public async Task RecommendationCreatesOnlyALocalDisabledDraft (int zone)
		{
		await Select ();
		_dashboard.ProfileZone = zone;
		Discovery ();
		_handler.Reply ("""{"code":0,"data":[{"day":2.4,"second":301}]}""");
		await _dashboard.LoadRecommendationsAsync ();
		Assert.That (_dashboard.CanPrepareRecommendedPlan, Is.True);
		string ports = string.Join ("|", Enumerable.Repeat ("58020a001e0000800000000000d7,/,aux,646464646464646464646464,tail", 3));
		_handler.Reply (ZoneProfileTests.Discovery ().Replace ("untouched", ports));
		await _dashboard.PrepareRecommendedPlanAsync ();
		Assert.That (_dashboard.PlanZone, Is.EqualTo (zone));
		Assert.That (_dashboard.PlanDraft.Enabled, Is.False);
		Assert.That (_dashboard.PlanDraft.Duration, Is.EqualTo ("360"));
		Assert.That (_dashboard.PlanDraft.Repeat, Is.EqualTo ("Every N days"));
		Assert.That (_dashboard.PlanDraft.Interval, Is.EqualTo ("2"));
		Assert.That (_dashboard.PlanDraft.Date, Is.Empty);
		Assert.That (_handler.Requests.Count (r => r.Path == "/app/device/sub/update"), Is.Zero);
		}
	[TestCase ("[]")]
	[TestCase ("[{}]")]
	[TestCase ("[{\"day\":0,\"second\":60}]")]
	[TestCase ("[{\"day\":128,\"second\":60}]")]
	[TestCase ("[{\"day\":1,\"second\":43260}]")]
	public async Task UnusableSuggestionCannotCreateAPlan (string data)
		{
		await Select ();
		Discovery ();
		_handler.Reply ("{\"code\":0,\"data\":" + data + "}");
		await _dashboard.LoadRecommendationsAsync ();
		int count = _handler.Requests.Count;
		await _dashboard.PrepareRecommendedPlanAsync ();
		Assert.That (_dashboard.CanPrepareRecommendedPlan, Is.False);
		Assert.That (_handler.Requests, Has.Count.EqualTo (count));
		}
	[Test]
	public async Task SignedOutActionsDoNothing ()
		{
		await _dashboard.LoadProfileAsync ();
		await _dashboard.SaveProfileAsync ();
		await _dashboard.LoadRecommendationsAsync ();
		await _dashboard.PrepareRecommendedPlanAsync ();
		Assert.That (_handler.Requests, Is.Empty);
		}
	}
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
public sealed class DashboardSceneWeatherTests
	{
	private ScriptedHandler _handler = null!; private HttpClient _http = null!; private Dashboard _dashboard = null!;
	[SetUp]
	public async Task Setup ()
		{
		_handler = new ();
		_http = new (_handler, false);
		_dashboard = new (new RainPointCloudClient (_http));
		_handler.Reply ("""{"code":0,"data":{"token":"fixture","tokenExpired":3600}}""");
		_handler.Reply ("""{"code":0,"data":[{"hid":5,"homeName":"Garden"}]}""");
		await _dashboard.ConnectAsync ("fixture@example.invalid", "fixture", "44");
		_handler.Reply (SceneTests.HUBS);
		await _dashboard.SelectHomeAsync (_dashboard.Homes.Single ());
		}
	[TearDown]
	public async Task Cleanup ()
		{
		await _dashboard.CloseAsync ();
		_http.Dispose ();
		_handler.Dispose ();
		}
	private async Task Scenes ()
		{
		_handler.Reply (SceneTests.LIST);
		_handler.Reply (SceneTests.HUBS);
		_handler.Reply (SceneTests.CATALOG);
		await _dashboard.LoadScenesAsync ();
		}
	private async Task Detail ()
		{
		await Scenes ();
		_dashboard.SelectedScene = _dashboard.Scenes.Single ();
		_handler.Reply (SceneTests.LIST);
		_handler.Reply (SceneTests.DETAIL);
		await _dashboard.LoadSelectedSceneAsync ();
		}
	[Test]
	public async Task SceneReadDoesNotActuateAndHomeChangeClearsState ()
		{
		await Detail ();
		Assert.That (_dashboard.CanWriteScene, Is.True);
		Assert.That (_dashboard.SceneConditions.Single ().Threshold, Is.EqualTo (50));
		Assert.That (_handler.Requests.Count (r => r.Method == HttpMethod.Post), Is.EqualTo (1));
		await _dashboard.SelectHomeAsync (null);
		Assert.That (_dashboard.CanWriteScene, Is.False);
		Assert.That (_dashboard.SceneConditions, Is.Empty);
		Assert.That (_dashboard.Scenes, Is.Empty);
		}
	[TestCase (true)]
	[TestCase (false)]
	public async Task SceneWriteRequiresReloadAfterAnyAttempt (bool fail)
		{
		await Detail ();
		_handler.Reply (SceneTests.LIST);
		_handler.Reply (SceneTests.DETAIL);
		_handler.Reply (fail ? "{\"code\":42}" : "{\"code\":0}");
		await _dashboard.SetSelectedSceneEnabledAsync (false);
		Assert.That (_dashboard.CanWriteScene, Is.False);
		int count = _handler.Requests.Count;
		await _dashboard.DeleteSelectedSceneAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (count));
		}
	[Test]
	public async Task FailedSceneReadClearsOldWritableState ()
		{
		await Detail ();
		_handler.Reply ("{\"code\":42}");
		await _dashboard.LoadSelectedSceneAsync ();
		Assert.That (_dashboard.CanWriteScene, Is.False);
		Assert.That (_dashboard.SceneActions, Is.Empty);
		}
	[Test]
	public async Task ExistingSceneCannotBeSilentlyEditedAsDraft ()
		{
		await Detail ();
		_dashboard.SceneThreshold = "90";
		_dashboard.AddSceneWeatherCondition ();
		Assert.That (_dashboard.SceneConditions, Has.Count.EqualTo (1));
		_dashboard.NewSceneDraft ();
		_dashboard.AddSceneWeatherCondition ();
		Assert.That (_dashboard.SceneConditions.Single ().Threshold, Is.EqualTo (90));
		Assert.That (_handler.Requests.Count (r => r.Method == HttpMethod.Post), Is.EqualTo (1));
		}
	[Test]
	public void InvalidLocalDraftDoesNotReachNetwork ()
		{
		_dashboard.NewSceneDraft ();
		int count = _handler.Requests.Count;
		_dashboard.SceneThreshold = "not a number";
		_dashboard.AddSceneWeatherCondition ();
		Assert.That (_dashboard.SceneConditions, Is.Empty);
		Assert.That (_handler.Requests, Has.Count.EqualTo (count));
		}
	[Test]
	public async Task WeatherRequiresSeparateAccessAndClearsOnHomeChange ()
		{
		Assert.That (_dashboard.CanReadWeather, Is.False);
		await _dashboard.LoadWeatherAsync ();
		_dashboard.ConfigureWeather (new ("fixtureKey", "fixtureSecret"));
		Assert.That (_dashboard.CanReadWeather, Is.True);
		_handler.Reply (AdministrationTests.HOME);
		_handler.Reply ("""{"code":0,"data":{"current":{"tempF":"68"},"hourly":{"items":[{"tempF":"68"}]},"daily":{"items":[{"tempMaxF":"77"}]}}}""");
		await _dashboard.LoadWeatherAsync ();
		Assert.That (_dashboard.CurrentWeather!.TemperatureCelsius, Is.EqualTo (20));
		Assert.That (_dashboard.HourlyWeather, Has.Count.EqualTo (1));
		await _dashboard.SelectHomeAsync (null);
		Assert.That (_dashboard.CurrentWeather, Is.Null);
		Assert.That (_dashboard.HourlyWeather, Is.Empty);
		Assert.That (_dashboard.CanReadWeather, Is.False);
		}
	[Test]
	public async Task WeatherFailureCannotDisplayPreviousObservationAsFresh ()
		{
		_dashboard.ConfigureWeather (new ("fixtureKey", "fixtureSecret"));
		_handler.Reply (AdministrationTests.HOME);
		_handler.Reply ("""{"code":0,"data":{"current":{"tempF":"68"}}}""");
		await _dashboard.LoadWeatherAsync ();
		_handler.Reply (AdministrationTests.HOME);
		_handler.Reply ("{\"code\":42}");
		await _dashboard.LoadWeatherAsync ();
		Assert.That (_dashboard.CurrentWeather, Is.Null);
		Assert.That (_dashboard.DailyWeather, Is.Empty);
		}
	[Test]
	public async Task EditingExistingSceneUsesReplacementAndConsumesBasis ()
		{
		_dashboard.SelectHub (_dashboard.Hubs.Single ());
		await Detail ();
		_dashboard.EditSelectedScene ();
		Assert.That (_dashboard.CanEditSceneDraft, Is.True);
		_dashboard.SceneNameDraft = "Updated fixture";
		_handler.Reply (SceneTests.HUBS);
		_handler.Reply (SceneTests.CATALOG);
		_handler.Reply ("""{"code":0,"data":[{"uid":10}]}""");
		_handler.Reply (AdministrationTests.HOME);
		_handler.Reply (SceneTests.LIST);
		_handler.Reply (SceneTests.DETAIL);
		_handler.Reply ("{\"code\":0}");
		await _dashboard.SaveNewSceneAsync ();
		using var body = System.Text.Json.JsonDocument.Parse (_handler.Requests.Last ().Body!);
		Assert.That (body.RootElement.GetProperty ("id").GetInt64 (), Is.EqualTo (12));
		Assert.That (body.RootElement.GetProperty ("sceneName").GetString (), Is.EqualTo ("Updated fixture"));
		Assert.That (_dashboard.CanWriteScene, Is.False);
		Assert.That (_dashboard.CanCreateScene, Is.False);
		}

	[Test]
	public async Task HistoryPagingCannotReuseChangedRangeAndHomeChangeClearsResults ()
		{
		_dashboard.SceneHistoryFromUtc = "2026-09-01";
		_dashboard.SceneHistoryThroughUtc = "2026-09-25";
		_handler.Reply (SceneHistoryTests.Response ("""[{"aid":1,"mid":101,"addr":2,"rt":0}]""", total: 100));
		await _dashboard.LoadSceneHistoryAsync ();
		_dashboard.SelectedSceneLog = _dashboard.SceneHistory.Single ();
		Assert.That (_dashboard.SceneHistoryActions.Single ().ResultCode, Is.EqualTo (0));
		Assert.That (_dashboard.CanReadNextSceneHistory, Is.True);
		int count = _handler.Requests.Count;
		_dashboard.SceneHistoryFromUtc = "2026-09-02";
		await _dashboard.NextSceneHistoryAsync ();
		Assert.That (_handler.Requests, Has.Count.EqualTo (count));
		await _dashboard.SelectHomeAsync (null);
		Assert.That (_dashboard.SceneHistory, Is.Empty);
		Assert.That (_dashboard.SceneHistoryActions, Is.Empty);
		Assert.That (_dashboard.CanReadNextSceneHistory, Is.False);
		}
	[Test]
	public async Task FailedHistoryRefreshClearsPreviousResults ()
		{
		_handler.Reply (SceneHistoryTests.Response ("[]"));
		await _dashboard.LoadSceneHistoryAsync ();
		_handler.Reply ("{\"code\":42}");
		await _dashboard.LoadSceneHistoryAsync ();
		Assert.That (_dashboard.SceneHistory, Is.Empty);
		Assert.That (_dashboard.CanReadNextSceneHistory, Is.False);
		}

	}
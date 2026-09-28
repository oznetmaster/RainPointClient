// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
namespace RainPointClient.Desktop.Core;

public sealed partial class Dashboard
	{
	private RainPointSceneLogPage? _sceneLogPage;
	private DateTimeOffset _sceneLogFrom, _sceneLogThrough;
	private long? _sceneLogFilter;
	private RainPointSceneLogEntry? _selectedSceneLog;
	public ObservableCollection<RainPointSceneLogEntry> SceneHistory { get; } = new ();
	public string SceneHistoryFromUtc { get; set; } = DateTime.UtcNow.AddDays (-7).ToString ("yyyy-MM-dd", CultureInfo.InvariantCulture);
	public string SceneHistoryThroughUtc { get; set; } = DateTime.UtcNow.ToString ("yyyy-MM-dd", CultureInfo.InvariantCulture);
	public string SceneHistoryMessage { get; private set; } = "Load scene history. Dates below use UTC; no scene is executed.";
	public RainPointSceneLogEntry? SelectedSceneLog
		{
		get => _selectedSceneLog; set
			{
			_selectedSceneLog = value;
			Changed ();
			}
		}
	public IReadOnlyList<RainPointSceneActionResult> SceneHistoryActions => SelectedSceneLog?.Actions ?? Array.Empty<RainPointSceneActionResult> ();
	public string SceneHistoryActionMessage => SelectedSceneLog is null ? "Select a history entry." : SelectedSceneLog.Actions is null ? "No action results supplied." : "Cloud result codes; not physical device confirmation.";
	public bool CanReadSceneHistory => CanReadScenes;
	public bool CanReadNextSceneHistory => CanReadSceneHistory && _sceneLogPage?.HasMore == true;
	private void ClearSceneHistory ()
		{
		_sceneLogPage = null;
		SceneHistory.Clear ();
		SelectedSceneLog = null;
		SceneHistoryMessage = "Load scene history. Dates below use UTC; no scene is executed.";
		}
	public Task LoadSceneHistoryAsync () => ReadSceneHistoryAsync (false);
	public Task NextSceneHistoryAsync () => ReadSceneHistoryAsync (true);
	private Task ReadSceneHistoryAsync (bool next)
		{
		if (!CanReadSceneHistory || next && !CanReadNextSceneHistory)
			return Task.CompletedTask;
		DateTimeOffset from, through;
		try
			{
			from = new DateTimeOffset (DateTime.ParseExact (SceneHistoryFromUtc, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None), TimeSpan.Zero);
			through = new DateTimeOffset (DateTime.ParseExact (SceneHistoryThroughUtc, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None), TimeSpan.Zero).AddDays (1).AddMilliseconds (-1);
			if (from > through)
				throw new FormatException ();
			}
		catch (Exception e) when (e is FormatException or ArgumentOutOfRangeException) { SceneHistoryMessage = "Use ordered UTC dates in yyyy-MM-dd format."; Changed (); return Task.CompletedTask; }
		long homeId = _home!.Id;
		long? filter = SelectedScene?.Id;
		if (next && (from != _sceneLogFrom || through != _sceneLogThrough || filter != _sceneLogFilter))
			{
			SceneHistoryMessage = "The filter changed. Load the first page again.";
			Changed ();
			return Task.CompletedTask;
			}
		int page = next ? _sceneLogPage!.Page + 1 : 0;
		return RunAsync (async token =>
		{
			ClearSceneHistory ();
			var result = await _client.GetSceneHistoryAsync (homeId, from, through, page, sceneId: filter, cancellationToken: token);
			_sceneLogPage = result;
			_sceneLogFrom = from;
			_sceneLogThrough = through;
			_sceneLogFilter = filter;
			foreach (var entry in result.Entries)
				SceneHistory.Add (entry);
			SceneHistoryMessage = $"Page {result.Page + 1}: {result.Entries.Count} entries; cloud total {result.Total}. UTC timestamps. Result codes do not prove physical execution.";
		}, "Scene history could not be read. Reload explicitly.");
		}
	}
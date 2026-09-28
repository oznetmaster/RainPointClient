// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
namespace RainPointClient.Desktop.Core;

public sealed partial class Dashboard
	{
	private RainPointSessionRecovery? _sessionRecovery;
	public string RecoveryText { get; private set; } = "Automatic session renewal is off.";
	public bool IsRecoveringSession => _sessionRecovery?.State is RainPointSessionState.Renewing or RainPointSessionState.SigningIn;
	public Task StartSessionRecoveryAsync (Action<Action> dispatch, Func<CancellationToken, Task<RainPointCredentials?>>? credentials = null) => RunAsync (token =>
	 {
		 if (dispatch is null)
			 throw new ArgumentNullException (nameof (dispatch));
		 if (_sessionRecovery is not null)
			 return Task.CompletedTask;
		 var worker = new RainPointSessionRecovery (_client, credentials);
		 _sessionRecovery = worker;
		 worker.StateChanged += (_, args) => dispatch (() =>
		{
			if (!ReferenceEquals (_sessionRecovery, worker) || _closing)
				return;
			if (args.State is RainPointSessionState.Renewing or RainPointSessionState.SigningIn or RainPointSessionState.AuthenticationRequired)
				{
				_armed = false;
				ClearSettings ();
				ClearPlans ();
				ClearCalendar ();
				ClearHubTools ();
				}
			RecoveryText = args.State switch
				{
					RainPointSessionState.Healthy => "Session renewal active.",
					RainPointSessionState.Renewing => "Renewing session…",
					RainPointSessionState.SigningIn => "Reconnecting with the saved account…",
					RainPointSessionState.CoolingDown => "Session recovery is waiting before another attempt.",
					RainPointSessionState.AuthenticationRequired => "Session recovery needs explicit sign-in. No command was retried.",
					_ => "Automatic session renewal is off."
					};
			Changed ();
		});
		 try
			 {
			 Task run = worker.RunAsync (token);
			 RecoveryText = "Session renewal active.";
			 _ = ObserveSessionRecoveryAsync (worker, dispatch, run);
			 }
		 catch
			 {
			 _sessionRecovery = null;
			 RecoveryText = "Automatic session renewal is off.";
			 throw;
			 }
		 return Task.CompletedTask;
	 }, "Session recovery could not be started.");
	private async Task ObserveSessionRecoveryAsync (RainPointSessionRecovery worker, Action<Action> dispatch, Task run)
		{
		try
			{
			await run;
			}
		catch (Exception error) when (error is not OutOfMemoryException) { }
		dispatch (() => { if (ReferenceEquals (_sessionRecovery, worker) && !_closing) { _sessionRecovery = null; RecoveryText = "Session recovery stopped. Sign in again to restart it."; Changed (); } });
		}
	public Task StopSessionRecoveryAsync () => RunAsync (_ => StopSessionRecoveryCoreAsync (), "Session recovery could not stop cleanly.");
	private async Task StopSessionRecoveryCoreAsync ()
		{
		var worker = _sessionRecovery;
		_sessionRecovery = null;
		if (worker is not null)
			await worker.StopAsync ();
		RecoveryText = "Automatic session renewal is off.";
		Changed ();
		}
	}
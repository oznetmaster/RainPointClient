using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace RainPointClient.Desktop.Core;

public sealed partial class Dashboard
	{
	private RainPointZoneProfileSnapshot? _profile;
	private int _profileZone = 1;
	private bool _recommendationsEnabled;
	private bool _unknownProfileOptions;
	private IReadOnlyList<RainPointWateringRecommendation> _recommendations = Array.Empty<RainPointWateringRecommendation> ();
	public ObservableCollection<ProfileCategoryRow> ProfileCategories { get; } = new ();
	public int ProfileZone
		{
		get => _profileZone;
		set
			{
			if (!CanEdit || value is < 1 or > 3 || value == _profileZone)
				return;
			_profileZone = value;
			ClearProfiles ();
			Changed ();
			}
		}
	public bool RecommendationsEnabled
		{
		get => _recommendationsEnabled;
		set
			{
			if (!CanEdit)
				return;
			_recommendationsEnabled = value;
			Changed ();
			}
		}
	public string ProfileMessage { get; private set; } = "Load the selected zone's profile.";
	public string RecommendationText { get; private set; } = "No recommendation requested.";
	public bool CanLoadProfile => CanRefresh && _timer is not null;
	public bool CanSaveProfile => CanLoadProfile && _profile?.Availability == TimerReadingAvailability.Decoded && !_unknownProfileOptions && ProfileCategories.Count > 0 && ProfileCategories.All (c => c.Selected is not null);
	public bool CanPrepareRecommendedPlan => CanLoadProfile && SuggestedPlanValues (out _, out _);
	private void ClearProfiles ()
		{
		_profile = null;
		_unknownProfileOptions = false;
		_recommendationsEnabled = false;
		ProfileCategories.Clear ();
		_recommendations = Array.Empty<RainPointWateringRecommendation> ();
		ProfileMessage = "Load the selected zone's profile.";
		RecommendationText = "No recommendation requested.";
		}
	public Task LoadProfileAsync ()
		{
		if (!CanLoadProfile)
			return Task.CompletedTask;
		return RunAsync (async token =>
		{
			ClearProfiles ();
			var catalog = await _client.GetZoneProfileCatalogAsync (token);
			var profile = await _client.GetZoneProfileAsync (_hub!, _timer!.Address, _profileZone, token);
			if (profile.Availability != TimerReadingAvailability.Decoded)
				{
				ProfileMessage = "The stored profile is unreadable. No default values will be saved over it.";
				return;
				}
			_profile = profile;
			_recommendationsEnabled = profile.RecommendationsEnabled == true;
			_unknownProfileOptions = profile.SelectedOptionIds.Any (id => !catalog.Any (c => c.Options.Any (o => o.Id == id)));
			foreach (var category in catalog)
				ProfileCategories.Add (new ProfileCategoryRow (category, profile.SelectedOptionIds, () => CanEdit, Changed));
			ProfileMessage = _unknownProfileOptions ? "The saved profile contains options absent from the catalog. Editing is blocked to preserve them." : "Profile loaded. Unset categories display the vendor's defaults; saving does not create a watering plan.";
		}, "Profile could not be loaded. Reload explicitly.");
		}
	public Task SaveProfileAsync ()
		{
		if (!CanSaveProfile)
			return Task.CompletedTask;
		var expected = _profile!;
		var hub = _hub!;
		bool enabled = _recommendationsEnabled;
		int[] selected = ProfileCategories.Select (c => c.Selected!.Id).ToArray ();
		return RunAsync (async token =>
		{
			_profile = null;
			_recommendations = Array.Empty<RainPointWateringRecommendation> ();
			RecommendationText = "Profile changed; request recommendations again.";
			try
				{
				await _client.SetZoneProfileAsync (hub, expected, enabled, selected, token);
				var saved = await _client.GetZoneProfileAsync (hub, expected.Address, expected.Zone, token);
				if (saved.Availability != TimerReadingAvailability.Decoded || saved.RecommendationsEnabled != enabled || !saved.SelectedOptionIds.OrderBy (id => id).SequenceEqual (selected.OrderBy (id => id)))
					{
					ProfileMessage = "Cloud accepted the profile, but read-back differs. Reload; no write was retried.";
					return;
					}
				_profile = saved;
				ProfileMessage = "Profile matches cloud read-back. No watering plan was created or enabled.";
				}
			catch { ProfileMessage = "Save or read-back failed; the outcome may be unknown. Reload; no write was retried."; throw; }
		}, "Profile operation failed. See the profile message.");
		}
	public Task LoadRecommendationsAsync ()
		{
		if (!CanLoadProfile)
			return Task.CompletedTask;
		return RunAsync (async token =>
		{
			_recommendations = Array.Empty<RainPointWateringRecommendation> ();
			RecommendationText = "Loading recommendation…";
			try
				{
				_recommendations = await _client.GetZoneRecommendationsAsync (_hub!, _timer!.Address, _profileZone, token);
				RecommendationText = _recommendations.Count == 0 ? "No recommendation returned." : string.Join ("\n", _recommendations.Select (r => "Interval: " + (r.IntervalDays?.ToString (CultureInfo.InvariantCulture) ?? "unknown") + " days · Duration: " + (r.Duration?.TotalMinutes.ToString ("0.##", CultureInfo.InvariantCulture) ?? "unknown") + " minutes.")) + "\nCloud suggestions require review; they do not create or enable watering.";
				}
			catch { RecommendationText = "Recommendation could not be read. Retry explicitly."; throw; }
		}, "Recommendation read failed.");
		}
	private bool SuggestedPlanValues (out int days, out int seconds)
		{
		days = seconds = 0;
		if (_recommendations.FirstOrDefault () is not { IntervalDays: > 0, Duration: { } duration } first)
			return false;
		decimal interval = decimal.Floor (first.IntervalDays!.Value + 0.5m);
		double minutes = Math.Ceiling (duration.TotalMinutes);
		if (interval is < 1 or > 127 || minutes < 1 || minutes > 720)
			return false;
		days = (int)interval;
		seconds = (int)minutes * 60;
		return true;
		}
	public async Task PrepareRecommendedPlanAsync ()
		{
		if (!CanPrepareRecommendedPlan || !SuggestedPlanValues (out int days, out int seconds))
			return;
		PlanZone = _profileZone;
		await LoadPlansAsync ();
		if (!CanNewPlan)
			return;
		NewPlan ();
		PlanDraft.Duration = seconds.ToString (CultureInfo.InvariantCulture);
		PlanDraft.Repeat = days == 1 ? "Every day" : "Every N days";
		PlanDraft.Interval = days.ToString (CultureInfo.InvariantCulture);
		// Deliberately leave the start date for the user to review in the home's calendar.
		ProfileMessage = "Disabled draft prepared in Plans. Review start time and effective date before saving.";
		Changed ();
		}
	}
public sealed class ProfileOptionRow
	{
	internal ProfileOptionRow (RainPointProfileOption option)
		{
		Id = option.Id;
		Name = ProfileCategoryRow.Label (option.Label);
		}
	public int Id
		{
		get;
		}
	public string Name
		{
		get;
		}
	}
public sealed class ProfileCategoryRow : INotifyPropertyChanged
	{
	private readonly Func<bool> _canEdit;
	private readonly Action _changed;
	private ProfileOptionRow? _selected;
	public event PropertyChangedEventHandler? PropertyChanged;
	internal ProfileCategoryRow (RainPointProfileCategory category, IReadOnlyList<int> saved, Func<bool> canEdit, Action changed)
		{
		Name = Label (category.Label);
		_canEdit = canEdit;
		_changed = changed;
		Options = Array.AsReadOnly (category.Options.Select (o => new ProfileOptionRow (o)).ToArray ());
		int? selected = category.Options.FirstOrDefault (o => saved.Contains (o.Id))?.Id ?? category.Options.FirstOrDefault (o => o.IsDefault)?.Id;
		_selected = Options.FirstOrDefault (o => o.Id == selected);
		}
	public string Name
		{
		get;
		}
	public IReadOnlyList<ProfileOptionRow> Options
		{
		get;
		}
	public ProfileOptionRow? Selected
		{
		get => _selected;
		set
			{
			if (!_canEdit () || (value is not null && !Options.Contains (value)))
				return;
			_selected = value;
			PropertyChanged?.Invoke (this, new PropertyChangedEventArgs (nameof (Selected)));
			_changed ();
			}
		}
	internal static string Label (string value)
		{
		string text = value.StartsWith ("@plan_config_", StringComparison.Ordinal) ? value.Substring (13) : value.TrimStart ('@');
		return CultureInfo.InvariantCulture.TextInfo.ToTitleCase (text.Replace ('_', ' '));
		}
	}
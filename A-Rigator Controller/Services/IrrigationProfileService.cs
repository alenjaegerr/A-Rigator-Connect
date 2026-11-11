using System.Collections.ObjectModel;
using System.Text.Json;
using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.Services;

public sealed class IrrigationProfileService
{
    private const string ProfilesKey = "arigator.irrigation.profiles.v1";
    private const string SelectedProfileKey = "arigator.irrigation.selected-profile.v1";

    public ObservableCollection<IrrigationProfile> Profiles { get; } = new();

    public IrrigationProfile? SelectedProfile { get; private set; }

    public event EventHandler? ProfilesChanged;
    public event EventHandler? SelectedProfileChanged;

    public IrrigationProfileService()
    {
        Load();
    }

    public void SaveProfile(IrrigationProfile profile)
    {
        Validate(profile);
        var existing = Profiles.FirstOrDefault(item => item.Id == profile.Id);
        if (existing is null)
            Profiles.Add(profile);
        else
        {
            var index = Profiles.IndexOf(existing);
            Profiles[index] = profile;
        }

        Persist();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteProfile(string id)
    {
        var profile = Profiles.FirstOrDefault(item => item.Id == id);
        if (profile is null || Profiles.Count <= 1)
            return;

        Profiles.Remove(profile);
        if (SelectedProfile?.Id == id)
            SelectProfile(Profiles[0].Id);

        Persist();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectProfile(string id)
    {
        SelectedProfile = Profiles.FirstOrDefault(item => item.Id == id)
            ?? Profiles.FirstOrDefault();
        if (SelectedProfile is not null)
            Preferences.Default.Set(SelectedProfileKey, SelectedProfile.Id);
        SelectedProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Load()
    {
        try
        {
            var json = Preferences.Default.Get(ProfilesKey, string.Empty);
            var saved = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<List<IrrigationProfile>>(json);
            if (saved is not null)
            {
                foreach (var profile in saved)
                {
                    try { Validate(profile); Profiles.Add(profile); }
                    catch { /* Skip malformed stored entries without losing valid profiles. */ }
                }
            }
        }
        catch
        {
            // Recover with a safe default if local preferences are unreadable.
        }

        if (Profiles.Count == 0)
        {
            Profiles.Add(new IrrigationProfile { Name = "Balanced watering" });
            Persist();
        }

        var selectedId = Preferences.Default.Get(SelectedProfileKey, string.Empty);
        SelectedProfile = Profiles.FirstOrDefault(item => item.Id == selectedId)
            ?? Profiles[0];
        Preferences.Default.Set(SelectedProfileKey, SelectedProfile.Id);
    }

    private void Persist()
    {
        Preferences.Default.Set(ProfilesKey, JsonSerializer.Serialize(Profiles.ToList()));
    }

    private static void Validate(IrrigationProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Trim().Length > 32)
            throw new ArgumentException("Profile name must contain 1–32 characters.");
        if (profile.LeftDurationSeconds is < 1 or > 60 ||
            profile.BackDurationSeconds is < 1 or > 60 ||
            profile.RightDurationSeconds is < 1 or > 60)
            throw new ArgumentOutOfRangeException(nameof(profile), "Each watering duration must be between 1 and 60 seconds.");
        if (profile.RepeatCount is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(profile), "Repeat count must be between 1 and 20.");
        profile.Name = profile.Name.Trim();
    }
}

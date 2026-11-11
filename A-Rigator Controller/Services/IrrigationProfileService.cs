using Microsoft.Maui.Storage;
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

    public IrrigationProfileService() => Load();

    public void SaveProfile(IrrigationProfile profile)
    {
        Validate(profile);
        profile.ApplyDirectionMode();
        var existing = Profiles.FirstOrDefault(item => item.Id == profile.Id);
        if (existing is null) Profiles.Add(profile);
        else Profiles[Profiles.IndexOf(existing)] = profile;
        Persist();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteProfile(string id)
    {
        var profile = Profiles.FirstOrDefault(item => item.Id == id);
        if (profile is null || Profiles.Count <= 1) return;
        Profiles.Remove(profile);
        if (SelectedProfile?.Id == id) SelectProfile(Profiles[0].Id);
        Persist();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectProfile(string id)
    {
        SelectedProfile = Profiles.FirstOrDefault(item => item.Id == id) ?? Profiles.FirstOrDefault();
        if (SelectedProfile is not null) Preferences.Default.Set(SelectedProfileKey, SelectedProfile.Id);
        SelectedProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Load()
    {
        try
        {
            var json = Preferences.Default.Get(ProfilesKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(json))
            {
                using var document = JsonDocument.Parse(json);
                var saved = JsonSerializer.Deserialize<List<IrrigationProfile>>(json) ?? new();
                var index = 0;
                foreach (var profile in saved)
                {
                    try
                    {
                        // Older profiles had three separate durations and no direction mode.
                        // Preserve their broad coverage as a full sweep and use the longest
                        // legacy duration as the new per-direction timer.
                        var item = document.RootElement.ValueKind == JsonValueKind.Array
                            ? document.RootElement.EnumerateArray().ElementAtOrDefault(index)
                            : default;
                        if (item.ValueKind == JsonValueKind.Object &&
                            !item.TryGetProperty("DirectionMode", out _))
                        {
                            profile.DirectionMode = "FullSweep";
                            profile.DurationSeconds = Math.Clamp(
                                Math.Max(profile.LeftDurationSeconds,
                                    Math.Max(profile.BackDurationSeconds, profile.RightDurationSeconds)), 1, 60);
                            profile.ApplyDirectionMode();
                        }
                        Validate(profile);
                        Profiles.Add(profile);
                    }
                    catch { /* Skip malformed stored entries without losing valid profiles. */ }
                    index++;
                }
            }
        }
        catch { /* Recover with a safe default if local preferences are unreadable. */ }

        if (Profiles.Count == 0)
        {
            var initial = new IrrigationProfile { Name = "Balanced watering" };
            initial.ApplyDirectionMode();
            Profiles.Add(initial);
            Persist();
        }

        var selectedId = Preferences.Default.Get(SelectedProfileKey, string.Empty);
        SelectedProfile = Profiles.FirstOrDefault(item => item.Id == selectedId) ?? Profiles[0];
        Preferences.Default.Set(SelectedProfileKey, SelectedProfile.Id);
        Persist();
    }

    private void Persist() =>
        Preferences.Default.Set(ProfilesKey, JsonSerializer.Serialize(Profiles.ToList()));

    private static void Validate(IrrigationProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Trim().Length > 32)
            throw new ArgumentException("Profile name must contain 1–32 characters.");
        if (profile.DirectionMode is not ("LeftSweep" or "RightSweep" or "BackSweep" or "BothSides" or "FullSweep"))
            throw new ArgumentException("Choose a valid nozzle sweep mode.");
        if (profile.DurationSeconds is < 1 or > 60)
            throw new ArgumentOutOfRangeException(nameof(profile), "Sweep duration must be between 1 and 60 seconds.");
        if (profile.RepeatCount is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(profile), "Pass count must be between 1 and 20.");
        profile.Name = profile.Name.Trim();
    }
}

using System.Text.Json;
using A_Rigator_Controller.Models;
using A_Rigator_Controller.Services;
using A_Rigator_Controller.ViewModels;
using Microsoft.Maui.Storage;

namespace A_Rigator_Controller.Views;

public partial class AccountPage : ContentPage
{
    private readonly IrrigationProfileService _profiles;
    private readonly IAccountService _account;
    private readonly IRemoteRobotConfigurationStore _robotConfiguration;
    private readonly IrrigationProfileSyncCoordinator _profileSync;

    public AccountPage(AccountViewModel viewModel, DashboardViewModel dashboardViewModel, IrrigationProfileService profiles,
        IAccountService account, IRemoteRobotConfigurationStore robotConfiguration,
        IrrigationProfileSyncCoordinator profileSync)
    {
        InitializeComponent();
        BindingContext = viewModel;
        MascotHeader.BindingContext = dashboardViewModel;
        viewModel.AvatarChanged += (_, path) =>
            MessagingCenter.Send(this, "ProfileAvatarChanged", path);
        _profiles = profiles;
        _account = account;
        _robotConfiguration = robotConfiguration;
        _profileSync = profileSync;
    }

    private async void OnOpenSettingsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("settings");
    }

    private async void OnImportProfilesClicked(object sender, EventArgs e)
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choose an A-Rigator irrigation profile JSON file"
            });
            if (file is null) return;
            if (!string.Equals(Path.GetExtension(file.FileName), ".json", StringComparison.OrdinalIgnoreCase))
            {
                await DisplayAlert("Unsupported file", "Please select a .json irrigation profile file.", "OK");
                return;
            }

            await using var stream = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            if (memory.Length is 0 or > 1_048_576)
            {
                await DisplayAlert("Invalid file", "The JSON file is empty or exceeds the 1 MB import limit.", "OK");
                return;
            }

            var json = System.Text.Encoding.UTF8.GetString(memory.ToArray());
            var imported = JsonSerializer.Deserialize<List<IrrigationProfile>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (imported is null || imported.Count is < 1 or > 100)
            {
                await DisplayAlert("Invalid profile file", "The file must contain between 1 and 100 irrigation profiles.", "OK");
                return;
            }

            var added = _profiles.ImportProfiles(imported);
            await DisplayAlert("Import complete",
                $"Imported {imported.Count} profile(s). {added} new profile(s) were added; matching IDs were updated when newer.",
                "OK");
        }
        catch (JsonException)
        {
            await DisplayAlert("Invalid JSON", "This file is not a valid A-Rigator irrigation profile export.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Import failed", ex.Message, "OK");
        }
    }

    private async void OnDeleteAccountClicked(object sender, EventArgs e)
    {
        var confirmed = await DisplayAlert("Delete A-Rigator account?",
            "This permanently deletes your cloud account, registered robot records, and cloud irrigation profiles. This action cannot be undone. Local app data on this device will also be reset.",
            "Delete account", "Cancel");
        if (!confirmed) return;

        try
        {
            await _account.DeleteAccountAsync();
            _profiles.ResetForDeletedAccount();
            await _profileSync.ClearAccountSyncStateAsync();
            await _robotConfiguration.ClearAsync();
            ClearLocalAccountData();
            await DisplayAlert("Account deleted",
                "Your A-Rigator account and associated cloud data have been deleted. You can continue using local features as a guest.",
                "OK");
            await Shell.Current.GoToAsync("//main/home");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Deletion failed",
                $"Your account was not confirmed as deleted. Please try again or contact support. Details: {ex.Message}",
                "OK");
        }
    }

    private static void ClearLocalAccountData()
    {
        var avatarPaths = new[]
        {
            Preferences.Default.Get<string?>("arigator.account.avatar", null),
            Preferences.Default.Get<string?>("arigator.robot.avatar", null)
        };

        foreach (var path in avatarPaths)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                var fullPath = Path.GetFullPath(path);
                var appDirectory = Path.GetFullPath(FileSystem.AppDataDirectory) + Path.DirectorySeparatorChar;
                if (fullPath.StartsWith(appDirectory, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
                    File.Delete(fullPath);
            }
            catch { }
        }

        foreach (var key in new[]
        {
            "display_name", "arigator.robot.nickname",
            "arigator.account.avatar", "arigator.robot.avatar"
        })
            Preferences.Default.Remove(key);
    }
}

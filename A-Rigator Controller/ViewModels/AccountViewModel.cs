using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using A_Rigator_Controller.Services;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;

namespace A_Rigator_Controller.ViewModels;

public sealed class AccountViewModel : INotifyPropertyChanged
{
    private readonly IAccountService _account;
    private readonly IUserProfileService _userProfile;
    private readonly IIrrigationProfileCloudService _cloudProfiles;
    private readonly IrrigationProfileService _localProfiles;
    private string _displayName = "";
    private string _robotName = "A-Rigator 01";
    private string? _avatarPath;
    private string _syncStatus = "Sign in to download irrigation profiles saved to your account.";
    private bool _isBusy;

    public AccountViewModel(IAccountService account, IUserProfileService userProfile,
        IIrrigationProfileCloudService cloudProfiles, IrrigationProfileService localProfiles)
    {
        _account = account;
        _userProfile = userProfile;
        _cloudProfiles = cloudProfiles;
        _localProfiles = localProfiles;

        _displayName = _userProfile.GetDisplayName() ?? _account.DisplayName ?? "A-Rigator User";
        _robotName = Preferences.Default.Get("arigator.robot.nickname", "A-Rigator 01");
        _avatarPath = Preferences.Default.Get<string?>("arigator.account.avatar", null);

        SignInCommand = new Command(async () => await SignInAsync(), () => !IsBusy);
        SignOutCommand = new Command(async () => await SignOutAsync(), () => !IsBusy);
        SaveAccountCommand = new Command(SaveAccount);
        DownloadProfilesCommand = new Command(async () => await DownloadProfilesAsync(), () => !IsBusy && IsAuthenticated);
        PickAvatarCommand = new Command(async () => await PickAvatarAsync());

        _ = RestoreSessionAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string DisplayName { get => _displayName; set { if (_displayName == value) return; _displayName = value; OnPropertyChanged(); } }
    public string RobotName { get => _robotName; set { if (_robotName == value) return; _robotName = value; OnPropertyChanged(); } }
    public string? AvatarPath { get => _avatarPath; private set { _avatarPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAvatar)); } }
    public bool HasAvatar => !string.IsNullOrWhiteSpace(AvatarPath) && File.Exists(AvatarPath);
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); (SignInCommand as Command)?.ChangeCanExecute(); (SignOutCommand as Command)?.ChangeCanExecute(); (DownloadProfilesCommand as Command)?.ChangeCanExecute(); } }
    public bool IsAuthenticated => _account.IsAuthenticated;
    public bool IsGuest => !IsAuthenticated;
    public string EmailAddress => _account.Email ?? "Not linked";
    public string AccountSummary => IsAuthenticated ? (_account.Email ?? "Signed in") : "Guest account — local features available";
    public string SyncStatus { get => _syncStatus; private set { _syncStatus = value; OnPropertyChanged(); } }

    public ICommand SignInCommand { get; }
    public ICommand SignOutCommand { get; }
    public ICommand SaveAccountCommand { get; }
    public ICommand DownloadProfilesCommand { get; }
    public ICommand PickAvatarCommand { get; }

    private async Task RestoreSessionAsync()
    {
        try { await _account.InitializeSessionAsync(); }
        catch { /* Keep local account settings available if session restoration fails. */ }
        OnPropertyChanged(nameof(IsAuthenticated));
        OnPropertyChanged(nameof(IsGuest));
        OnPropertyChanged(nameof(AccountSummary));
        OnPropertyChanged(nameof(EmailAddress));
        (DownloadProfilesCommand as Command)?.ChangeCanExecute();
        if (IsAuthenticated) SyncStatus = "Signed in. Download your saved irrigation profiles whenever you need them.";
    }

    private async Task SignInAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _account.SignInWithGoogleAsync();
            if (string.IsNullOrWhiteSpace(_userProfile.GetDisplayName()) && !string.IsNullOrWhiteSpace(_account.DisplayName))
                DisplayName = _account.DisplayName!;
            SaveAccount();
            SyncStatus = "Signed in successfully. Your account's irrigation profiles are ready to download.";
        }
        catch (Exception ex) { SyncStatus = $"Sign-in failed: {ex.Message}"; }
        finally { IsBusy = false; OnPropertyChanged(nameof(IsAuthenticated)); OnPropertyChanged(nameof(IsGuest)); OnPropertyChanged(nameof(AccountSummary)); OnPropertyChanged(nameof(EmailAddress)); }
    }

    private async Task SignOutAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await _account.SignOutAsync(); SyncStatus = "Signed out. Local profiles and robot controls remain available."; }
        catch (Exception ex) { SyncStatus = ex.Message; }
        finally { IsBusy = false; OnPropertyChanged(nameof(IsAuthenticated)); OnPropertyChanged(nameof(IsGuest)); OnPropertyChanged(nameof(AccountSummary)); OnPropertyChanged(nameof(EmailAddress)); }
    }

    private void SaveAccount()
    {
        if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Trim().Length > 48)
        {
            SyncStatus = "Display name must contain 1–48 characters.";
            return;
        }
        if (string.IsNullOrWhiteSpace(RobotName) || RobotName.Trim().Length > 32)
        {
            SyncStatus = "Robot name must contain 1–32 characters.";
            return;
        }
        _userProfile.SaveDisplayName(DisplayName.Trim());
        Preferences.Default.Set("arigator.robot.nickname", RobotName.Trim());
        DisplayName = DisplayName.Trim();
        RobotName = RobotName.Trim();
        SyncStatus = IsAuthenticated
            ? "Account settings saved on this device. Cloud account sync will be added with the account-profile update."
            : "Account settings saved on this device. Sign in to associate them with your account.";
    }

    private async Task PickAvatarAsync()
    {
        try
        {
            var photo = await MediaPicker.Default.PickPhotoAsync();
            if (photo is null) return;
            var extension = Path.GetExtension(photo.FileName);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".jpg";
            var destination = Path.Combine(FileSystem.AppDataDirectory, $"account-avatar{extension}");
            await using var source = await photo.OpenReadAsync();
            await using var target = File.Create(destination);
            await source.CopyToAsync(target);
            AvatarPath = destination;
            Preferences.Default.Set("arigator.account.avatar", destination);
            SyncStatus = "Profile picture saved on this device.";
        }
        catch (Exception ex) { SyncStatus = $"Could not select profile picture: {ex.Message}"; }
    }

    private async Task DownloadProfilesAsync()
    {
        if (IsBusy || !IsAuthenticated) return;
        IsBusy = true;
        SyncStatus = "Downloading your irrigation profiles…";
        try
        {
            var downloaded = await _cloudProfiles.DownloadProfilesAsync();
            foreach (var profile in downloaded) _localProfiles.SaveProfile(profile);
            SyncStatus = downloaded.Count == 0
                ? "No irrigation profiles are saved to this account yet."
                : $"Irrigation profiles have been loaded. {downloaded.Count} profile(s) are available in Auto.";
        }
        catch (Exception ex) { SyncStatus = $"Profile download failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

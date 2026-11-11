using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using A_Rigator_Controller.Services;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;

namespace A_Rigator_Controller.ViewModels;

public sealed class AccountViewModel : INotifyPropertyChanged
{
    private readonly IAccountService _account;
    private readonly IUserProfileService _userProfile;
    private readonly IrrigationProfileService _localProfiles;
    private readonly IrrigationProfileSyncCoordinator _sync;
    private string _displayName = "";
    private string _robotName = "A-Rigator 01";
    private string? _avatarPath;
    private string? _robotAvatarPath;
    private string _syncStatus = "Your irrigation profiles are stored locally on this device.";
    private bool _isBusy;

    public AccountViewModel(IAccountService account, IUserProfileService userProfile,
        IrrigationProfileService localProfiles, IrrigationProfileSyncCoordinator sync)
    {
        _account = account;
        _userProfile = userProfile;
        _localProfiles = localProfiles;
        _sync = sync;

        _displayName = _userProfile.GetDisplayName() ?? _account.DisplayName ?? "A-Rigator User";
        _robotName = Preferences.Default.Get("arigator.robot.nickname", "A-Rigator 01");
        _avatarPath = Preferences.Default.Get<string?>("arigator.account.avatar", null);
        _robotAvatarPath = Preferences.Default.Get<string?>("arigator.robot.avatar", null);

        SignInCommand = new Command(async () => await SignInAsync(), () => !IsBusy);
        SignOutCommand = new Command(async () => await SignOutAsync(), () => !IsBusy);
        SaveAccountCommand = new Command(SaveAccount);
        SyncProfilesCommand = new Command(async () => await SyncProfilesAsync(), () => !IsBusy && IsAuthenticated);
        DownloadLocalProfilesCommand = new Command(async () => await DownloadLocalProfilesAsync(), () => !IsBusy);
        PickAvatarCommand = new Command(async () => await PickPhotoAsync(false));
        PickRobotPhotoCommand = new Command(async () => await PickPhotoAsync(true));
        _sync.StatusChanged += OnSyncStatusChanged;
        _ = RestoreSessionAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string DisplayName { get => _displayName; set { if (_displayName == value) return; _displayName = value; OnPropertyChanged(); } }
    public string RobotName { get => _robotName; set { if (_robotName == value) return; _robotName = value; OnPropertyChanged(); } }
    public string? AvatarPath { get => _avatarPath; private set { _avatarPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAvatar)); } }
    public string? RobotAvatarPath { get => _robotAvatarPath; private set { _robotAvatarPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRobotAvatar)); } }
    public bool HasAvatar => !string.IsNullOrWhiteSpace(AvatarPath) && File.Exists(AvatarPath);
    public bool HasRobotAvatar => !string.IsNullOrWhiteSpace(RobotAvatarPath) && File.Exists(RobotAvatarPath);
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); RefreshCommands(); } }
    public bool IsAuthenticated => _account.IsAuthenticated;
    public bool IsGuest => !IsAuthenticated;
    public string EmailAddress => _account.Email ?? "Not linked";
    public string AccountSummary => IsAuthenticated ? (_account.Email ?? "Signed in") : "Guest account — local features available";
    public string SyncStatus { get => _syncStatus; private set { _syncStatus = value; OnPropertyChanged(); } }

    public ICommand SignInCommand { get; }
    public ICommand SignOutCommand { get; }
    public ICommand SaveAccountCommand { get; }
    public ICommand SyncProfilesCommand { get; }
    public ICommand DownloadLocalProfilesCommand { get; }
    public ICommand PickAvatarCommand { get; }
    public ICommand PickRobotPhotoCommand { get; }

    private void RefreshCommands()
    {
        (SignInCommand as Command)?.ChangeCanExecute();
        (SignOutCommand as Command)?.ChangeCanExecute();
        (SyncProfilesCommand as Command)?.ChangeCanExecute();
        (DownloadLocalProfilesCommand as Command)?.ChangeCanExecute();
    }

    private async Task RestoreSessionAsync()
    {
        try { await _account.InitializeSessionAsync(); }
        catch { /* Local-only mode remains available if session restoration fails. */ }
        OnPropertyChanged(nameof(IsAuthenticated));
        OnPropertyChanged(nameof(IsGuest));
        OnPropertyChanged(nameof(AccountSummary));
        OnPropertyChanged(nameof(EmailAddress));
        RefreshCommands();
        if (IsAuthenticated) await _sync.SyncNowAsync();
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
            SyncStatus = "Signed in. Syncing your locally saved irrigation profiles…";
            OnPropertyChanged(nameof(IsAuthenticated));
            OnPropertyChanged(nameof(IsGuest));
            OnPropertyChanged(nameof(AccountSummary));
            OnPropertyChanged(nameof(EmailAddress));
            await _sync.SyncNowAsync();
        }
        catch (Exception ex) { SyncStatus = $"Sign-in failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private async Task SignOutAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await _account.SignOutAsync(); SyncStatus = "Signed out. Irrigation profiles remain available locally on this device."; }
        catch (Exception ex) { SyncStatus = ex.Message; }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsAuthenticated));
            OnPropertyChanged(nameof(IsGuest));
            OnPropertyChanged(nameof(AccountSummary));
            OnPropertyChanged(nameof(EmailAddress));
        }
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
        SyncStatus = "Account and robot settings saved on this device.";
    }

    private async Task PickPhotoAsync(bool robotPhoto)
    {
        try
        {
            var photo = await MediaPicker.Default.PickPhotoAsync();
            if (photo is null) return;
            var extension = Path.GetExtension(photo.FileName);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".jpg";
            var fileName = robotPhoto ? $"robot-avatar{extension}" : $"account-avatar{extension}";
            var destination = Path.Combine(FileSystem.AppDataDirectory, fileName);
            await using var source = await photo.OpenReadAsync();
            await using var target = File.Create(destination);
            await source.CopyToAsync(target);

            if (robotPhoto)
            {
                RobotAvatarPath = destination;
                Preferences.Default.Set("arigator.robot.avatar", destination);
                SyncStatus = "Robot picture saved on this device.";
            }
            else
            {
                AvatarPath = destination;
                Preferences.Default.Set("arigator.account.avatar", destination);
                SyncStatus = "Account picture saved on this device.";
            }
        }
        catch (Exception ex) { SyncStatus = $"Could not select picture: {ex.Message}"; }
    }

    private async Task SyncProfilesAsync()
    {
        if (IsBusy || !IsAuthenticated) return;
        IsBusy = true;
        try { await _sync.SyncNowAsync(); }
        finally { IsBusy = false; }
    }

    private async Task DownloadLocalProfilesAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var json = JsonSerializer.Serialize(_localProfiles.Profiles.ToList(),
                new JsonSerializerOptions { WriteIndented = true });
            var path = Path.Combine(FileSystem.CacheDirectory, "arigator-irrigation-profiles.json");
            await File.WriteAllTextAsync(path, json);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Download local irrigation profiles",
                File = new ShareFile(path, "application/json")
            });
            SyncStatus = $"Prepared {_localProfiles.Profiles.Count} local irrigation profile(s) for download or sharing.";
        }
        catch (Exception ex) { SyncStatus = $"Could not export local profiles: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private void OnSyncStatusChanged(object? sender, string status)
    {
        MainThread.BeginInvokeOnMainThread(() => SyncStatus = status);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller.Views;

public partial class SettingsPage : ContentPage
{
    private readonly FirmwareSourceUpdateService _firmwareSourceUpdateService;
    private string _firmwareCommitUrl = string.Empty;

    public SettingsPage(DashboardViewModel viewModel, FirmwareSourceUpdateService firmwareSourceUpdateService)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _firmwareSourceUpdateService = firmwareSourceUpdateService;
        Shell.SetTabBarIsVisible(this, true);
        DarkModeSwitch.IsToggled = Application.Current?.UserAppTheme == AppTheme.Dark;
    }

    private void OnDarkModeToggled(object? sender, ToggledEventArgs e)
    {
        Preferences.Default.Set("DarkModeEnabled", e.Value);
        if (Application.Current is not null)
        {
            Application.Current.UserAppTheme = e.Value ? AppTheme.Dark : AppTheme.Light;
        }
    }

    private async void OnRobotSetupClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("pairing");
    }

    private async void OnAccountClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("account");
    }
}
    private async void OnCheckFirmwareSourceClicked(object sender, EventArgs e)
    {
        CheckFirmwareSourceButton.IsEnabled = false;
        ViewFirmwareCommitButton.IsVisible = false;
        FirmwareSourceActivity.IsVisible = true;
        FirmwareSourceActivity.IsRunning = true;
        FirmwareSourceStatusLabel.Text = "Checking GitHub…";
        FirmwareSourceDetailsLabel.Text = "Looking for changes to A-RigatorBot.ino.";

        try
        {
            var result = await _firmwareSourceUpdateService.CheckForUpdatesAsync();
            _firmwareCommitUrl = result.CommitUrl;

            if (result.IsFirstCheck)
            {
                FirmwareSourceStatusLabel.Text = "Source revision recorded";
                FirmwareSourceDetailsLabel.Text =
                    $"Baseline: {result.ShortSha}. Check again after a new firmware source commit is pushed.";
            }
            else if (result.HasChanged)
            {
                FirmwareSourceStatusLabel.Text = "Firmware source has changed";
                FirmwareSourceDetailsLabel.Text =
                    $"New source commit: {result.ShortSha}\n{result.CommitMessage}" +
                    (result.CommitDate is null ? string.Empty : $"\nUpdated {result.CommitDate.Value.ToLocalTime():g}");
            }
            else
            {
                FirmwareSourceStatusLabel.Text = "No new source changes";
                FirmwareSourceDetailsLabel.Text =
                    $"Latest checked revision: {result.ShortSha}" +
                    (result.CommitDate is null ? string.Empty : $" • {result.CommitDate.Value.ToLocalTime():g}");
            }

            ViewFirmwareCommitButton.IsVisible = !string.IsNullOrWhiteSpace(_firmwareCommitUrl);
        }
        catch (Exception ex)
        {
            FirmwareSourceStatusLabel.Text = "Unable to check for updates";
            FirmwareSourceDetailsLabel.Text = ex is HttpRequestException
                ? "Could not reach GitHub. Check your internet connection and try again."
                : $"GitHub check failed: {ex.Message}";
        }
        finally
        {
            FirmwareSourceActivity.IsRunning = false;
            FirmwareSourceActivity.IsVisible = false;
            CheckFirmwareSourceButton.IsEnabled = true;
        }
    }

    private async void OnViewFirmwareCommitClicked(object sender, EventArgs e)
    {
        if (Uri.TryCreate(_firmwareCommitUrl, UriKind.Absolute, out var uri))
            await Launcher.Default.OpenAsync(uri);
    }

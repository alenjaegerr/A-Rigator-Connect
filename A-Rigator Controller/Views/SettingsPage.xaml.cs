using A_Rigator_Controller.Models;
using A_Rigator_Controller.ViewModels;
using A_Rigator_Controller.Services;

namespace A_Rigator_Controller.Views;

public partial class SettingsPage : ContentPage
{
    private readonly FirmwareSourceUpdateService _firmwareSourceUpdateService;
    private readonly IRobotCommunicationService _robotCommunicationService;
    private readonly IRobotProvisioningService _robotProvisioningService;
    private string _firmwareCommitUrl = string.Empty;

    public SettingsPage(DashboardViewModel viewModel, FirmwareSourceUpdateService firmwareSourceUpdateService, IRobotCommunicationService robotCommunicationService, IRobotProvisioningService robotProvisioningService)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _firmwareSourceUpdateService = firmwareSourceUpdateService;
        _robotCommunicationService = robotCommunicationService;
        _robotProvisioningService = robotProvisioningService;
        Appearing += OnSettingsPageAppearing;
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
        try
        {
            await Shell.Current.GoToAsync("//setup/remote-connection");
        }
        catch
        {
            await DisplayAlert("Couldn't open connection settings", "Please return to the Home screen and try again.", "OK");
        }
    }

    private async void OnFactoryResetClicked(object sender, EventArgs e)
    {
        var firstConfirmation = await DisplayAlert(
            "Factory reset A-Rigator?",
            "This will stop the robot, erase its saved Wi-Fi network and operating mode, and restart it in Local setup mode.",
            "Continue",
            "Cancel");
        if (!firstConfirmation) return;

        var finalConfirmation = await DisplayAlert(
            "This cannot be undone",
            "A-Rigator will return to its brand-new setup state. Your app account and cloud irrigation profiles will remain untouched.",
            "Reset robot",
            "Go back");
        if (!finalConfirmation) return;

        FactoryResetButton.IsEnabled = false;
        FactoryResetButton.Text = "RESETTING…";
        try
        {
            await _robotCommunicationService.SendCommandAsync(RobotControlCommand.FactoryReset);
            await Task.Delay(1200);
            await _robotCommunicationService.DisconnectAsync();
            if (Shell.Current is AppShell appShell)
                await appShell.ResumeFirstTimeSetupAfterFactoryResetAsync();

            await DisplayAlert(
                "A-Rigator reset",
                "The robot is restarting with its original Local setup. Connect to it nearby to configure Wi-Fi again.",
                "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Factory reset failed",
                $"A-Rigator could not confirm the reset. Keep the robot powered on and connected, then try again.\n\n{ex.Message}",
                "OK");
        }
        finally
        {
            FactoryResetButton.IsEnabled = true;
            FactoryResetButton.Text = "RESET A-RIGATOR";
        }
    }

    private async void OnForgetWifiClicked(object sender, EventArgs e)
    {
        var confirmed = await DisplayAlert(
            "Forget saved Wi-Fi?",
            "A-Rigator will disconnect from its saved router network. Local Bluetooth control will still work. Your robot pairing, account, and irrigation profiles will stay unchanged.",
            "Forget network",
            "Keep network");
        if (!confirmed) return;

        try
        {
            var alreadyKnown = await _robotProvisioningService.CheckAvailabilityAsync();
            if (alreadyKnown)
            {
                await _robotProvisioningService.ConnectAsync();
            }
            else
            {
                var devices = await _robotProvisioningService.ScanForRobotsAsync();
                if (devices.Count == 0)
                {
                    await DisplayAlert("A-Rigator not found", "Bring A-Rigator nearby and turn it on, then try again.", "OK");
                    return;
                }
                await _robotProvisioningService.ConnectToRobotAsync(devices[0].Id);
            }
            var result = await _robotProvisioningService.ForgetSavedWifiAsync();
            await DisplayAlert(result.Success ? "Wi-Fi forgotten" : "Couldn't forget Wi-Fi", result.Message, "OK");
        }
        catch
        {
            await DisplayAlert("Couldn't reach A-Rigator", "Keep the robot nearby, make sure Bluetooth is enabled, and try again.", "OK");
        }
    }

    private async void OnAccountClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("account");
    }


    private async void OnSettingsPageAppearing(object? sender, EventArgs e)
    {
        await RefreshInstalledFirmwareVersionAsync();
    }

    private async Task RefreshInstalledFirmwareVersionAsync()
    {
        InstalledFirmwareVersionLabel.Text = "Checking…";
        try
        {
            var status = await _robotCommunicationService.GetStatusAsync();
            InstalledFirmwareVersionLabel.Text = string.IsNullOrWhiteSpace(status?.FirmwareVersion)
                ? "Unavailable"
                : status.FirmwareVersion;
        }
        catch
        {
            InstalledFirmwareVersionLabel.Text = "Robot offline";
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
}

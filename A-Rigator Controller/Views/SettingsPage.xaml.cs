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
        Shell.SetTabBarIsVisible(this, false);
        DarkModeSwitch.IsToggled = Application.Current?.UserAppTheme == AppTheme.Dark;
    }

    private async void OnBackToProfileClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//main/profile");
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
                await _robotProvisioningService.ConnectToRobotAsync(devices[0].DeviceId);
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
        await RefreshAudioVolumeAsync();
    }

    private void OnAudioVolumeChanged(object? sender, ValueChangedEventArgs e)
    {
        AudioVolumeValueLabel.Text = $"{(int)Math.Round(e.NewValue)} / 30";
    }

    private async Task RefreshAudioVolumeAsync()
    {
        try
        {
            var status = await _robotCommunicationService.GetStatusAsync();
            if (status?.AudioVolume is int volume && volume is >= 0 and <= 30)
            {
                AudioVolumeSlider.Value = volume;
                AudioVolumeStatusLabel.Text = $"Robot volume: {volume}/30";
            }
            else AudioVolumeStatusLabel.Text = "Update the firmware to read its saved volume.";
        }
        catch { AudioVolumeStatusLabel.Text = "Connect to A-Rigator to read its saved volume."; }
    }

    private async void OnApplyAudioVolumeClicked(object sender, EventArgs e)
    {
        var volume = (int)Math.Round(AudioVolumeSlider.Value);
        ApplyAudioVolumeButton.IsEnabled = false;
        ApplyAudioVolumeButton.Text = "SAVING…";
        try
        {
            await _robotCommunicationService.SetAudioVolumeAsync(volume);
            AudioVolumeStatusLabel.Text = $"Volume saved on A-Rigator: {volume}/30";
        }
        catch (Exception ex) { AudioVolumeStatusLabel.Text = $"Couldn't save volume: {ex.Message}"; }
        finally
        {
            ApplyAudioVolumeButton.IsEnabled = true;
            ApplyAudioVolumeButton.Text = "APPLY VOLUME";
        }
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

    private async void OnLoadHardwareConfigClicked(object sender, EventArgs e)
    {
        HardwareConfigStatusLabel.Text = "Reading calibration from A-Rigator…";
        try
        {
            var status = await _robotCommunicationService.GetStatusAsync();
            if (status is null ||
                status.SteeringLeftAngle is null || status.SteeringCenterAngle is null || status.SteeringRightAngle is null ||
                status.NozzleMinimumAngle is null || status.NozzleCenterAngle is null || status.NozzleMaximumAngle is null ||
                status.NozzleLeftSectorMaximumAngle is null || status.NozzleRightSectorMinimumAngle is null)
                throw new InvalidOperationException("The connected firmware does not report hardware calibration. Update the ESP32 firmware first.");

            SteeringLeftEntry.Text = status.SteeringLeftAngle.Value.ToString();
            SteeringCenterEntry.Text = status.SteeringCenterAngle.Value.ToString();
            SteeringRightEntry.Text = status.SteeringRightAngle.Value.ToString();
            NozzleMinimumEntry.Text = status.NozzleMinimumAngle.Value.ToString();
            NozzleCenterEntry.Text = status.NozzleCenterAngle.Value.ToString();
            NozzleMaximumEntry.Text = status.NozzleMaximumAngle.Value.ToString();
            NozzleLeftSectorEntry.Text = status.NozzleLeftSectorMaximumAngle.Value.ToString();
            NozzleRightSectorEntry.Text = status.NozzleRightSectorMinimumAngle.Value.ToString();
            HardwareConfigStatusLabel.Text = "Loaded the robot's current saved calibration.";
        }
        catch (Exception ex)
        {
            HardwareConfigStatusLabel.Text = $"Couldn't load robot calibration: {ex.Message}";
        }
    }

    private async void OnSaveHardwareConfigClicked(object sender, EventArgs e)
    {
        if (!TryReadAngle(SteeringLeftEntry, out var steerLeft) ||
            !TryReadAngle(SteeringCenterEntry, out var steerCenter) ||
            !TryReadAngle(SteeringRightEntry, out var steerRight) ||
            !TryReadAngle(NozzleMinimumEntry, out var nozzleMin) ||
            !TryReadAngle(NozzleCenterEntry, out var nozzleCenter) ||
            !TryReadAngle(NozzleMaximumEntry, out var nozzleMax) ||
            !TryReadAngle(NozzleLeftSectorEntry, out var nozzleLeft) ||
            !TryReadAngle(NozzleRightSectorEntry, out var nozzleRight))
        {
            await DisplayAlert("Invalid calibration", "Enter whole-number servo angles from 0 to 180.", "OK");
            return;
        }

        var configuration = new RobotHardwareConfiguration
        {
            SteeringLeftAngle = steerLeft,
            SteeringCenterAngle = steerCenter,
            SteeringRightAngle = steerRight,
            NozzleMinimumAngle = nozzleMin,
            NozzleCenterAngle = nozzleCenter,
            NozzleMaximumAngle = nozzleMax,
            NozzleLeftSectorMaximumAngle = nozzleLeft,
            NozzleRightSectorMinimumAngle = nozzleRight
        };

        try
        {
            configuration.Validate();
            var confirmed = await DisplayAlert(
                "Apply robot calibration?",
                "The ESP32 will validate and save these values. Servo movement may be sharp; keep the wheels lifted, keep the pump off, and check linkage clearance before driving.",
                "Apply",
                "Cancel");
            if (!confirmed) return;

            SaveHardwareConfigButton.IsEnabled = false;
            SaveHardwareConfigButton.Text = "SAVING…";
            await _robotCommunicationService.ApplyHardwareConfigurationAsync(configuration);
            HardwareConfigStatusLabel.Text = "Robot acknowledged the calibration update. Reload to verify the saved values.";
        }
        catch (Exception ex)
        {
            HardwareConfigStatusLabel.Text = $"Calibration was not applied: {ex.Message}";
        }
        finally
        {
            SaveHardwareConfigButton.IsEnabled = true;
            SaveHardwareConfigButton.Text = "SAVE TO ROBOT";
        }
    }

    private static bool TryReadAngle(Entry entry, out int value) =>
        int.TryParse(entry.Text, out value) && value is >= 0 and <= 180;

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

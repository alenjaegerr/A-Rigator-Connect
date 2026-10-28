namespace A_Rigator_Controller.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        InitializeComponent();

        // Dark mode is intentionally not persisted between app launches.
        DarkModeSwitch.IsToggled = false;
        ApplyTheme(false);
    }

    private void OnDarkModeToggled(object? sender, ToggledEventArgs e)
    {
        ApplyTheme(e.Value);
    }

    private static void ApplyTheme(bool darkModeEnabled)
    {
        if (Application.Current is not null)
        {
            Application.Current.UserAppTheme = darkModeEnabled ? AppTheme.Dark : AppTheme.Light;
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

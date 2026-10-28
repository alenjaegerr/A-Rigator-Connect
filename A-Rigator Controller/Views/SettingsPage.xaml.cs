namespace A_Rigator_Controller.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        InitializeComponent();

        // Reflect the current session theme without resetting it when this page opens.
        DarkModeSwitch.IsToggled = Application.Current?.UserAppTheme == AppTheme.Dark;
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

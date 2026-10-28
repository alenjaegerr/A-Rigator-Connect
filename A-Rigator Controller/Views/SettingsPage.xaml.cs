namespace A_Rigator_Controller.Views;

public partial class SettingsPage : ContentPage
{
    private const string DarkModePreferenceKey = "DarkModeEnabled";

    public SettingsPage()
    {
        InitializeComponent();

        var darkModeEnabled = Preferences.Get(DarkModePreferenceKey, false);
        DarkModeSwitch.IsToggled = darkModeEnabled;
        ApplyTheme(darkModeEnabled);
    }

    private void OnDarkModeToggled(object? sender, ToggledEventArgs e)
    {
        Preferences.Set(DarkModePreferenceKey, e.Value);
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

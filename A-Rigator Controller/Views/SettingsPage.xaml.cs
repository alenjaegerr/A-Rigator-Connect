using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
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
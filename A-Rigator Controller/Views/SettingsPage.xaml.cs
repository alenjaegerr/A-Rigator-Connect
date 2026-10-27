namespace A_Rigator_Controller.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        InitializeComponent();
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

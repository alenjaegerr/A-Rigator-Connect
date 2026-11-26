using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller.Views;

public partial class AutonomousHubPage : ContentPage
{
    public AutonomousHubPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        // Native Shell tabs stay hidden; this page renders BottomNavigationBar in XAML.
        BindingContext = viewModel;
    }

    private async void OnPathwayModeClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("pathway");
    }

    private async void OnTerrainModeClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("terrain");
    }

    private async void OnIrrigationProfilesClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("irrigation-profiles");
    }
}
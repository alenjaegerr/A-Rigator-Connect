using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller.Views;

public partial class IrrigationProfilesPage : ContentPage
{
    public IrrigationProfilesPage(IrrigationProfilesViewModel viewModel, DashboardViewModel dashboardViewModel)
    {
        InitializeComponent();
        Shell.SetTabBarIsVisible(this, true);
        BindingContext = viewModel;
        MascotHeader.BindingContext = dashboardViewModel;
    }

    private async void OnBackToAutonomousClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}

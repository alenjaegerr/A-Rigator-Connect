using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller.Views;

public partial class AutonomousHubPage : ContentPage
{
    public AutonomousHubPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        Shell.SetTabBarIsVisible(this, true);
        BindingContext = viewModel;
    }
}
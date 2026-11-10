using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller.Views;

public partial class ControlPage : ContentPage
{
    public ControlPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        Shell.SetTabBarIsVisible(this, true);
        BindingContext = viewModel;
    }
}
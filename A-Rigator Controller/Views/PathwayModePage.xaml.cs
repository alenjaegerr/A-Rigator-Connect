using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller.Views;

public partial class PathwayModePage : ContentPage
{
    public PathwayModePage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        Shell.SetTabBarIsVisible(this, false);
    }
}

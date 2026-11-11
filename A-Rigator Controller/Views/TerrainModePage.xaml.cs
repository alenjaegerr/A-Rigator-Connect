using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller.Views;

public partial class TerrainModePage : ContentPage
{
    public TerrainModePage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        Shell.SetTabBarIsVisible(this, false);
    }
}

using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller.Views;

public partial class IrrigationProfilesPage : ContentPage
{
    public IrrigationProfilesPage(IrrigationProfilesViewModel viewModel)
    {
        InitializeComponent();
        Shell.SetTabBarIsVisible(this, true);
        BindingContext = viewModel;
    }
}

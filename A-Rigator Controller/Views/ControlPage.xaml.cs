using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller.Views;

public partial class ControlPage : ContentPage
{
    public ControlPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        // Native Shell tabs stay hidden; this page renders BottomNavigationBar in XAML.
        BindingContext = viewModel;
    }
}
using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller.Views;

public partial class TelemetryPage : ContentPage
{
    public TelemetryPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        // Native Shell tabs stay hidden; this page renders BottomNavigationBar in XAML.
        BindingContext = viewModel;
    }
}
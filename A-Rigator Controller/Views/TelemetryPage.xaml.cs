using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller.Views;

public partial class TelemetryPage : ContentPage
{
    public TelemetryPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
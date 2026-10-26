using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller.Views;

public partial class RemoteWifiSetupPage : ContentPage
{
    public RemoteWifiSetupPage(RemoteWifiSetupViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;

        viewModel.DashboardRequested += (_, _) => DashboardRequested?.Invoke(this, EventArgs.Empty);
        viewModel.NotNowRequested += (_, _) => NotNowRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? DashboardRequested;

    public event EventHandler? NotNowRequested;
}

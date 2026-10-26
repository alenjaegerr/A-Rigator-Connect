using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller.Views;

public partial class PairingPage : ContentPage
{
    public PairingPage(PairingViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        viewModel.DashboardPreviewRequested += (_, _) => DashboardPreviewRequested?.Invoke(this, EventArgs.Empty);
        viewModel.RemoteWifiSetupRequested += (_, _) => RemoteWifiSetupRequested?.Invoke(this, EventArgs.Empty);
        viewModel.NotNowRequested += (_, _) => NotNowRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? DashboardPreviewRequested;

    public event EventHandler? RemoteWifiSetupRequested;

    public event EventHandler? NotNowRequested;

    public void StartNewPairingFlow()
    {
        ((PairingViewModel)BindingContext).StartNewPairingFlow();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ((PairingViewModel)BindingContext).RefreshWifiState();
    }
}

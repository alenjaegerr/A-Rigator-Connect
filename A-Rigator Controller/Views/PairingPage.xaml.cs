using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller.Views;

public partial class PairingPage : ContentPage
{
    public PairingPage(PairingViewModel viewModel)
    {
        InitializeComponent();
        Shell.SetTabBarIsVisible(this, false);
        BindingContext = viewModel;
        viewModel.DashboardPreviewRequested += (_, _) =>
        {
            if (DashboardPreviewRequested is not null)
                DashboardPreviewRequested.Invoke(this, EventArgs.Empty);
            else if (Shell.Current is AppShell shell)
                _ = shell.ShowDashboardAsync();
        };
        viewModel.RemoteWifiSetupRequested += (_, _) =>
        {
            if (RemoteWifiSetupRequested is not null)
                RemoteWifiSetupRequested.Invoke(this, EventArgs.Empty);
            else if (Shell.Current is AppShell shell)
                _ = shell.ShowRemoteAccessInfoAsync();
        };
        viewModel.NotNowRequested += (_, _) =>
        {
            if (NotNowRequested is not null)
                NotNowRequested.Invoke(this, EventArgs.Empty);
            else if (Shell.Current is AppShell shell)
                _ = shell.ShowDashboardAsync();
        };
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

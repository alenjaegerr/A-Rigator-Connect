using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller.Views;

public partial class RemoteWifiSetupPage : ContentPage
{
    public RemoteWifiSetupPage(RemoteWifiSetupViewModel viewModel)
    {
        InitializeComponent();
        Shell.SetTabBarIsVisible(this, false);
        BindingContext = viewModel;

        viewModel.DashboardRequested += (_, _) =>
        {
            if (DashboardRequested is not null)
                DashboardRequested.Invoke(this, EventArgs.Empty);
            else if (Shell.Current is AppShell shell)
                _ = shell.ShowDashboardAsync();
        };
        viewModel.NotNowRequested += (_, _) =>
        {
            if (NotNowRequested is not null)
                NotNowRequested.Invoke(this, EventArgs.Empty);
            else if (Shell.Current is AppShell shell)
                _ = shell.ShowDashboardAsync();
        };
    }

    public event EventHandler? DashboardRequested;

    public event EventHandler? NotNowRequested;
}

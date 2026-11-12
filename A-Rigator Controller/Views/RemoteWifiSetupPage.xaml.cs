using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller.Views;

public partial class RemoteWifiSetupPage : ContentPage
{
    public RemoteWifiSetupPage(RemoteWifiSetupViewModel viewModel)
    {
        InitializeComponent();
        Shell.SetTabBarIsVisible(this, false);
        BindingContext = viewModel;

        viewModel.RemoteSetupRequested += (_, _) =>
        {
            if (RemoteSetupRequested is not null)
                RemoteSetupRequested.Invoke(this, EventArgs.Empty);
            else if (Shell.Current is AppShell shell)
                _ = shell.BeginRemoteRobotSetupAsync();
        };
        viewModel.NotNowRequested += (_, _) =>
        {
            if (NotNowRequested is not null)
                NotNowRequested.Invoke(this, EventArgs.Empty);
            else if (Shell.Current is AppShell shell)
                _ = shell.ShowDashboardAsync();
        };
    }

    public event EventHandler? RemoteSetupRequested;

    public event EventHandler? NotNowRequested;
}

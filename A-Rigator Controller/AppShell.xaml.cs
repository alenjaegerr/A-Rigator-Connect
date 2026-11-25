using A_Rigator_Controller.Models;
using A_Rigator_Controller.Services;
using A_Rigator_Controller.ViewModels;
using A_Rigator_Controller.Views;
using Microsoft.Maui.Storage;

namespace A_Rigator_Controller;

public partial class AppShell : Shell
{
    private readonly PairingPage _pairingPage;
    private readonly DashboardViewModel _dashboardViewModel;
    public AppShell(
        MainPage dashboardPage,
        ControlPage controlPage,
        AutonomousHubPage autonomousHubPage,
        PairingPage pairingPage,
        SettingsPage settingsPage,
        AccountPage accountPage,
        RemoteWifiSetupPage remoteWifiSetupPage,
        IrrigationProfileSyncCoordinator profileSyncCoordinator)
    {
        InitializeComponent();
        _pairingPage = pairingPage;
        Navigated += OnShellNavigated;
        _ = profileSyncCoordinator.StartAsync();

        // One shared dashboard view model owns the robot connection, telemetry
        // refresh and manual-control heartbeat across all operating destinations.
        _dashboardViewModel = (DashboardViewModel)dashboardPage.BindingContext;
        _dashboardViewModel.RemoteWifiSetupRequested += async (_, _) => await BeginRemoteRobotSetupAsync();
        _dashboardViewModel.RemoteModeSelectionRequested += async (_, _) => await BeginRemoteRobotSetupAsync();
        _dashboardViewModel.LocalModeSelectionRequested += async (_, _) => await BeginLocalRobotSetupAsync();
        _dashboardViewModel.ReconnectRequested += async (_, _) => await BeginReconnectFlowAsync();

        var hasCompletedSetup = Preferences.Get("arigator_setup_completed", false);

        // Main application navigation: five user-oriented destinations.
        var mainTabBar = new TabBar { Route = "main" };

        var homeTab = new Tab { Title = "Home", Route = "home" };
        homeTab.Items.Add(new ShellContent
        {
            Title = "Home",
            Route = "home-content",
            Icon = ImageSource.FromFile("nav_home.png"),
            Content = dashboardPage
        });

        var manualTab = new Tab { Title = "Manual", Route = "manual" };
        manualTab.Items.Add(new ShellContent
        {
            Title = "Manual",
            Route = "manual-content",
            Icon = ImageSource.FromFile("nav_control.png"),
            Content = controlPage
        });

        var autonomousTab = new Tab { Title = "Auto", Route = "autonomous" };
        autonomousTab.Items.Add(new ShellContent
        {
            Title = "Autonomous",
            Route = "autonomous-content",
            Icon = ImageSource.FromFile("nav_auto.png"),
            Content = autonomousHubPage
        });

        var systemTab = new Tab { Title = "System", Route = "system" };
        systemTab.Items.Add(new ShellContent
        {
            Title = "System",
            Route = "system-content",
            Icon = ImageSource.FromFile("nav_settings.png"),
            Content = settingsPage
        });

        var accountTab = new Tab { Title = "Account", Route = "account" };
        accountTab.Items.Add(new ShellContent
        {
            Title = "Account",
            Route = "account-content",
            Icon = ImageSource.FromFile("nav_account.png"),
            Content = accountPage
        });

        mainTabBar.Items.Add(homeTab);
        mainTabBar.Items.Add(manualTab);
        mainTabBar.Items.Add(autonomousTab);
        mainTabBar.Items.Add(systemTab);
        mainTabBar.Items.Add(accountTab);

        // Keep onboarding isolated from the persistent five-tab application shell.
        var setupTabBar = new TabBar { Route = "setup" };

        var pairingTab = new Tab { Title = "Pairing", Route = "pairing" };
        pairingTab.Items.Add(new ShellContent
        {
            Title = "Pairing",
            Route = "pairing-content",
            Content = pairingPage
        });

        var remoteConnectionTab = new Tab { Title = "Remote connection", Route = "remote-connection" };
        remoteConnectionTab.Items.Add(new ShellContent
        {
            Title = "Remote connection",
            Route = "remote-connection-content",
            Content = remoteWifiSetupPage
        });

        setupTabBar.Items.Add(pairingTab);
        setupTabBar.Items.Add(remoteConnectionTab);

        Items.Add(mainTabBar);
        Items.Add(setupTabBar);

        Routing.RegisterRoute("pathway", typeof(PathwayModePage));
        Routing.RegisterRoute("terrain", typeof(TerrainModePage));
        Routing.RegisterRoute("irrigation-profiles", typeof(IrrigationProfilesPage));

        pairingPage.DashboardPreviewRequested += async (_, _) =>
        {
            await ShowDashboardAsync();
        };
        pairingPage.RemoteWifiSetupRequested += async (_, _) => await ShowRemoteAccessInfoAsync();
        pairingPage.NotNowRequested += async (_, _) => await ShowDashboardAsync();
        remoteWifiSetupPage.RemoteSetupRequested += async (_, _) => await BeginRemoteRobotSetupAsync();

        // Fresh installs go straight to robot discovery. No name-entry or sign-in gate.
        // Returning installs resume the dashboard using the device's local setup marker.
        CurrentItem = hasCompletedSetup ? mainTabBar : setupTabBar;
        setupTabBar.CurrentItem = pairingTab;
        if (!hasCompletedSetup)
            Dispatcher.Dispatch(async () => await pairingPage.BeginDiscoveryFlowAsync(isRemoteSetup: false));
    }

    public Task ShowDashboardAsync()
    {
        _dashboardViewModel.StartMonitoring();
        return GoToAsync("//main/home");
    }

    public Task ShowRemoteAccessInfoAsync() => GoToAsync("//setup/remote-connection");

    public async Task ResumeFirstTimeSetupAfterFactoryResetAsync()
    {
        Preferences.Remove("arigator_setup_completed");
        _dashboardViewModel.StopMonitoring();
        _dashboardViewModel.PreferredConnectionMode = RobotConnectionMode.BLE;
        await _pairingPage.BeginDiscoveryFlowAsync(isRemoteSetup: false);
        await GoToAsync("//setup/pairing");
    }

    public async Task BeginRemoteRobotSetupAsync()
    {
        var pairingPage = _pairingPage;
        if (pairingPage is null) return;

        _dashboardViewModel.StopMonitoring();
        await pairingPage.BeginDiscoveryFlowAsync(isRemoteSetup: true);
        await GoToAsync("//setup/pairing");
    }

    private async Task BeginLocalRobotSetupAsync()
    {
        _dashboardViewModel.StopMonitoring();
        await _pairingPage.BeginDiscoveryFlowAsync(isRemoteSetup: false);
        await GoToAsync("//setup/pairing");
    }

    private async Task BeginReconnectFlowAsync()
    {
        var isRemoteSetup = _dashboardViewModel.PreferredConnectionMode == RobotConnectionMode.IoT;
        _dashboardViewModel.StopMonitoring();
        await _pairingPage.BeginDiscoveryFlowAsync(isRemoteSetup);
        await GoToAsync("//setup/pairing");
    }

    private async void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        var page = CurrentPage;
        if (page is null)
            return;

        page.Opacity = 0;
        await page.FadeTo(1, 220, Easing.CubicInOut);
    }
}

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
    private readonly IRobotCommunicationService _robotCommunicationService;
    public AppShell(
        MainPage dashboardPage,
        ControlPage controlPage,
        AutonomousHubPage autonomousHubPage,
        PairingPage pairingPage,
        TelemetryPage telemetryPage,
        AccountPage accountPage,
        RemoteWifiSetupPage remoteWifiSetupPage,
        IrrigationProfileSyncCoordinator profileSyncCoordinator,
        IRobotCommunicationService robotCommunicationService)
    {
        InitializeComponent();
        _pairingPage = pairingPage;
        _robotCommunicationService = robotCommunicationService;
        Navigated += OnShellNavigated;
        Dispatcher.Dispatch(async () => await StartBackgroundServicesAsync(profileSyncCoordinator));

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

        var homeTab = new Tab { Title = "Home", Route = "home", };
        homeTab.Items.Add(new ShellContent
        {
            Title = "Home",
            Route = "home-content",
            
            Content = dashboardPage
        });

        var manualTab = new Tab { Title = "Control", Route = "manual", };
        manualTab.Items.Add(new ShellContent
        {
            Title = "Control",
            Route = "manual-content",
            
            Content = controlPage
        });

        var autonomousTab = new Tab { Title = "Automate", Route = "autonomous", };
        autonomousTab.Items.Add(new ShellContent
        {
            Title = "Automate",
            Route = "autonomous-content",
            
            Content = autonomousHubPage
        });

        var activityTab = new Tab { Title = "Activity", Route = "activity", };
        activityTab.Items.Add(new ShellContent
        {
            Title = "Activity",
            Route = "activity-content",
            
            Content = telemetryPage
        });

        var accountTab = new Tab { Title = "Profile", Route = "profile", };
        var profileShellContent = new ShellContent
        {
            Title = "Profile",
            Route = "profile-content",
            
            Content = accountPage
        };
        accountTab.Items.Add(profileShellContent);

        mainTabBar.Items.Add(homeTab);
        mainTabBar.Items.Add(manualTab);
        mainTabBar.Items.Add(autonomousTab);
        mainTabBar.Items.Add(activityTab);
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
        Routing.RegisterRoute("settings", typeof(SettingsPage));

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
            Dispatcher.Dispatch(async () => await BeginFirstTimeDiscoveryAsync(pairingPage));
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
        _robotCommunicationService.PreferredConnectionMode = RobotConnectionMode.BLE;
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
        try
        {
            var page = CurrentPage;
            if (page is null)
                return;

            page.Opacity = 0;
            await page.FadeTo(1, 220, Easing.CubicInOut);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppShell] Navigation transition failed: {ex}");
        }
    }

    private static async Task StartBackgroundServicesAsync(
        IrrigationProfileSyncCoordinator profileSyncCoordinator)
    {
        try
        {
            await profileSyncCoordinator.StartAsync();
        }
        catch (Exception ex)
        {
            // Cloud profile synchronization is optional. It must never block
            // navigation or local BLE operation when startup/network services fail.
            System.Diagnostics.Debug.WriteLine($"[AppShell] Profile sync startup failed: {ex}");
        }
    }

    private static async Task BeginFirstTimeDiscoveryAsync(PairingPage pairingPage)
    {
        try
        {
            await pairingPage.BeginDiscoveryFlowAsync(isRemoteSetup: false);
        }
        catch (Exception ex)
        {
            // Keep onboarding visible and record unexpected discovery failures.
            System.Diagnostics.Debug.WriteLine($"[AppShell] First-run BLE discovery failed: {ex}");
        }
    }
}

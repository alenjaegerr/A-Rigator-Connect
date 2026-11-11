using A_Rigator_Controller.Services;
using A_Rigator_Controller.ViewModels;
using A_Rigator_Controller.Views;

namespace A_Rigator_Controller;

public partial class AppShell : Shell
{
    public AppShell(
        MainPage dashboardPage,
        ControlPage controlPage,
        AutonomousHubPage autonomousHubPage,
        IrrigationProfilesPage irrigationProfilesPage,
        WelcomePage welcomePage,
        PairingPage pairingPage,
        SettingsPage settingsPage,
        PathwayModePage pathwayModePage,
        IUserProfileService userProfileService)
    {
        InitializeComponent();
        Navigated += OnShellNavigated;

        // One shared dashboard view model owns the robot connection, telemetry
        // refresh and manual-control heartbeat across all operating destinations.
        ((DashboardViewModel)dashboardPage.BindingContext).StartMonitoring();

        var hasDisplayName = !string.IsNullOrWhiteSpace(userProfileService.GetDisplayName());
        if (hasDisplayName)
            pairingPage.StartNewPairingFlow();

        // Main application navigation: five user-oriented destinations.
        var mainTabBar = new TabBar { Route = "main" };

        var homeTab = new Tab { Title = "Home", Route = "home" };
        homeTab.Items.Add(new ShellContent
        {
            Title = "Home",
            Route = "home-content",
            Icon = ImageSource.FromFile("nav_home.svg"),
            Content = dashboardPage
        });

        var manualTab = new Tab { Title = "Manual", Route = "manual" };
        manualTab.Items.Add(new ShellContent
        {
            Title = "Manual",
            Route = "manual-content",
            Icon = ImageSource.FromFile("nav_control.svg"),
            Content = controlPage
        });

        var autonomousTab = new Tab { Title = "Auto", Route = "autonomous" };
        autonomousTab.Items.Add(new ShellContent
        {
            Title = "Autonomous",
            Route = "autonomous-content",
            Icon = ImageSource.FromFile("nav_auto.svg"),
            Content = autonomousHubPage
        });

        var profilesTab = new Tab { Title = "Profiles", Route = "profiles" };
        profilesTab.Items.Add(new ShellContent
        {
            Title = "Profiles",
            Route = "profiles-content",
            Icon = ImageSource.FromFile("nav_profiles.svg"),
            Content = irrigationProfilesPage
        });

        var systemTab = new Tab { Title = "System", Route = "system" };
        systemTab.Items.Add(new ShellContent
        {
            Title = "System",
            Route = "system-content",
            Icon = ImageSource.FromFile("nav_settings.svg"),
            Content = settingsPage
        });

        mainTabBar.Items.Add(homeTab);
        mainTabBar.Items.Add(manualTab);
        mainTabBar.Items.Add(autonomousTab);
        mainTabBar.Items.Add(profilesTab);
        mainTabBar.Items.Add(systemTab);

        // Keep onboarding isolated from the persistent five-tab application shell.
        var setupTabBar = new TabBar { Route = "setup" };

        var welcomeTab = new Tab { Title = "Welcome", Route = "welcome" };
        welcomeTab.Items.Add(new ShellContent
        {
            Title = "Welcome",
            Route = "welcome-content",
            Content = welcomePage
        });

        var pairingTab = new Tab { Title = "Pairing", Route = "pairing" };
        pairingTab.Items.Add(new ShellContent
        {
            Title = "Pairing",
            Route = "pairing-content",
            Content = pairingPage
        });

        setupTabBar.Items.Add(welcomeTab);
        setupTabBar.Items.Add(pairingTab);

        Items.Add(mainTabBar);
        Items.Add(setupTabBar);

        Routing.RegisterRoute("remote-wifi", typeof(RemoteWifiSetupPage));
        Routing.RegisterRoute("pathway", typeof(PathwayModePage));
        Routing.RegisterRoute("account", typeof(AccountPage));

        welcomePage.DisplayNameSaved += async (_, _) =>
        {
            pairingPage.StartNewPairingFlow();
            await GoToAsync("//setup/pairing");
        };

        pairingPage.DashboardPreviewRequested += async (_, _) => await ShowDashboardAsync();
        pairingPage.RemoteWifiSetupRequested += async (_, _) => await ShowRemoteAccessInfoAsync();
        pairingPage.NotNowRequested += async (_, _) => await ShowDashboardAsync();

        CurrentItem = setupTabBar;
        setupTabBar.CurrentItem = hasDisplayName ? pairingTab : welcomeTab;
    }

    public Task ShowDashboardAsync() => GoToAsync("//main/home");

    public Task ShowRemoteAccessInfoAsync() => GoToAsync("remote-wifi");

    private async void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        var page = CurrentPage;
        if (page is null)
            return;

        page.Opacity = 0;
        await page.FadeTo(1, 220, Easing.CubicInOut);
    }
}

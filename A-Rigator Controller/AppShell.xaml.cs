using A_Rigator_Controller.Services;
using A_Rigator_Controller.ViewModels;
using A_Rigator_Controller.Views;

namespace A_Rigator_Controller;

public partial class AppShell : Shell
{
    private readonly ShellContent _homeContent;

    public AppShell(
        MainPage dashboardPage,
        ControlPage controlPage,
        TelemetryPage telemetryPage,
        WelcomePage welcomePage,
        PairingPage pairingPage,
        RemoteWifiSetupPage remoteWifiSetupPage,
        AccountPage accountPage,
        SettingsPage settingsPage,
        IUserProfileService userProfileService)
    {
        InitializeComponent();

        // Keep one shared telemetry/heartbeat loop alive while users move between tabs.
        ((DashboardViewModel)dashboardPage.BindingContext).StartMonitoring();

        var hasDisplayName = !string.IsNullOrWhiteSpace(userProfileService.GetDisplayName());
        if (hasDisplayName)
            pairingPage.StartNewPairingFlow();

        var tabBar = new TabBar { Route = "main" };

        var homeTab = new Tab { Title = "Home", Route = "home" };
        _homeContent = new ShellContent
        {
            Title = "Home",
            Route = "home-content",
            Icon = "nav_home.svg",
            Content = !hasDisplayName ? welcomePage : pairingPage
        };
        homeTab.Items.Add(_homeContent);

        var controlTab = new Tab { Title = "Control", Route = "control" };
        controlTab.Items.Add(new ShellContent
        {
            Title = "Control",
            Route = "control-content",
            Icon = "nav_control.svg",
            Content = controlPage
        });

        var telemetryTab = new Tab { Title = "Telemetry", Route = "telemetry" };
        telemetryTab.Items.Add(new ShellContent
        {
            Title = "Telemetry",
            Route = "telemetry-content",
            Icon = "nav_telemetry.svg",
            Content = telemetryPage
        });

        var settingsTab = new Tab { Title = "Settings", Route = "settings-tab" };
        settingsTab.Items.Add(new ShellContent
        {
            Title = "Settings",
            Route = "settings-content",
            Icon = "nav_settings.svg",
            Content = settingsPage
        });

        tabBar.Items.Add(homeTab);
        tabBar.Items.Add(controlTab);
        tabBar.Items.Add(telemetryTab);
        tabBar.Items.Add(settingsTab);
        Items.Add(tabBar);

        Routing.RegisterRoute("welcome", typeof(WelcomePage));
        Routing.RegisterRoute("pairing", typeof(PairingPage));
        Routing.RegisterRoute("remote-wifi", typeof(RemoteWifiSetupPage));
        Routing.RegisterRoute("account", typeof(AccountPage));

        welcomePage.DisplayNameSaved += async (_, _) =>
        {
            pairingPage.StartNewPairingFlow();
            ReplaceHomeContent(pairingPage);
            await GoToAsync("//main/home");
        };

        pairingPage.DashboardPreviewRequested += async (_, _) =>
        {
            ReplaceHomeContent(dashboardPage);
            await GoToAsync("//main/home");
        };

        pairingPage.RemoteWifiSetupRequested += async (_, _) =>
            await GoToAsync("remote-wifi");

        pairingPage.NotNowRequested += async (_, _) =>
        {
            ReplaceHomeContent(dashboardPage);
            await GoToAsync("//main/home");
        };

        remoteWifiSetupPage.DashboardRequested += async (_, _) =>
        {
            ReplaceHomeContent(dashboardPage);
            await GoToAsync("//main/home");
        };

        remoteWifiSetupPage.NotNowRequested += async (_, _) =>
        {
            ReplaceHomeContent(dashboardPage);
            await GoToAsync("//main/home");
        };

        CurrentItem = tabBar;
    }

    private void ReplaceHomeContent(Page page)
    {
        _homeContent.Content = page;
    }
}
using A_Rigator_Controller.Services;
using A_Rigator_Controller.ViewModels;
using A_Rigator_Controller.Views;

namespace A_Rigator_Controller;

public partial class AppShell : Shell
{
    private readonly ShellContent _homeContent;
    private readonly MainPage _dashboardPage;

    public AppShell(
        MainPage dashboardPage,
        ControlPage controlPage,
        TelemetryPage telemetryPage,
        WelcomePage welcomePage,
        PairingPage pairingPage,
        RemoteWifiSetupPage remoteWifiSetupPage,
        SettingsPage settingsPage,
        IUserProfileService userProfileService)
    {
        InitializeComponent();
        _dashboardPage = dashboardPage;

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
            Icon = ImageSource.FromFile("nav_home.svg"),
            Content = !hasDisplayName ? welcomePage : pairingPage
        };
        homeTab.Items.Add(_homeContent);

        var controlTab = new Tab { Title = "Control", Route = "control" };
        controlTab.Items.Add(new ShellContent
        {
            Title = "Control",
            Route = "control-content",
            Icon = ImageSource.FromFile("nav_control.svg"),
            Content = controlPage
        });

        var telemetryTab = new Tab { Title = "Telemetry", Route = "telemetry" };
        telemetryTab.Items.Add(new ShellContent
        {
            Title = "Telemetry",
            Route = "telemetry-content",
            Icon = ImageSource.FromFile("nav_telemetry.svg"),
            Content = telemetryPage
        });

        var settingsTab = new Tab { Title = "Settings", Route = "settings-tab" };
        settingsTab.Items.Add(new ShellContent
        {
            Title = "Settings",
            Route = "settings-content",
            Icon = ImageSource.FromFile("nav_settings.svg"),
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

        pairingPage.DashboardPreviewRequested += async (_, _) => await ShowDashboardAsync();
        pairingPage.RemoteWifiSetupRequested += async (_, _) => await ShowRemoteAccessInfoAsync();
        pairingPage.NotNowRequested += async (_, _) => await ShowDashboardAsync();

        remoteWifiSetupPage.DashboardRequested += async (_, _) => await ShowDashboardAsync();
        remoteWifiSetupPage.NotNowRequested += async (_, _) => await ShowDashboardAsync();

        CurrentItem = tabBar;
    }

    public async Task ShowDashboardAsync()
    {
        _dashboardPage.Opacity = 0;
        ReplaceHomeContent(_dashboardPage);
        await GoToAsync("//main/home");
        await _dashboardPage.FadeTo(1, 350, Easing.CubicInOut);
    }

    public Task ShowRemoteAccessInfoAsync() => GoToAsync("remote-wifi");

    private void ReplaceHomeContent(Page page)
    {
        _homeContent.Content = page;
    }
}
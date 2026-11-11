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
        SettingsPage settingsPage,
        IUserProfileService userProfileService)
    {
        InitializeComponent();
        Navigated += OnShellNavigated;
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
            await NavigateToHomeContentAsync();
        };

        pairingPage.DashboardPreviewRequested += async (_, _) => await ShowDashboardAsync();
        pairingPage.RemoteWifiSetupRequested += async (_, _) => await ShowRemoteAccessInfoAsync();
        pairingPage.NotNowRequested += async (_, _) => await ShowDashboardAsync();

        CurrentItem = tabBar;
    }

    public async Task ShowDashboardAsync()
    {
        ReplaceHomeContent(_dashboardPage);
        await NavigateToHomeContentAsync();
    }

    public Task ShowRemoteAccessInfoAsync() => GoToAsync("remote-wifi");

    private Task NavigateToHomeContentAsync()
    {
        // Include the ShellContent route, not only the Tab route. This makes the
        // destination explicit after the Home tab's page instance is replaced.
        return GoToAsync("//main/home/home-content");
    }

    private async void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        var page = CurrentPage;
        if (page is null)
            return;

        page.Opacity = 0;
        await page.FadeTo(1, 220, Easing.CubicInOut);
    }

    private void ReplaceHomeContent(Page page)
    {
        _homeContent.Content = page;
    }
}

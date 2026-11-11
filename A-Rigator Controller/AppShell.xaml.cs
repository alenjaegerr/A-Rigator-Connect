using A_Rigator_Controller.Services;
using A_Rigator_Controller.ViewModels;
using A_Rigator_Controller.Views;

namespace A_Rigator_Controller;

public partial class AppShell : Shell
{
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

        // Keep one shared telemetry/heartbeat loop alive while users move between tabs.
        ((DashboardViewModel)dashboardPage.BindingContext).StartMonitoring();

        var hasDisplayName = !string.IsNullOrWhiteSpace(userProfileService.GetDisplayName());
        if (hasDisplayName)
            pairingPage.StartNewPairingFlow();

        // The main tab bar always owns the dashboard. Setup pages live in a
        // separate Shell item so replacing a setup page can never leave stale
        // content cached inside the active Home tab.
        var mainTabBar = new TabBar { Route = "main" };

        var homeTab = new Tab { Title = "Home", Route = "home" };
        homeTab.Items.Add(new ShellContent
        {
            Title = "Home",
            Route = "home-content",
            Icon = ImageSource.FromFile("nav_home.svg"),
            Content = dashboardPage
        });

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

        mainTabBar.Items.Add(homeTab);
        mainTabBar.Items.Add(controlTab);
        mainTabBar.Items.Add(telemetryTab);
        mainTabBar.Items.Add(settingsTab);

        // Keep onboarding outside the main tab bar. PairingPage and WelcomePage
        // already hide the tab bar, while their own Shell item stays independent
        // from the persistent dashboard content.
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
        Routing.RegisterRoute("account", typeof(AccountPage));

        welcomePage.DisplayNameSaved += async (_, _) =>
        {
            pairingPage.StartNewPairingFlow();
            await GoToAsync("//setup/pairing");
        };

        pairingPage.DashboardPreviewRequested += async (_, _) => await ShowDashboardAsync();
        pairingPage.RemoteWifiSetupRequested += async (_, _) => await ShowRemoteAccessInfoAsync();
        pairingPage.NotNowRequested += async (_, _) => await ShowDashboardAsync();

        // Start in onboarding without ever assigning it to the Home tab.
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

using A_Rigator_Controller.Services;
using A_Rigator_Controller.Views;

namespace A_Rigator_Controller
{
    public partial class AppShell : Shell
    {
        private ShellContent _homeContent = null!;

        public AppShell(
            MainPage dashboardPage,
            WelcomePage welcomePage,
            PairingPage pairingPage,
            RemoteWifiSetupPage remoteWifiSetupPage,
            AccountPage accountPage,
            SettingsPage settingsPage,
            IUserProfileService userProfileService,
            IAccountService accountService)
        {
            InitializeComponent();

            var hasDisplayName = !string.IsNullOrWhiteSpace(userProfileService.GetDisplayName());
            var remoteModeAvailable = accountService.IsRemoteModeAllowed;
            var remoteWifiTitle = remoteModeAvailable
                ? "Remote Wi-Fi"
                : "Remote Wi-Fi (Google sign-in required)";

            if (hasDisplayName)
                pairingPage.StartNewPairingFlow();

            var homeItem = new FlyoutItem { Title = "Home" };
            _homeContent = new ShellContent
            {
                Title = "",
                Route = "Home",
                Content = !hasDisplayName ? welcomePage : pairingPage
            };
            homeItem.Items.Add(_homeContent);
            Items.Add(homeItem);

            var dashboardItem = new FlyoutItem { Title = "Local Dashboard" };
            dashboardItem.Items.Add(new ShellContent
            {
                Title = "",
                Route = "dashboard",
                Content = dashboardPage
            });
            Items.Add(dashboardItem);

            var setupItem = new FlyoutItem { Title = "Robot Setup" };
            setupItem.Items.Add(new ShellContent
            {
                Title = "",
                Route = "pairing",
                Content = pairingPage
            });
            Items.Add(setupItem);

            var remoteWifiItem = new FlyoutItem
            {
                Title = remoteWifiTitle,
                IsEnabled = remoteModeAvailable
            };
            remoteWifiItem.Items.Add(new ShellContent
            {
                Title = "",
                Route = "remote-wifi",
                Content = remoteWifiSetupPage,
                IsEnabled = remoteModeAvailable
            });
            Items.Add(remoteWifiItem);

            var accountItem = new FlyoutItem { Title = "Account" };
            accountItem.Items.Add(new ShellContent
            {
                Title = "",
                Route = "account",
                Content = accountPage
            });
            Items.Add(accountItem);

            var settingsItem = new FlyoutItem { Title = "Settings" };
            settingsItem.Items.Add(new ShellContent
            {
                Title = "",
                Route = "settings",
                Content = settingsPage
            });
            Items.Add(settingsItem);

            welcomePage.DisplayNameSaved += (_, _) =>
            {
                pairingPage.StartNewPairingFlow();
                ReplaceHomeContent(pairingPage);
            };
            pairingPage.DashboardPreviewRequested += (_, _) => ShowDashboard(dashboardPage);
            pairingPage.RemoteWifiSetupRequested += (_, _) => ReplaceHomeContent(remoteWifiSetupPage);
            pairingPage.NotNowRequested += (_, _) => ShowDashboard(dashboardPage);
            remoteWifiSetupPage.DashboardRequested += (_, _) => ShowDashboard(dashboardPage);
            remoteWifiSetupPage.NotNowRequested += (_, _) => ShowDashboard(dashboardPage);

            CurrentItem = _homeContent;
        }

        private void ShowDashboard(MainPage dashboardPage)
        {
            dashboardPage.Opacity = 0;
            ReplaceHomeContent(dashboardPage);
            _ = dashboardPage.FadeTo(1, 550, Easing.CubicInOut);
        }

        private void ReplaceHomeContent(Page page)
        {
            var content = new ShellContent
            {
                Title = "",
                Route = "Home",
                Content = page
            };

            var index = Items.IndexOf(_homeContent);
            Items.Remove(_homeContent);
            Items.Insert(index, content);
            _homeContent = content;
            CurrentItem = content;
        }
    }
}
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
            IUserProfileService userProfileService,
            IAccountService accountService)
        {
            InitializeComponent();

            var hasDisplayName = !string.IsNullOrWhiteSpace(userProfileService.GetDisplayName());
            var remoteModeAvailable = accountService.IsRemoteModeAllowed;
            var remoteWifiTitle = remoteModeAvailable ? "Remote Wi-Fi" : "Remote Wi‑Fi (Google sign-in required)";

            if (hasDisplayName)
            {
                pairingPage.StartNewPairingFlow();
            }

            var homeItem = new FlyoutItem
            {
                Title = "Home"
            };
            _homeContent = new ShellContent
            {
                Title = "A-Rigator Connect",
                Route = "Home",
                Content = !hasDisplayName ? welcomePage : pairingPage
            };
            homeItem.Items.Add(_homeContent);
            Items.Add(homeItem);

            var dashboardItem = new FlyoutItem
            {
                Title = "Local Dashboard"
            };
            dashboardItem.Items.Add(new ShellContent
            {
                Title = "Local Dashboard",
                Route = "dashboard",
                Content = dashboardPage
            });
            Items.Add(dashboardItem);

            var setupItem = new FlyoutItem
            {
                Title = "Robot Setup"
            };
            setupItem.Items.Add(new ShellContent
            {
                Title = "Robot Setup",
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
                Title = remoteWifiTitle,
                Route = "remote-wifi",
                Content = remoteWifiSetupPage,
                IsEnabled = remoteModeAvailable
            });
            Items.Add(remoteWifiItem);

            var accountItem = new FlyoutItem
            {
                Title = "Account"
            };
            accountItem.Items.Add(new ShellContent
            {
                Title = "Account",
                Route = "account",
                Content = accountPage
            });
            Items.Add(accountItem);

            welcomePage.DisplayNameSaved += (_, _) =>
            {
                pairingPage.StartNewPairingFlow();
                ShowPairing(pairingPage);
            };
            pairingPage.DashboardPreviewRequested += (_, _) => ShowDashboard(dashboardPage);
            pairingPage.RemoteWifiSetupRequested += (_, _) => ShowRemoteWifiSetup(remoteWifiSetupPage);
            pairingPage.NotNowRequested += (_, _) => ShowDashboard(dashboardPage);
            remoteWifiSetupPage.DashboardRequested += (_, _) => ShowDashboard(dashboardPage);
            remoteWifiSetupPage.NotNowRequested += (_, _) => ShowDashboard(dashboardPage);

            CurrentItem = _homeContent;
        }

        private void ShowPairing(PairingPage pairingPage)
        {
            var pairingContent = new ShellContent
            {
                Title = "A-Rigator Connect",
                Route = "Home",
                Content = pairingPage
            };

            var homeContentIndex = Items.IndexOf(_homeContent);
            Items.Remove(_homeContent);
            Items.Insert(homeContentIndex, pairingContent);
            _homeContent = pairingContent;
            CurrentItem = pairingContent;
        }

        private void ShowRemoteWifiSetup(RemoteWifiSetupPage remoteWifiSetupPage)
        {
            var remoteWifiContent = new ShellContent
            {
                Title = "Remote Wi-Fi",
                Route = "remote-wifi",
                Content = remoteWifiSetupPage
            };

            var homeContentIndex = Items.IndexOf(_homeContent);
            Items.Remove(_homeContent);
            Items.Insert(homeContentIndex, remoteWifiContent);
            _homeContent = remoteWifiContent;
            CurrentItem = remoteWifiContent;
        }

        private void ShowDashboard(MainPage dashboardPage)
        {
            var dashboardContent = new ShellContent
            {
                Title = "A-Rigator Connect",
                Route = "Home",
                Content = dashboardPage
            };

            var homeContentIndex = Items.IndexOf(_homeContent);
            Items.Remove(_homeContent);
            Items.Insert(homeContentIndex, dashboardContent);
            _homeContent = dashboardContent;
            CurrentItem = dashboardContent;
        }
    }
}

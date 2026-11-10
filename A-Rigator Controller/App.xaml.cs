using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller
{
    public partial class App : Application
    {
        private readonly AppShell _appShell;
        private readonly DashboardViewModel _dashboardViewModel;

        public App(AppShell appShell, DashboardViewModel dashboardViewModel)
        {
            InitializeComponent();
            var darkModeEnabled = Preferences.Default.Get("DarkModeEnabled", false);
            UserAppTheme = darkModeEnabled ? AppTheme.Dark : AppTheme.Light;
            _appShell = appShell;
            _dashboardViewModel = dashboardViewModel;
        }

        protected override void OnSleep()
        {
            // Stop manual outputs when the app is backgrounded; autonomous mode remains robot-controlled.
            _dashboardViewModel.StopMonitoring();
            base.OnSleep();
        }

        protected override void OnResume()
        {
            _dashboardViewModel.StartMonitoring();
            base.OnResume();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window();
            _appShell.Opacity = 0;

            var loadingPage = new LoadingPage(() =>
            {
                window.Page = _appShell;
                _ = _appShell.FadeTo(1, 550, Easing.CubicInOut);
            });

            window.Page = loadingPage;
            return window;
        }
    }
}
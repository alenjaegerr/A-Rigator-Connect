namespace A_Rigator_Controller
{
    public partial class App : Application
    {
        private readonly AppShell _appShell;

        public App(AppShell appShell)
        {
            InitializeComponent();

            // Restore the user's saved theme before the loading screen appears.
            bool darkModeEnabled =
                Preferences.Default.Get("DarkModeEnabled", false);

            UserAppTheme = darkModeEnabled
                ? AppTheme.Dark
                : AppTheme.Light;

            _appShell = appShell;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window();
            _appShell.Opacity = 0;

            var loadingPage = new LoadingPage(() =>
            {
                window.Page = _appShell;
                _ = _appShell.FadeTo(1, 320, Easing.CubicInOut);
            });

            window.Page = loadingPage;
            return window;
        }
    }
}

namespace A_Rigator_Controller
{
    public partial class App : Application
    {
        private readonly AppShell _appShell;

        public App(AppShell appShell)
        {
            InitializeComponent();

            // Apply the saved appearance before any screen is displayed.
            var darkModeEnabled = Preferences.Get("DarkModeEnabled", false);
            UserAppTheme = darkModeEnabled ? AppTheme.Dark : AppTheme.Light;

            _appShell = appShell;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(_appShell);
        }
    }
}
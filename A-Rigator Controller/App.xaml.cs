namespace A_Rigator_Controller
{
    public partial class App : Application
    {
        private readonly AppShell _appShell;

        public App(AppShell appShell)
        {
            InitializeComponent();

            // Start every fresh app session in light mode. Dark mode is a session-only choice.
            UserAppTheme = AppTheme.Light;

            _appShell = appShell;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(_appShell);
        }
    }
}
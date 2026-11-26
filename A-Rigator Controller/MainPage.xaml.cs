using System.ComponentModel;
using A_Rigator_Controller.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace A_Rigator_Controller
{
    public partial class MainPage : ContentPage
    {
        private DashboardViewModel ViewModel => (DashboardViewModel)BindingContext;

        public MainPage(DashboardViewModel viewModel)
        {
            InitializeComponent();
            // The page uses the shared branded BottomNavigationBar in XAML.
            // Keep the native Shell tab bar hidden to prevent duplicate navigation.
            BindingContext = viewModel;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            ViewModel.PropertyChanged += OnDashboardPropertyChanged;
            ViewModel.StartMonitoring();
            UpdateConnectionPulse();
        }

        protected override void OnDisappearing()
        {
            ViewModel.PropertyChanged -= OnDashboardPropertyChanged;
            this.AbortAnimation("DashboardConnectionPulse");
            ConnectionPulseDot.Opacity = 0.35;
            base.OnDisappearing();
        }

        private void OnDashboardPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(DashboardViewModel.ConnectionStatus) or
                nameof(DashboardViewModel.IsConnected) or null)
            {
                MainThread.BeginInvokeOnMainThread(UpdateConnectionPulse);
            }
        }

        private void UpdateConnectionPulse()
        {
            this.AbortAnimation("DashboardConnectionPulse");
            if (!ViewModel.IsConnected)
            {
                ConnectionPulseDot.Opacity = 0.35;
                return;
            }

            var pulse = new Animation(value => ConnectionPulseDot.Opacity = value, 1.0, 0.25);
            pulse.Commit(this, "DashboardConnectionPulse", 16, 1100,
                Easing.SinInOut, finished: null, repeat: () => ViewModel.IsConnected);
        }
    }
}
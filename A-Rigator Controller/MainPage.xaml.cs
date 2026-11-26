using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller
{
    public partial class MainPage : ContentPage
    {
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
            ((DashboardViewModel)BindingContext).StartMonitoring();
        }
    }
}
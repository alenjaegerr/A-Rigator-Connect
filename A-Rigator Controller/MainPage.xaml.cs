using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller
{
    public partial class MainPage : ContentPage
    {
        public MainPage(DashboardViewModel viewModel)
        {
            InitializeComponent();
            Shell.SetTabBarIsVisible(this, true);
            BindingContext = viewModel;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            ((DashboardViewModel)BindingContext).StartMonitoring();
        }
    }
}
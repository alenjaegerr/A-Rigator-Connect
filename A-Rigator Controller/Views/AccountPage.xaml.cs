using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller.Views;

public partial class AccountPage : ContentPage
{
    public AccountPage(AccountViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

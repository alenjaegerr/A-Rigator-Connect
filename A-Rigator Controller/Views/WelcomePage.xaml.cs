using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller.Views;

public partial class WelcomePage : ContentPage
{
    public WelcomePage(WelcomeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        viewModel.DisplayNameSaved += (_, _) => DisplayNameSaved?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? DisplayNameSaved;
}

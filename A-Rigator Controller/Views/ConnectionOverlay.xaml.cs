using System.ComponentModel;
using A_Rigator_Controller.ViewModels;

namespace A_Rigator_Controller.Views;

public partial class ConnectionOverlay : ContentView
{
    private DashboardViewModel? _viewModel;
    private int _animationVersion;

    public ConnectionOverlay()
    {
        InitializeComponent();
    }

    protected override void OnBindingContextChanged()
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        base.OnBindingContextChanged();

        _viewModel = BindingContext as DashboardViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            MainThread.BeginInvokeOnMainThread(() => _ = SetOverlayVisibleAsync(_viewModel.IsConnecting));
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DashboardViewModel.IsConnecting) || _viewModel is null)
            return;

        var shouldShow = _viewModel.IsConnecting;
        MainThread.BeginInvokeOnMainThread(() => _ = SetOverlayVisibleAsync(shouldShow));
    }

    private async Task SetOverlayVisibleAsync(bool visible)
    {
        var version = Interlocked.Increment(ref _animationVersion);
        if (visible)
        {
            IsVisible = true;
            InputTransparent = false;
            await this.FadeTo(1, 180, Easing.CubicInOut);
            return;
        }

        await this.FadeTo(0, 160, Easing.CubicInOut);
        if (version == _animationVersion)
        {
            IsVisible = false;
            InputTransparent = true;
        }
    }
}

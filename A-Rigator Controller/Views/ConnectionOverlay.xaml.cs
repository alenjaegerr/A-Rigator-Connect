namespace A_Rigator_Controller.Views;

public partial class ConnectionOverlay : ContentView
{
    public static readonly BindableProperty IsBusyProperty = BindableProperty.Create(
        nameof(IsBusy),
        typeof(bool),
        typeof(ConnectionOverlay),
        false,
        propertyChanged: OnIsBusyChanged);

    private int _animationVersion;

    public ConnectionOverlay()
    {
        InitializeComponent();
    }

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    private static void OnIsBusyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var overlay = (ConnectionOverlay)bindable;
        var visible = newValue is true;
        MainThread.BeginInvokeOnMainThread(() => _ = overlay.SetOverlayVisibleAsync(visible));
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

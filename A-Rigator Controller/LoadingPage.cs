using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace A_Rigator_Controller;

public sealed class LoadingPage : ContentPage
{
    private readonly Action _onComplete;
    private readonly Label _statusText;
    private bool _started;
    private bool _isLeaving;

    public LoadingPage(Action onComplete)
    {
        _onComplete = onComplete;
        BackgroundColor = Colors.White;
        Shell.SetNavBarIsVisible(this, false);

        var brandLogo = new Image
        {
            Source = ImageSource.FromFile("applogo.svg"),
            Aspect = Aspect.AspectFit,
            WidthRequest = 292,
            HeightRequest = 278,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        var intro = new Label
        {
            Text = "Getting things ready…",
            FontSize = 17,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#183B27"),
            HorizontalTextAlignment = TextAlignment.Center
        };
        _statusText = new Label
        {
            Text = "Preparing your robot companion",
            FontSize = 13,
            TextColor = Color.FromArgb("#5D7063"),
            HorizontalTextAlignment = TextAlignment.Center
        };
        var activity = new ActivityIndicator
        {
            IsRunning = true,
            Color = Color.FromArgb("#29A653"),
            HorizontalOptions = LayoutOptions.Center,
            WidthRequest = 28,
            HeightRequest = 28
        };
        var footer = new Label
        {
            Text = "LOCAL-FIRST • BLUETOOTH • WI-FI",
            FontSize = 10,
            CharacterSpacing = 1.2,
            TextColor = Color.FromArgb("#7A8A7E"),
            HorizontalTextAlignment = TextAlignment.Center
        };
        var content = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(28, 32),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            Children =
            {
                brandLogo,
                new BoxView { HeightRequest = 12, Opacity = 0 },
                intro, _statusText,
                activity,
                new BoxView { HeightRequest = 12, Opacity = 0 },
                footer
            }
        };
        Content = new Grid
        {
            Padding = new Thickness(8),
            Children = { content }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started)
            return;

        _started = true;
        try
        {
            // The native splash has already shown the same bundled A-Rigator
            // logo. Keep this branded hand-off calm and lightweight.
            await Task.Delay(650);
            if (_isLeaving)
                return;

            _statusText.Text = "App ready — robot discovery is available.";
            await Task.Delay(180);
            if (_isLeaving)
                return;

            await Content.FadeTo(0, 220, Easing.CubicInOut);
            if (!_isLeaving)
                _onComplete();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadingPage] Startup animation failed: {ex}");
            // Decorative startup animation must never block entry into the application.
            if (!_isLeaving)
                _onComplete();
        }
    }

    protected override void OnDisappearing()
    {
        _isLeaving = true;
        Content?.CancelAnimations();
        base.OnDisappearing();
    }

}

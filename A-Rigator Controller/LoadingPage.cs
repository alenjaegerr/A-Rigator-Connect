using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace A_Rigator_Controller;

public sealed class LoadingPage : ContentPage
{
    private readonly Action _onComplete;
    private readonly Label _statusText;
    private readonly Image _alligator;
    private readonly Grid _track;
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
            WidthRequest = 270,
            HeightRequest = 256,
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
        _alligator = new Image
        {
            Source = "gator_working.svg",
            Aspect = Aspect.AspectFit,
            WidthRequest = 58,
            HeightRequest = 34,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center
        };
        _track = new Grid
        {
            HeightRequest = 48,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star) },
            Children = { _alligator },
            IsClippedToBounds = true
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
                _track, activity,
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
            await WaitForTrackLayoutAsync();
            if (_isLeaving)
                return;

            // Keep the runner moving while the app finishes starting.
            var travelDistance = Math.Max(0, _track.Width - _alligator.Width);
            if (travelDistance > 1)
            {
                _alligator.TranslationX = 0;
                _alligator.TranslationY = 0;

                for (var pass = 0; pass < 3 && !_isLeaving; pass++)
                {
                    await _alligator.TranslateTo(travelDistance, -7, 420, Easing.CubicInOut);
                    if (_isLeaving) return;
                    await _alligator.TranslateTo(0, 7, 420, Easing.CubicInOut);
                    if (_isLeaving) return;
                }
            }

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
        _alligator.CancelAnimations();
        Content?.CancelAnimations();
        base.OnDisappearing();
    }

    private async Task WaitForTrackLayoutAsync()
    {
        if (_track.Width > _alligator.Width)
            return;

        var layoutReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? sizeChanged = null;
        sizeChanged = (_, _) =>
        {
            if (_track.Width > _alligator.Width)
                layoutReady.TrySetResult(true);
        };

        _track.SizeChanged += sizeChanged;
        await Task.WhenAny(layoutReady.Task, Task.Delay(3000));
        _track.SizeChanged -= sizeChanged;
    }
}

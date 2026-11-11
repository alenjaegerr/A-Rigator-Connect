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

    public LoadingPage(Action onComplete)
    {
        _onComplete = onComplete;
        BackgroundColor = Color.FromArgb("#F3F6F3");
        Shell.SetNavBarIsVisible(this, false);

        var brand = new Label
        {
            Text = "A-RIGATOR",
            FontSize = 31,
            FontAttributes = FontAttributes.Bold,
            CharacterSpacing = 2,
            TextColor = Color.FromArgb("#176B3A"),
            HorizontalTextAlignment = TextAlignment.Center
        };
        var subtitle = new Label
        {
            Text = "CONNECT",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            CharacterSpacing = 6,
            TextColor = Color.FromArgb("#4F6958"),
            HorizontalTextAlignment = TextAlignment.Center
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
            Text = "Preparing A-Rigator Connect",
            FontSize = 13,
            TextColor = Color.FromArgb("#5D7063"),
            HorizontalTextAlignment = TextAlignment.Center
        };
        _alligator = new Image
        {
            Source = "alligator_runner.svg",
            Aspect = Aspect.AspectFit,
            WidthRequest = 58,
            HeightRequest = 34,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center
        };
        _track = new Grid
        {
            HeightRequest = 42,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star) },
            Children = { _alligator }
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
                new Label { Text = "〰", FontSize = 34, TextColor = Color.FromArgb("#29A653"), HorizontalTextAlignment = TextAlignment.Center },
                brand, subtitle,
                new BoxView { HeightRequest = 26, Opacity = 0 },
                intro, _statusText,
                _track, activity,
                new BoxView { HeightRequest = 12, Opacity = 0 },
                footer
            }
        };
        Content = new Grid
        {
            Padding = new Thickness(8),
            Children =
            {
                new Border
                {
                    Stroke = Color.FromArgb("#DDE8DF"),
                    StrokeThickness = 1,
                    BackgroundColor = Colors.White,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(28) },
                    Padding = new Thickness(8),
                    Content = content,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Fill
                }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started)
            return;

        _started = true;
        await Task.Delay(100);
        await WaitForTrackLayoutAsync();

        // Animate only after the track has a real measured width.
        var travelDistance = Math.Max(0, _track.Width - _alligator.Width);
        if (travelDistance > 1)
        {
            _alligator.TranslationX = 0;
            await _alligator.TranslateTo(travelDistance, 0, 650, Easing.CubicInOut);
            await _alligator.TranslateTo(0, 0, 220, Easing.CubicInOut);
        }

        _statusText.Text = "App ready — robot discovery is available.";
        await Task.Delay(180);
        await Content.FadeTo(0, 220, Easing.CubicInOut);
        _onComplete();
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
        await Task.WhenAny(layoutReady.Task, Task.Delay(1500));
        _track.SizeChanged -= sizeChanged;
    }
}
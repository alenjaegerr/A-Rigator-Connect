using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace A_Rigator_Controller;

public sealed class LoadingPage : ContentPage
{
    private readonly Action _onComplete;
    private readonly Label _statusText;
    private readonly Image _runner;
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
        _runner = new Image
        {
            Source = ImageSource.FromFile("gator_working.svg"),
            Aspect = Aspect.AspectFit,
            WidthRequest = 58,
            HeightRequest = 48,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center
        };
        _track = new Grid
        {
            HeightRequest = 52,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star) },
            Children = { _runner },
            IsClippedToBounds = true
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
                _track,
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

            // The official baby-alligator mascot makes one smooth left-to-right
            // journey. Four gentle hops over 1.6 seconds give each up/down
            // bounce a 0.4-second cycle without competing TranslateTo calls.
            var travelDistance = Math.Max(0, _track.Width - _runner.Width);
            if (travelDistance > 1)
            {
                var journey = new Animation(progress =>
                {
                    _runner.TranslationX = travelDistance * progress;
                    const double bounceCycles = 4;
                    _runner.TranslationY =
                        -Math.Abs(Math.Sin(progress * bounceCycles * Math.PI)) * 7;
                });

                var completed = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                journey.Commit(
                    this,
                    "StartupMascotJourney",
                    rate: 16,
                    length: 1600,
                    easing: Easing.Linear,
                    finished: (_, _) => completed.TrySetResult(true));

                await completed.Task;
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

    private async Task WaitForTrackLayoutAsync()
    {
        if (_track.Width > _runner.Width)
            return;

        var layoutReady = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? sizeChanged = null;
        sizeChanged = (_, _) =>
        {
            if (_track.Width > _runner.Width)
                layoutReady.TrySetResult(true);
        };

        _track.SizeChanged += sizeChanged;
        await Task.WhenAny(layoutReady.Task, Task.Delay(3000));
        _track.SizeChanged -= sizeChanged;
    }

    protected override void OnDisappearing()
    {
        _isLeaving = true;
        Content?.CancelAnimations();
        this.AbortAnimation("StartupMascotJourney");
        base.OnDisappearing();
    }

}

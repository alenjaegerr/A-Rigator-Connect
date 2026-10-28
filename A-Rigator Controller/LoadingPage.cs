using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace A_Rigator_Controller;

public sealed class LoadingPage : ContentPage
{
    private readonly Action _onComplete;
    private readonly ProgressBar _progressBar;
    private readonly Label _progressText;
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
            Text = "Starting A-Rigator Connect",
            FontSize = 13,
            TextColor = Color.FromArgb("#5D7063"),
            HorizontalTextAlignment = TextAlignment.Center
        };

        _progressText = new Label
        {
            Text = "0%",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#176B3A"),
            HorizontalTextAlignment = TextAlignment.Center
        };

        _progressBar = new ProgressBar
        {
            Progress = 0,
            ProgressColor = Color.FromArgb("#29A653"),
            BackgroundColor = Color.FromArgb("#DCE7DE"),
            HeightRequest = 10,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill
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

        // The mascot has its own row above a conventional horizontal progress bar.
        _track = new Grid
        {
            HeightRequest = 52,
            RowSpacing = 1,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star) },
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(36) },
                new RowDefinition { Height = new GridLength(12) }
            }
        };
        _track.Add(_alligator);
        Grid.SetRow(_alligator, 0);
        _track.Add(_progressBar);
        Grid.SetRow(_progressBar, 1);

        var footer = new Label
        {
            Text = "AUTONOMOUS • CONNECTED • READY",
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
                brand,
                subtitle,
                new BoxView { HeightRequest = 26, Opacity = 0 },
                intro,
                _statusText,
                new BoxView { HeightRequest = 4, Opacity = 0 },
                _track,
                _progressText,
                new BoxView { HeightRequest = 18, Opacity = 0 },
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
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 28 },
                    Padding = new Thickness(8),
                    Content = content,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Fill
                }
            }
        };

        _track.SizeChanged += (_, _) => PositionAlligator(_progressBar.Progress);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started)
            return;

        _started = true;
        await Task.Delay(180);

        string[] stages =
        {
            "Waking up the control panel",
            "Checking app components",
            "Preparing your dashboard",
            "Almost there"
        };

        const int steps = 40;
        for (var i = 1; i <= steps; i++)
        {
            var progress = i / (double)steps;
            _statusText.Text = stages[Math.Min(stages.Length - 1, (i - 1) / 10)];
            _progressText.Text = $"{(int)Math.Round(progress * 100)}%";

            var targetX = Math.Max(0, (_track.Width - _alligator.Width) * progress);
            var bounceY = i % 2 == 0 ? -2 : 1;
            var move = _alligator.TranslateTo(targetX, bounceY, 75, Easing.Linear);
            var fill = _progressBar.ProgressTo(progress, 75, Easing.Linear);
            await Task.WhenAll(move, fill);
        }

        _statusText.Text = "Ready — welcome in!";
        _progressText.Text = "100%";
        await Task.Delay(250);
        _onComplete();
    }

    private void PositionAlligator(double progress)
    {
        if (_track.Width <= 0)
            return;

        _alligator.TranslationX = Math.Max(0, (_track.Width - _alligator.Width) * progress);
    }
}

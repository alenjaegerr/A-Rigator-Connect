using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace A_Rigator_Controller.Views;

/// <summary>
/// Shared branded navigation surface. Shell continues to own route navigation,
/// while this view replaces the platform-native tab strip on every main page.
/// </summary>
public sealed class BottomNavigationBar : ContentView
{
    public static readonly BindableProperty SelectedTabProperty =
        BindableProperty.Create(
            nameof(SelectedTab),
            typeof(string),
            typeof(BottomNavigationBar),
            "Home",
            propertyChanged: (bindable, _, _) =>
                ((BottomNavigationBar)bindable).RefreshSelection());

    private readonly Dictionary<string, Border> _tabBorders = new();
    private readonly Dictionary<string, Label> _tabLabels = new();
    private readonly Border _containerBorder;

    public string SelectedTab
    {
        get => (string)GetValue(SelectedTabProperty);
        set => SetValue(SelectedTabProperty, value);
    }

    public BottomNavigationBar()
    {
        HeightRequest = 78;
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.End;

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 3,
            Padding = new Thickness(7, 5)
        };

        AddTab(grid, 0, "Home", "nav_home.svg", "//main/home");
        AddTab(grid, 1, "Control", "nav_control.svg", "//main/manual");
        AddTab(grid, 2, "Automate", "nav_automate.svg", "//main/autonomous");
        AddTab(grid, 3, "Activity", "nav_activity.svg", "//main/activity");
        AddTab(grid, 4, "Profile", "nav_profile.svg", "//main/profile");

        _containerBorder = new Border
        {
            Padding = 0,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(24) },
            Content = grid
        };

        Content = _containerBorder;
        ApplyTheme();
        RefreshSelection();

        if (Application.Current is not null)
            Application.Current.RequestedThemeChanged += OnRequestedThemeChanged;
    }

    private void AddTab(Grid grid, int column, string title, string icon, string route)
    {
        var image = new Image
        {
            Source = ImageSource.FromFile(icon),
            WidthRequest = 29,
            HeightRequest = 29,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center
        };

        var label = new Label
        {
            Text = title,
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

        var stack = new VerticalStackLayout
        {
            Spacing = 2,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            Children = { image, label }
        };

        var border = new Border
        {
            Padding = new Thickness(2, 4),
            StrokeThickness = 0,
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Content = stack
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            try
            {
                if (Shell.Current is not null)
                    await Shell.Current.GoToAsync(route);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BottomNavigation] Route {route} failed: {ex}");
            }
        };
        border.GestureRecognizers.Add(tap);

        grid.Children.Add(border);
        Grid.SetColumn(border, column);
        _tabBorders[title] = border;
        _tabLabels[title] = label;
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ApplyTheme();
            RefreshSelection();
        });
    }

    private void ApplyTheme()
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;

        _containerBorder.BackgroundColor = isDark
            ? Color.FromArgb("#18251D")
            : Colors.White;
        _containerBorder.Stroke = new SolidColorBrush(Color.FromArgb(
            isDark ? "#34483B" : "#DCE6DF"));

        foreach (var (title, label) in _tabLabels)
            label.TextColor = isDark
                ? Color.FromArgb("#9AADA0")
                : Color.FromArgb("#718078");
    }

    private void RefreshSelection()
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;

        foreach (var (title, border) in _tabBorders)
        {
            var selected = string.Equals(
                title, SelectedTab, StringComparison.OrdinalIgnoreCase);

            border.BackgroundColor = selected
                ? Color.FromArgb(isDark ? "#294B37" : "#E8F4EB")
                : Colors.Transparent;

            _tabLabels[title].TextColor = selected
                ? Color.FromArgb(isDark ? "#A8E2BD" : "#176B45")
                : Color.FromArgb(isDark ? "#9AADA0" : "#718078");
        }
    }
}
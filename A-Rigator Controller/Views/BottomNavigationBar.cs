using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace A_Rigator_Controller.Views;

/// <summary>
/// Branded, full-colour bottom navigation. This deliberately replaces the
/// monochrome native Shell tab icons while retaining Shell route navigation.
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

    public string SelectedTab
    {
        get => (string)GetValue(SelectedTabProperty);
        set => SetValue(SelectedTabProperty, value);
    }

    public BottomNavigationBar()
    {
        HeightRequest = 76;
        HorizontalOptions = LayoutOptions.Fill;
        BackgroundColor = Colors.White;

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

        Content = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = new SolidColorBrush(Color.FromArgb("#DCE6DF")),
            StrokeThickness = 1,
            Padding = 0,
            Content = grid
        };

        RefreshSelection();
    }

    private void AddTab(Grid grid, int column, string title, string icon, string route)
    {
        var image = new Image
        {
            Source = ImageSource.FromFile(icon),
            WidthRequest = 27,
            HeightRequest = 27,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center
        };

        var label = new Label
        {
            Text = title,
            FontSize = 10,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            TextColor = Color.FromArgb("#718078")
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
            Stroke = new SolidColorBrush(Colors.Transparent),
            BackgroundColor = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
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
        Grid.SetRow(border, 0);
        _tabBorders[title] = border;
        _tabLabels[title] = label;
    }

    private void RefreshSelection()
    {
        foreach (var (title, border) in _tabBorders)
        {
            var selected = string.Equals(title, SelectedTab, StringComparison.OrdinalIgnoreCase);
            border.BackgroundColor = selected
                ? Color.FromArgb("#E8F4EB")
                : Colors.Transparent;
            _tabLabels[title].TextColor = selected
                ? Color.FromArgb("#176B45")
                : Color.FromArgb("#718078");
        }
    }
}
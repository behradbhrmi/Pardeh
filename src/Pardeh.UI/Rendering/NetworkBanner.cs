using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Pardeh.Core.Playback;

namespace Pardeh.UI.Rendering;

/// <summary>
/// The line over the player that says what a network item is doing: connecting or reconnecting (with Cancel), buffering,
/// or why it failed. It looks the same over every skin.
/// </summary>
internal sealed class NetworkBanner : Border
{
    private readonly TextBlock _text = new()
    {
        Foreground = Brushes.White, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxLines = 3,
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly Button _button = new()
    {
        FontSize = 11, Padding = new Thickness(10, 3), MinHeight = 0, Margin = new Thickness(10, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White,
        Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
    };

    public NetworkBanner(Action act)
    {
        Background = new SolidColorBrush(Color.FromArgb(225, 18, 22, 34));
        BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(12, 7);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(_button, 1);
        grid.Children.Add(_text);
        grid.Children.Add(_button);
        Child = grid;
        _button.Click += (_, _) => act();
    }

    public NetworkPhase Phase { get; private set; }

    public void Show(NetworkPhase phase, string message)
    {
        Phase = phase;
        if (_text.Text != message)
            _text.Text = message;
        var label = phase == NetworkPhase.Failed ? "OK" : "Cancel";
        if (!label.Equals(_button.Content))
            _button.Content = label;
        _button.IsVisible = phase is NetworkPhase.Connecting or NetworkPhase.Reconnecting or NetworkPhase.Failed;
    }
}

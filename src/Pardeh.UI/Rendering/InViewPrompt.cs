using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Pardeh.UI.Rendering;

/// <summary>
/// A one-line text question for a head without windows (Android): a small dark card near the top of the view, over the skin (the
/// soft keyboard covers the bottom). The answer, or null when cancelled (Cancel, an empty tap outside is not used).
/// </summary>
internal static partial class InViewPrompt
{
    public static Task<string?> Ask(Canvas overlay, double viewWidth, string question, string initial)
    {
        var done = new TaskCompletionSource<string?>();
        var box = new TextBox { Text = initial, FontSize = 16, MinHeight = 42, VerticalContentAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(8) };
        var ok = new Button { Content = "OK", MinWidth = 96, MinHeight = 42, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 96, MinHeight = 42, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, IsCancel = true };
        var card = new Border
        {
            Width = Math.Max(240, viewWidth - 24), Padding = new Thickness(16), CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.Parse("#EE20242F")), BorderBrush = new SolidColorBrush(Color.Parse("#66FFFFFF")), BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = question, FontSize = 15, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap },
                    box,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } },
                },
            },
        };
        var host = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Dark, Child = card };
        Canvas.SetLeft(host, 12);
        Canvas.SetTop(host, 64);

        void Finish(string? answer)
        {
            overlay.Children.Remove(host);
            done.TrySetResult(answer);
        }

        ok.Click += (_, _) => Finish(box.Text ?? string.Empty);
        cancel.Click += (_, _) => Finish(null);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                Finish(box.Text ?? string.Empty);
            else if (e.Key == Key.Escape)
                Finish(null);
        };
        overlay.Children.Add(host);
        box.AttachedToVisualTree += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        return done.Task;
    }
}

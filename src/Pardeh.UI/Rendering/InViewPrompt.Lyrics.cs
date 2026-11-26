using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Pardeh.UI.Rendering;

/// <summary>More in-view dialogs for a head without windows (Android): a confirmation and a multi-line text editor (the lyrics).</summary>
internal static partial class InViewPrompt
{
    private static Button Pill(string text, bool primary = false) => new()
    {
        Content = text, MinWidth = 92, MinHeight = 42, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        IsDefault = primary,
    };

    private static Border Card(double viewWidth, Control child) => new()
    {
        Width = Math.Max(240, viewWidth - 24), Padding = new Thickness(16), CornerRadius = new CornerRadius(12),
        Background = new SolidColorBrush(Color.Parse("#FF20242F")), BorderBrush = new SolidColorBrush(Color.Parse("#66FFFFFF")), BorderThickness = new Thickness(1),
        Child = child,
    };

    private static ThemeVariantScope Host(Canvas overlay, Control card, double top)
    {
        var host = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Dark, Child = card };
        Canvas.SetLeft(host, 12);
        Canvas.SetTop(host, top);
        overlay.Children.Add(host);
        return host;
    }

    /// <summary>A question with a title, an explanation and two buttons. True when the user agreed.</summary>
    public static Task<bool> Confirm(Canvas overlay, double viewWidth, string title, string body, string yes, string no)
    {
        var done = new TaskCompletionSource<bool>();
        var ok = Pill(yes, primary: true);
        var cancel = Pill(no);
        var card = Card(viewWidth, new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = body, FontSize = 14, Foreground = new SolidColorBrush(Color.Parse("#D8DCE8")), TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } },
            },
        });
        var host = Host(overlay, card, 80);
        void Finish(bool answer)
        {
            overlay.Children.Remove(host);
            done.TrySetResult(answer);
        }

        ok.Click += (_, _) => Finish(true);
        cancel.Click += (_, _) => Finish(false);
        return done.Task;
    }

    /// <summary>
    /// A multi-line text editor (the lyrics): the answer, or null when cancelled. <paramref name="readClipboard"/> fills the "Paste" button
    /// (a head's clipboard); long press in the field pastes too.
    /// </summary>
    public static Task<string?> Edit(Canvas overlay, double viewWidth, double viewHeight, string title, string hint, string initial, Func<Task<string?>>? readClipboard)
    {
        var done = new TaskCompletionSource<string?>();
        var box = new TextBox
        {
            Text = initial, FontSize = 15, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            Height = Math.Clamp(viewHeight * 0.4, 150, 300), VerticalContentAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(8),
            PlaceholderText = hint,
        };
        var save = Pill("Save", primary: true);
        var cancel = Pill("Cancel");
        var paste = Pill("Paste");
        var clear = Pill("Clear");
        // Four equal buttons in one row: Paste, Clear, Cancel, Save (without the clipboard hook: Clear, Cancel, Save).
        var items = readClipboard is not null ? new[] { paste, clear, cancel, save } : new[] { clear, cancel, save };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", items.Select(_ => "*"))), ColumnSpacing = 6 };
        for (var i = 0; i < items.Length; i++)
        {
            items[i].MinWidth = 0;
            items[i].HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(items[i], i);
            row.Children.Add(items[i]);
        }
        var card = Card(viewWidth, new StackPanel
        {
            Spacing = 10,
            Children = { new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap }, box, row },
        });
        var host = Host(overlay, card, 56);
        void Finish(string? answer)
        {
            overlay.Children.Remove(host);
            done.TrySetResult(answer);
        }

        save.Click += (_, _) => Finish(box.Text ?? string.Empty);
        cancel.Click += (_, _) => Finish(null);
        clear.Click += (_, _) => box.Text = string.Empty;
        paste.Click += async (_, _) =>
        {
            if (readClipboard is not null && await readClipboard() is { Length: > 0 } text)
                box.Text = text;
        };
        box.AttachedToVisualTree += (_, _) => box.Focus();
        return done.Task;
    }
}

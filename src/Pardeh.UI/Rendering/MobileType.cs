using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Pardeh.UI.Rendering;

/// <summary>
/// The type-first rows of the Windows Phone 7 and Zune HD looks in the Library and Settings: no cards, separators or
/// chevrons; large light words on the bare page. Windows Phone puts a square accent tile beside a library category;
/// Zune uses words alone and writes its menus in lower case.
/// </summary>
internal static class TypeRows
{
    private static readonly FontFamily Face = new("Segoe UI, Roboto, sans-serif");

    // Windows has the thin face as a family of its own (asking Segoe UI for a light weight drew a heavy one); elsewhere the weight does it.
    private static readonly FontFamily Thin = new("Segoe UI Light, Roboto, sans-serif");

    private static TextBlock Line(string text, double size, Color color, bool light = true) => new()
    {
        Text = text, FontSize = size, FontFamily = light ? Thin : Face, FontWeight = light && !OperatingSystem.IsWindows() ? FontWeight.Light : FontWeight.Normal,
        Foreground = new SolidColorBrush(color), TextWrapping = TextWrapping.Wrap,
    };

    public static Control Group(IEnumerable<Control> rows, Thickness margin)
    {
        var stack = new StackPanel { Margin = margin };
        stack.Children.AddRange(rows);
        return stack;
    }

    /// <summary>
    /// A library category. <paramref name="glyph"/> is the white icon for the Windows Phone tile (null on Zune);
    /// <paramref name="lower"/> writes the fixed menu words in lower case (never a user's own playlist or artist names).
    /// </summary>
    public static Control Category(MobileTheme theme, Control? glyph, string title, int? count, string unit, bool lit, bool lower, Action open)
    {
        var ink = lit ? theme.Accent : theme.Ink;
        var name = Line(lower ? title.ToLowerInvariant() : title, theme.Zune ? 31 : 23, ink);
        var caption = count is { } n ? $"{n} {(n == 1 ? unit.TrimEnd('s') : unit)}" : null;
        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(theme.Zune ? 22 : 14, 4, 12, 4), Children = { name } };
        if (caption is not null)
        {
            var small = Line(caption, 12.5, theme.Zune ? theme.Accent : theme.Sub, light: false);
            small.Margin = new Thickness(1, theme.Zune ? -3 : -1, 0, 0);
            lines.Children.Add(small);
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), MinHeight = theme.Zune ? 62 : 72, Background = Brushes.Transparent };
        if (glyph is not null)
        {
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(new Border { Width = 56, Height = 56, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(theme.Accent), Child = glyph });
        }

        Grid.SetColumn(lines, 1);
        grid.Children.Add(lines);
        grid.Tapped += (_, _) => open();
        return grid;
    }

    /// <summary>
    /// A settings row. <paramref name="menu"/> is true for the rows of a menu (the category list), which get the large type;
    /// switches and value rows stay readable at a smaller size.
    /// </summary>
    public static Control Setting(MobileTheme theme, string title, string? summary, Control? trailing, Action? tap, bool menu, double minHeight)
    {
        var name = Line(menu && theme.Zune ? title.ToLowerInvariant() : title, menu ? theme.Zune ? 29 : 24 : 18, theme.Ink);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, menu ? 4 : 7, 8, menu ? 4 : 7), Children = { name } };
        if (!string.IsNullOrEmpty(summary))
        {
            var small = Line(summary, 13, theme.Sub, light: false);
            small.Margin = new Thickness(1, menu ? -2 : 1, 0, 0);
            text.Children.Add(small);
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = minHeight, Background = Brushes.Transparent, Children = { text } };
        if (trailing is not null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Center;
            trailing.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(trailing, 1);
            grid.Children.Add(trailing);
        }

        if (tap is not null)
            grid.Tapped += (_, _) => tap();
        return grid;
    }

    public static Control Heading(MobileTheme theme, string text)
    {
        var heading = Line(theme.Zune ? text.ToLowerInvariant() : text, 17, theme.Accent);
        heading.Margin = new Thickness(8, 10, 0, 4);
        return heading;
    }
}

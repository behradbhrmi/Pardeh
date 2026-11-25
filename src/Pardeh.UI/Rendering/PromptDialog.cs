using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Pardeh.UI.Rendering;

/// <summary>A one-line text question (library search, playlist name) in a small XP-style dialog.</summary>
internal static class PromptDialog
{
    /// <summary>The text entered, or null when cancelled.</summary>
    /// <param name="multiline">A tall box that keeps line breaks (lyrics, chords); Enter then starts a new line.</param>
    public static async Task<string?> Ask(Window owner, string question, string initial, bool multiline = false)
    {
        string? result = null;
        var box = new TextBox { Text = initial, Width = multiline ? 460 : 300, FontFamily = UiTheme.Family, FontSize = 12 };
        if (multiline)
        {
            box.AcceptsReturn = true;
            box.Height = 320;
            box.VerticalContentAlignment = VerticalAlignment.Top;
        }

        var ok = new Button { Content = "OK", Width = 75, IsDefault = !multiline, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancel", Width = 75, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        var dialog = new Window
        {
            Title = question,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(12),
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = question, FontFamily = UiTheme.Family, FontSize = 12, Foreground = Brushes.Black },
                    box,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
                },
            },
        };
        ok.Click += (_, _) => { result = box.Text ?? string.Empty; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                dialog.Close();
        };
        UiTheme.Dialog(dialog);
        await dialog.ShowDialog(owner);
        return result;
    }
}

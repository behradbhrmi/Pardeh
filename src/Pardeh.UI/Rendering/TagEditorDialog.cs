using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Pardeh.Core.Changes;

namespace Pardeh.UI.Rendering;

/// <summary>Library › Edit Info: title, artist, album, genre, year and track number. It only asks; the change service writes.</summary>
internal static class TagEditorDialog
{
    /// <summary>Shows the editor; returns what the user changed, or null when cancelled.</summary>
    public static async Task<TagFields?> Ask(Window owner, string fileName, TagFields current)
    {
        var shown = TagForm.Values(current);
        var boxes = shown.Select(value => new TextBox { Text = value, Width = 320 }).ToArray();

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 6, ColumnSpacing = 10 };
        for (var i = 0; i < boxes.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = TagForm.Labels[i] + ":", VerticalAlignment = VerticalAlignment.Center, FontFamily = UiTheme.Family, Foreground = Brushes.Black };
            Grid.SetRow(label, i);
            Grid.SetRow(boxes[i], i);
            Grid.SetColumn(boxes[i], 1);
            grid.Children.Add(label);
            grid.Children.Add(boxes[i]);
        }

        TagFields? result = null;
        var error = new TextBlock { Foreground = Brushes.DarkRed, FontFamily = UiTheme.Family, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var ok = new Button { Content = "OK", Width = 75, IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancel", Width = 75, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        var dialog = new Window
        {
            Title = "Edit Info - " + fileName,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(14),
                Spacing = 12,
                Children =
                {
                    grid,
                    error,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
                },
            },
        };

        ok.Click += (_, _) =>
        {
            var (change, problem) = TagForm.Read(shown, boxes.Select(b => b.Text ?? string.Empty).ToArray());
            if (change is null)
            {
                error.Text = problem;
                error.IsVisible = true;
                return;
            }

            result = change;
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        UiTheme.Dialog(dialog);
        await dialog.ShowDialog(owner);
        return result;
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Pardeh.UI.Rendering;

/// <summary>Library › Edit Info: title, artist, album, genre, year and track number, written into the file's tags.</summary>
internal static class TagEditorDialog
{
    /// <summary>Shows the editor; returns true when the tags were saved.</summary>
    public static async Task<bool> Edit(Window owner, string path)
    {
        TagLib.File file;
        try
        {
            file = TagLib.File.Create(path);
        }
        catch (Exception ex) when (ex is TagLib.UnsupportedFormatException or TagLib.CorruptFileException or IOException or UnauthorizedAccessException)
        {
            PlatformServices.Current.Inform(owner, "This file's information can't be edited:\n" + ex.Message, "Edit Info");
            return false;
        }

        using (file)
        {
            var tag = file.Tag;
            var fields = new (string Label, TextBox Box)[]
            {
                ("Title", new TextBox { Text = tag.Title ?? string.Empty }),
                ("Artist", new TextBox { Text = tag.JoinedPerformers ?? string.Empty }),
                ("Album artist", new TextBox { Text = tag.JoinedAlbumArtists ?? string.Empty }),
                ("Album", new TextBox { Text = tag.Album ?? string.Empty }),
                ("Genre", new TextBox { Text = tag.JoinedGenres ?? string.Empty }),
                ("Year", new TextBox { Text = tag.Year > 0 ? tag.Year.ToString() : string.Empty }),
                ("Track number", new TextBox { Text = tag.Track > 0 ? tag.Track.ToString() : string.Empty }),
            };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 6, ColumnSpacing = 10 };
            for (var i = 0; i < fields.Length; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var label = new TextBlock { Text = fields[i].Label + ":", VerticalAlignment = VerticalAlignment.Center, FontFamily = UiTheme.Family, Foreground = Brushes.Black };
                Grid.SetRow(label, i);
                Grid.SetRow(fields[i].Box, i);
                Grid.SetColumn(fields[i].Box, 1);
                fields[i].Box.Width = 320;
                grid.Children.Add(label);
                grid.Children.Add(fields[i].Box);
            }

            var saved = false;
            var ok = new Button { Content = "OK", Width = 75, IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center };
            var cancel = new Button { Content = "Cancel", Width = 75, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
            var dialog = new Window
            {
                Title = "Edit Info - " + Path.GetFileName(path),
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
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
                    },
                },
            };

            ok.Click += (_, _) =>
            {
                string Value(int i) => fields[i].Box.Text?.Trim() ?? string.Empty;
                string[] List(int i) => Value(i).Length == 0 ? [] : Value(i).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                try
                {
                    tag.Title = Value(0);
                    tag.Performers = List(1);
                    tag.AlbumArtists = List(2);
                    tag.Album = Value(3);
                    tag.Genres = List(4);
                    tag.Year = uint.TryParse(Value(5), out var year) ? year : 0;
                    tag.Track = uint.TryParse(Value(6), out var track) ? track : 0;
                    file.Save();
                    saved = true;
                    dialog.Close();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    PlatformServices.Current.Inform(dialog,
                        "Couldn't save (is the file playing or read-only?):\n" + ex.Message, "Edit Info");
                }
            };
            cancel.Click += (_, _) => dialog.Close();
            UiTheme.Dialog(dialog);
            await dialog.ShowDialog(owner);
            return saved;
        }
    }
}

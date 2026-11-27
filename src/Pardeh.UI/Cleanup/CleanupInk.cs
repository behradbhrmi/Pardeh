using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Pardeh.Core.Cleanup;

namespace Pardeh.UI.CleanupViews;

/// <summary>The colours and shape of the cleanup screens: a light desktop dialog, or a dark full screen on a phone.</summary>
internal sealed record CleanupInk(IBrush Text, IBrush Sub, IBrush Line, IBrush Bad, IBrush Card, bool Narrow)
{
    public static CleanupInk Desktop { get; } = new(Brushes.Black, Brush("#4A4F5C"), Brush("#C9CEDA"), Brush("#8B0000"), Brush("#FFFFFF"), false);

    public static CleanupInk Phone { get; } = new(Brushes.White, Brush("#B8BFD0"), Brush("#3D4455"), Brush("#FF9C9C"), Brush("#262B38"), true);

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    public TextBlock Label(string text, double size = 13, bool bold = false, IBrush? colour = null, bool wrap = true) => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, Foreground = colour ?? Text,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Lets Escape press a desktop dialog's Cancel button.</summary>
    /// <remarks>
    /// Not on a phone: Android's Back arrives as the Escape key and then as Back, so it would press this and close the screen
    /// under it too.
    /// </remarks>
    public Button Cancels(Button button)
    {
        button.IsCancel = !Narrow;
        return button;
    }

    public Button Button(string text, Action click, bool primary = false)
    {
        var button = new Button
        {
            Content = text, MinWidth = Narrow ? 0 : 84, MinHeight = Narrow ? 42 : 26, IsDefault = primary && !Narrow,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => click();
        return button;
    }

    public CheckBox Tick(string text, bool on)
    {
        var box = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Center, MinHeight = Narrow ? 40 : 0 };
        if (text.Length > 0)
            box.Content = Label(text);
        return box;
    }

    public Border Boxed(Control child) => new()
    {
        Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Narrow ? 8 : 2),
        Padding = new Thickness(Narrow ? 10 : 8), Child = child,
    };

    public ScrollViewer Scroller(Control content) => new()
    {
        Content = content,
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = Narrow ? Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
    };

    public static string FieldName(CleanupSuggestion suggestion) => suggestion.Field switch
    {
        CleanupField.Title => "Title",
        CleanupField.Artists => "Artist",
        CleanupField.AlbumArtists => "Album artist",
        CleanupField.Album => "Album",
        CleanupField.Genres => "Genre",
        CleanupField.Composers => "Composer",
        CleanupField.Comment => "Comment",
        CleanupField.Copyright => "Copyright",
        CleanupField.Cover => "Cover",
        _ => TextName(suggestion.TextId ?? string.Empty),
    };

    /// <summary>A readable name for an extra text field ("id3v2:TPUB" is "Publisher").</summary>
    public static string TextName(string id)
    {
        var parts = id.Split(':');
        var field = parts.Length > 1 ? parts[1] : id;
        return field switch
        {
            "TPUB" or "WM/Publisher" or "©pub" or "ORGANIZATION" or "PUBLISHER" => "Publisher",
            "TENC" or "WM/EncodedBy" or "©enc" or "ENCODEDBY" or "ENCODED-BY" => "Encoded by",
            "TSSE" or "WM/ToolName" or "©too" or "ENCODER" => "Encoder",
            "TOPE" => "Original artist",
            "TEXT" => "Lyricist",
            "WOAR" or "WM/AuthorURL" => "Artist web page",
            "WOAF" => "Track web page",
            "WPUB" => "Publisher web page",
            "WCOM" => "Shop address",
            "WXXX" => "Web address",
            "COMM" => "Another comment",
            "TXXX" => parts.Length > 2 && parts[2].Length > 0 ? parts[2] : "Custom text",
            "desc" or "ldes" => "Description",
            _ => field,
        };
    }

    public static string Why(CleanupReason reason) => reason switch
    {
        CleanupReason.Junk => "Website text",
        CleanupReason.FromFileName => "From the file name",
        CleanupReason.BrokenText => "Repaired text: check it reads right",
        CleanupReason.Repeated => "The same words are on songs of unrelated artists",
        _ => "The same picture is on songs of unrelated artists",
    };

    public static string Shown(string value) => value.Length == 0 ? "(empty)" : value.Replace('\u001f', ' ');
}

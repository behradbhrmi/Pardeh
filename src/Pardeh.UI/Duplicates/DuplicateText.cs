using System.Globalization;
using Avalonia.Controls;
using Pardeh.Core.Duplicates;
using Pardeh.Core.Library;
using Pardeh.UI.CleanupViews;

namespace Pardeh.UI.DuplicateViews;

/// <summary>How copies and groups are put into words, and the card a copy is shown on.</summary>
internal static class DuplicateText
{
    // The invisible left-to-right mark keeps the parts in order when some are Persian.
    public const string Dot = "‎ · ";

    public static string Count(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

    public static string Kind(DuplicateKind kind) => kind switch
    {
        DuplicateKind.Identical => "Identical files",
        DuplicateKind.SameAudio => "Same audio, different details",
        _ => "Probably the same recording",
    };

    public static string KindNote(DuplicateKind kind) => kind switch
    {
        DuplicateKind.Identical => "The files are the same byte for byte.",
        DuplicateKind.SameAudio => "The sound is the same byte for byte; only the information in the files differs.",
        _ => "Grouped by title, artist and length only. They can still be different recordings: listen before removing one.",
    };

    public static string Length(double seconds) =>
        seconds > 0 ? TimeSpan.FromSeconds(Math.Round(seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture) : "?:??";

    public static string Size(long bytes) =>
        bytes >= 1_000_000_000 ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1e9:0.0} GB")
        : bytes >= 100_000 ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1e6:0.0} MB")
        : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(bytes > 0 ? 1 : 0, bytes / 1000)} KB");

    /// <summary>Where a file is, in words a person can use.</summary>
    public static string Folder(string source)
    {
        if (!MediaSources.IsContent(source))
        {
            var folder = Path.GetDirectoryName(source) ?? string.Empty;
            // The part of Android's storage path that is the same for every file says nothing.
            return folder.StartsWith("/storage/emulated/0/", StringComparison.Ordinal) ? folder["/storage/emulated/0/".Length..] : folder;
        }

        var text = Uri.UnescapeDataString(source[(source.LastIndexOf('/') + 1)..]);
        if (text.StartsWith("primary:", StringComparison.Ordinal))
            text = text["primary:".Length..];
        var slash = text.LastIndexOf('/');
        return slash > 0 ? text[..slash] : "a picked folder";
    }

    /// <summary>Title and artist as cleaned, for a heading.</summary>
    public static string Song(SongCopy copy)
    {
        var artist = string.Join(", ", (copy.Facts.Tags.Artists ?? []).Select(a => Core.Cleanup.JunkText.Clean(a).Text.Trim()).Where(a => a.Length > 0));
        var title = copy.Name.Title.Length > 0 ? copy.Name.Title : copy.Facts.FileName;
        return artist.Length > 0 ? title + Dot + artist : title;
    }

    public static string Facts(SongCopy copy) =>
        string.Join(Dot, CopyAdvisor.StatedQuality(copy), Length(copy.Seconds), Size(copy.Item.FileSize), Folder(copy.Source));

    private static string Tags(SongCopy copy)
    {
        string Part(string name, MergeField field)
        {
            var value = CopyAdvisor.Show(copy, field);
            return name + ": " + (value.Length == 0 ? "(empty)" : value.Replace('\n', ' '));
        }

        return string.Join(Dot, Part("Title", MergeField.Title), Part("Artist", MergeField.Artists), Part("Album", MergeField.Album))
            + (copy.Facts.CoverHash is null ? Dot + "no cover" : Dot + CopyAdvisor.Show(copy, MergeField.Cover));
    }

    /// <summary>A copy's file name, quality, length, place and information, one under another.</summary>
    public static StackPanel Details(CleanupInk ink, SongCopy copy, IReadOnlyList<string>? notes = null, bool tags = true)
    {
        var name = ink.Label(copy.Facts.FileName, bold: true);
        name.MaxLines = 2;
        var panel = new StackPanel { Spacing = 2, Children = { name, ink.Label(Facts(copy), 12, colour: ink.Sub) } };
        if (tags)
        {
            var line = ink.Label(Tags(copy), 12, colour: ink.Sub);
            line.MaxLines = 3;
            panel.Children.Add(line);
        }

        if (notes is { Count: > 0 })
            panel.Children.Add(ink.Label(string.Join(Dot, notes), 11, colour: notes.Any(n => n.StartsWith("Cut", StringComparison.Ordinal) || n.StartsWith("Can't", StringComparison.Ordinal)) ? ink.Bad : ink.Sub));
        return panel;
    }

    /// <summary>A row of buttons that wraps on a narrow screen.</summary>
    public static WrapPanel Buttons(params Control[] buttons)
    {
        var panel = new WrapPanel();
        foreach (var button in buttons)
        {
            button.Margin = new Avalonia.Thickness(0, 0, 8, 6);
            panel.Children.Add(button);
        }

        return panel;
    }
}

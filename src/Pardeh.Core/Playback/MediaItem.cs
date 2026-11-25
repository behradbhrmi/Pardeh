namespace Pardeh.Core.Playback;

/// <summary>A playable item: a local file or a URL, with the metadata we know about it.</summary>
public sealed class MediaItem
{
    public MediaItem(string source)
    {
        Source = source;
        Name = Path.GetFileNameWithoutExtension(source);
    }

    /// <summary>File path or URL.</summary>
    public string Source { get; }

    /// <summary>Display name (title tag, else file name).</summary>
    public string Name { get; set; }

    public TimeSpan Duration { get; set; }

    public int VideoWidth { get; set; }

    public int VideoHeight { get; set; }

    /// <summary>Attributes such as Title, Author, WM/AlbumTitle, Copyright, Genre. Case-insensitive.</summary>
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Looks up an attribute using WMP-style names and common aliases.</summary>
    public string GetAttribute(string name)
    {
        if (Attributes.TryGetValue(name, out var value))
            return value;

        string? alias = name.ToLowerInvariant() switch
        {
            "album" or "wm/albumtitle" => "Album",
            "author" or "artist" or "wm/albumartist" => "Author",
            "title" or "name" => "Title",
            "genre" or "wm/genre" => "Genre",
            "year" or "wm/year" => "Year",
            "tracknumber" or "wm/tracknumber" => "TrackNumber",
            _ => null,
        };

        if (alias is not null && Attributes.TryGetValue(alias, out value))
            return value;

        return name.Equals("title", StringComparison.OrdinalIgnoreCase) || name.Equals("name", StringComparison.OrdinalIgnoreCase)
            ? Name
            : name.Equals("duration", StringComparison.OrdinalIgnoreCase)
                ? ((int)Duration.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : name.Equals("sourceURL", StringComparison.OrdinalIgnoreCase) ? Source
                : name.Equals("FileType", StringComparison.OrdinalIgnoreCase) ? Path.GetExtension(Source).TrimStart('.').ToLowerInvariant()
                : string.Empty;
    }

    public override string ToString() => Name;
}

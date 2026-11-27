using System.Globalization;
using Pardeh.Core.Changes;

namespace Pardeh.UI;

/// <summary>
/// The fields of Edit Info, shared by the desktop dialog and the phone editor. Only a field the user really changed is
/// written, so that what the form shows in a simpler way (several artists on one line, a full date as a year) is left alone.
/// </summary>
internal static class TagForm
{
    public static readonly string[] Labels = ["Title", "Artist", "Album artist", "Album", "Genre", "Year", "Track number"];

    private const int YearField = 5, TrackField = 6;

    public static bool IsNumber(int field) => field is YearField or TrackField;

    public static string[] Values(TagFields tags) =>
    [
        tags.Title ?? string.Empty,
        string.Join("; ", tags.Artists ?? []),
        string.Join("; ", tags.AlbumArtists ?? []),
        tags.Album ?? string.Empty,
        string.Join("; ", tags.Genres ?? []),
        tags.Year > 0 ? tags.Year.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
        tags.Track > 0 ? tags.Track.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
    ];

    /// <summary>The changes between what was shown and what was entered, or an error to show.</summary>
    public static (TagFields? Change, string? Error) Read(IReadOnlyList<string> shown, IReadOnlyList<string> entered)
    {
        var values = entered.Select(v => v.Trim()).ToArray();
        string? Text(int i) => values[i] == shown[i].Trim() ? null : values[i];
        string[]? List(int i) => Text(i)?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        uint? year = null, track = null;
        foreach (var field in new[] { YearField, TrackField })
        {
            if (Text(field) is not { } text)
                continue;
            uint number = 0;
            // Persian and Arabic digits are accepted too.
            var latin = string.Concat(text.Select(c => char.IsDigit(c) ? ((int)char.GetNumericValue(c)).ToString(CultureInfo.InvariantCulture) : c.ToString()));
            if (latin.Length > 0 && !uint.TryParse(latin, NumberStyles.None, CultureInfo.InvariantCulture, out number) || number > 9999)
                return (null, $"{Labels[field]} must be a number.");
            if (field == YearField)
                year = number;
            else
                track = number;
        }

        return (new TagFields
        {
            Title = Text(0),
            Artists = List(1),
            AlbumArtists = List(2),
            Album = Text(3),
            Genres = List(4),
            Year = year,
            Track = track,
        }, null);
    }
}

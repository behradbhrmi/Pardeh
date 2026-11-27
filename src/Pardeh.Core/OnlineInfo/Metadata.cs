using System.Globalization;

namespace Pardeh.Core.OnlineInfo;

/// <summary>
/// What a metadata search sends: a title and, unless only the title is searched, an artist. Nothing else (no file name, folder,
/// length, device or user name) leaves the machine.
/// </summary>
/// <param name="IncludeLive">Also return recordings marked as live ones, for a song that is one.</param>
public sealed record MetadataSearch(string Title, string? Artist, bool IncludeLive = false);

/// <summary>One credited artist, with the other names the database knows (other spellings and scripts).</summary>
public sealed record ArtistCredit(string Name, IReadOnlyList<string> OtherNames);

/// <summary>A release (album, single, compilation) that holds a recording.</summary>
/// <param name="Date">As the database gives it: a year, or a fuller date.</param>
/// <param name="Track">The recording's number on it, or 0.</param>
public sealed record MetadataRelease(string Id, string? GroupId, string Title, string AlbumArtist, string? Date, string? PrimaryType,
    IReadOnlyList<string> SecondaryTypes, bool Official, uint Track)
{
    public uint Year => MetadataText.YearOf(Date);
}

/// <summary>A recording a search found.</summary>
/// <param name="Disambiguation">What tells it apart from others of the same name ("live, 1986...").</param>
/// <param name="ArtistText">The artists as credited, with their joining words.</param>
/// <param name="LengthMs">0 when unknown.</param>
public sealed record MetadataCandidate(string Id, string Title, string Disambiguation, IReadOnlyList<ArtistCredit> Artists, string ArtistText, int LengthMs,
    string? FirstReleaseDate, IReadOnlyList<MetadataRelease> Releases)
{
    /// <summary>The year it first came out, not that of a reissue; 0 when unknown.</summary>
    public uint FirstYear
    {
        get
        {
            var first = MetadataText.YearOf(FirstReleaseDate);
            return first != 0 ? first : Releases.Select(r => r.Year).Where(y => y != 0).DefaultIfEmpty().Min();
        }
    }
}

public sealed record CoverImage(byte[] Data, string MimeType);

/// <summary>A database of song information.</summary>
public interface IMetadataProvider
{
    string Name { get; }

    /// <summary>Recordings for the search, possibly none. Throws <see cref="MetadataException"/> when the service could not be asked.</summary>
    Task<IReadOnlyList<MetadataCandidate>> SearchAsync(MetadataSearch search, CancellationToken cancel);

    /// <summary>The front cover for a release, or null when there is none. Throws <see cref="MetadataException"/> when the service could not be asked.</summary>
    Task<CoverImage?> CoverAsync(MetadataRelease release, CancellationToken cancel);
}

/// <summary>A service that could not be asked. The message is for the user.</summary>
public sealed class MetadataException(string message, Exception? inner = null) : Exception(message, inner);

internal static class MetadataText
{
    public static uint YearOf(string? date) =>
        date is { Length: >= 4 } && uint.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year >= 1000 ? year : 0;
}

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

/// <summary>What a link listed for a song leads to. In the order they are shown.</summary>
public enum SongLinkKind
{
    /// <summary>A page where the song is listed as free to download. A page, never the file itself.</summary>
    FreeDownloadPage,

    Buy,

    /// <summary>Listening without paying.</summary>
    Stream,

    /// <summary>Listening with a subscription.</summary>
    Subscription,
}

/// <summary>A page listed for a song. Anyone can edit the database, so it is a pointer and not a promise.</summary>
/// <param name="Address">An https address.</param>
/// <param name="Release">The release it is listed on, or null when it is listed on the recording itself.</param>
public sealed record SongLink(SongLinkKind Kind, string Address, string? Release = null)
{
    /// <summary>The host the link leads to.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Site => SafeLinks.Check(Address) is { } page ? page.IdnHost : string.Empty;
}

/// <param name="Links">By kind.</param>
/// <param name="Insecure">Links left out because they don't use https (or can't be opened safely).</param>
/// <param name="ReleasesSeen">The releases looked at, of <paramref name="Releases"/>.</param>
public sealed record SongLinks(IReadOnlyList<SongLink> Links, int Insecure = 0, int ReleasesSeen = 0, int Releases = 0);

/// <summary>Which web addresses may be offered for opening in the browser.</summary>
public static class SafeLinks
{
    private const int MaxAddress = 2000;

    /// <summary>The address, when it is an https page of a named public site; else null.</summary>
    public static Uri? Check(string? address) =>
        address is { Length: > 0 and <= MaxAddress } && Uri.TryCreate(address, UriKind.Absolute, out var page)
        && page.Scheme == Uri.UriSchemeHttps && page.IsDefaultPort && page.UserInfo.Length == 0
        && page.HostNameType == UriHostNameType.Dns && Downloads.NetworkRules.IsPublicName(page.IdnHost)
            ? page
            : null;
}

/// <summary>A database of song information.</summary>
public interface IMetadataProvider
{
    string Name { get; }

    /// <summary>Recordings for the search, possibly none. Throws <see cref="MetadataException"/> when the service could not be asked.</summary>
    Task<IReadOnlyList<MetadataCandidate>> SearchAsync(MetadataSearch search, CancellationToken cancel);

    /// <summary>The front cover for a release, or null when there is none. Throws <see cref="MetadataException"/> when the service could not be asked.</summary>
    /// <param name="small">Only the 500 pixel picture, for many songs at once: less to download and to hold.</param>
    Task<CoverImage?> CoverAsync(MetadataRelease release, CancellationToken cancel, bool small = false);

    /// <summary>The pages listed for a recording and its releases. Throws <see cref="MetadataException"/> when the service could not be asked.</summary>
    Task<SongLinks> LinksAsync(string recordingId, CancellationToken cancel) => Task.FromResult(new SongLinks([]));
}

/// <summary>A service that could not be asked. The message is for the user.</summary>
public sealed class MetadataException(string message, Exception? inner = null) : Exception(message, inner);

internal static class MetadataText
{
    public static uint YearOf(string? date) =>
        date is { Length: >= 4 } && uint.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year >= 1000 ? year : 0;
}

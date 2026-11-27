using System.Text.Json.Serialization;
using Pardeh.Core.Playback;

namespace Pardeh.Core.Downloads;

/// <summary>Who made a catalog's file and on what terms, as the catalog states it.</summary>
/// <param name="LicenseName">As the catalog names it, such as "CC BY-SA 4.0" or "Public domain".</param>
/// <param name="SourcePage">The file's own page at the catalog.</param>
public sealed record DownloadCredit(string Catalog, string Title, string Creator, string LicenseName, string? LicenseUrl, string SourcePage)
{
    /// <summary>The licence forbids passing on changed versions.</summary>
    public bool NoDerivatives { get; init; }

    public bool NonCommercial { get; init; }

    /// <summary>What goes into the file's copyright field.</summary>
    [JsonIgnore]
    public string CopyrightLine => string.Join(". ", new[] { Creator, LicenseName }.Where(p => p.Length > 0)) + (LicenseUrl is null ? string.Empty : ". " + LicenseUrl);
}

/// <summary>What would be downloaded, for the user to agree to.</summary>
/// <param name="Address">As asked for.</param>
/// <param name="Final">Where the server sends it from, after redirects.</param>
/// <param name="Stem">The file name without its extension, made safe.</param>
/// <param name="Size">In bytes, or null when the server does not say.</param>
public sealed record DownloadOffer(DownloadKind Kind, Uri Address, Uri Final, string Stem, string Extension, string TypeName, long? Size)
{
    /// <summary>A strong ETag, to tell whether the file is still the same when resuming.</summary>
    public string? ETag { get; init; }

    public DateTimeOffset? LastModified { get; init; }

    /// <summary>The SHA-1 the catalog gives, in hex.</summary>
    public string? Sha1 { get; init; }

    public DownloadCredit? Credit { get; init; }

    public string FileName => Stem + Extension;

    public bool HostChanged => !string.Equals(Address.IdnHost, Final.IdnHost, StringComparison.OrdinalIgnoreCase);

    public DownloadJob ToJob() => new(Guid.NewGuid().ToString("N"), Kind, Address.AbsoluteUri, Final.IdnHost, Stem, Extension, Size)
    {
        ETag = ETag,
        LastModified = LastModified,
        Sha1 = Sha1,
        Credit = Credit,
        Created = DateTime.UtcNow,
    };
}

/// <summary>One download, kept while it is unfinished.</summary>
/// <param name="Host">The host the user agreed to download from.</param>
public sealed record DownloadJob(string Id, DownloadKind Kind, string Address, string Host, string Stem, string Extension, long? Size)
{
    public string? ETag { get; init; }

    public DateTimeOffset? LastModified { get; init; }

    public string? Sha1 { get; init; }

    public DownloadCredit? Credit { get; init; }

    public DateTime Created { get; init; }

    [JsonIgnore]
    public string FileName => Stem + Extension;

    /// <summary>An address with a token or password in it is never written to disk, so such a download can't outlive the app.</summary>
    [JsonIgnore]
    public bool MayBeSaved => !NetworkMedia.HasSecret(Address);
}

/// <summary>A finished download: where it came from stays with it.</summary>
/// <param name="Source">The file as the library knows it.</param>
/// <param name="Address">Where it was downloaded from (without the query when that held a token).</param>
public sealed record DownloadRecord(string Id, DateTime When, DownloadKind Kind, string Source, string FileName, string Address, long Size)
{
    /// <summary>Null for a pasted link: Pardeh does not know its licence.</summary>
    public DownloadCredit? Credit { get; init; }
}

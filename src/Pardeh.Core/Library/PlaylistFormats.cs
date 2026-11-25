using System.Text;
using System.Xml.Linq;

namespace Pardeh.Core.Library;

/// <summary>Reads playlist files — .m3u / .m3u8, .pls, .wpl, .asx / .wax / .wvx, .xspf — into media paths and URLs.</summary>
public static class PlaylistFormats
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".m3u", ".m3u8", ".pls", ".wpl", ".asx", ".wax", ".wvx", ".xspf",
    };

    public static bool IsPlaylist(string path) => Extensions.Contains(Path.GetExtension(path));

    /// <summary>The entries of a playlist, with relative paths resolved against the playlist's folder.</summary>
    public static IReadOnlyList<string> Read(string path)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
        string Resolve(string entry)
        {
            entry = entry.Trim();
            if (entry.StartsWith("file:///", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(entry, UriKind.Absolute, out var file))
                return file.LocalPath;
            if (entry.Contains("://") || Path.IsPathFullyQualified(entry))
                return entry;
            return Path.GetFullPath(Path.Combine(folder, entry));
        }

        try
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var text = File.ReadAllText(path, extension == ".m3u" ? Encoding.Latin1 : Encoding.UTF8);
            return Entries(extension, text).Where(e => e.Length > 0).Select(Resolve).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return [];
        }
    }

    private static IEnumerable<string> Entries(string extension, string text) => extension switch
    {
        ".m3u" or ".m3u8" => text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')),
        ".pls" => text.Split('\n').Select(l => l.Trim())
            .Where(l => l.StartsWith("File", StringComparison.OrdinalIgnoreCase) && l.Contains('='))
            .Select(l => l[(l.IndexOf('=') + 1)..].Trim()),
        ".wpl" => XDocument.Parse(text).Descendants().Where(e => e.Name.LocalName.Equals("media", StringComparison.OrdinalIgnoreCase))
            .Select(e => (string?)e.Attribute("src")).OfType<string>(),
        ".xspf" => XDocument.Parse(text).Descendants().Where(e => e.Name.LocalName == "location").Select(e => Uri.UnescapeDataString(e.Value)),
        _ => Asx(text),
    };

    /// <summary>
    /// The entries of a playlist downloaded from <paramref name="address"/>: relative ones are resolved against it, and only
    /// network addresses are kept (a list from a server must not name files on this computer). Null for an HLS list, which the
    /// decoder plays itself, and for text that is no playlist.
    /// </summary>
    public static IReadOnlyList<string>? ParseRemote(string address, string text)
    {
        if (text.Contains("#EXT-X-", StringComparison.Ordinal) || !Uri.TryCreate(address, UriKind.Absolute, out var origin))
            return null;
        try
        {
            var entries = Entries(Path.GetExtension(origin.AbsolutePath).ToLowerInvariant(), text)
                .Select(e => e.Trim())
                .Where(e => e.Length > 0)
                .Select(e => Uri.TryCreate(origin, e, out var resolved) ? (e.Contains("://", StringComparison.Ordinal) ? e : resolved.AbsoluteUri) : e)
                .Where(Playback.NetworkMedia.IsNetwork)
                .Take(2000)
                .ToList();
            return entries.Count > 0 ? entries : null;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>ASX is loose XML (any case, often not well-formed): pull &lt;ref href="…"&gt; out with a regex.</summary>
    private static IEnumerable<string> Asx(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, "<ref\\s+href\\s*=\\s*\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value));

    /// <summary>Expands any playlist files in <paramref name="sources"/> into their entries.</summary>
    public static IEnumerable<string> Expand(IEnumerable<string> sources) =>
        sources.SelectMany(s => IsPlaylist(s) && File.Exists(s) ? Read(s) : [s]);
}

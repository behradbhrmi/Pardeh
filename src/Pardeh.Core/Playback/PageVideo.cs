using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pardeh.Core.Playback;

/// <summary>
/// The video address a web page publishes itself, if it does: the description meant for search engines (JSON-LD
/// "VideoObject"), the one meant for link previews (og:video, twitter:player:stream), or a plain video tag. Nothing is
/// run or worked out from the page's scripts, so sites that hide their video (YouTube among them) give nothing.
/// The page is untrusted: only a web address is ever taken from it, and never one that leads from a public page into the
/// user's own network.
/// </summary>
public static partial class PageVideo
{
    public const string NoAddressMessage = "This is a web page, and it does not give the address of its video, so Pardeh cannot play it.";

    private const RegexOptions Loose = RegexOptions.IgnoreCase | RegexOptions.Singleline;

    private static readonly string[] MediaExtensions = [".mp4", ".m4v", ".webm", ".mkv", ".mov", ".ogv", ".m3u8", ".mpd", ".ts", ".flv"];

    [GeneratedRegex("<!--.*?-->", Loose, 2000)]
    private static partial Regex Comments();

    [GeneratedRegex("<script\\b([^>]*)>(.*?)</script\\s*>", Loose, 2000)]
    private static partial Regex Scripts();

    // One tag with its attributes; a '>' inside a quoted value does not end it.
    [GeneratedRegex("<(meta|video|source|base|/video)\\b((?:[^>\"']|\"[^\"]*\"|'[^']*')*)>", Loose, 2000)]
    private static partial Regex Tags();

    [GeneratedRegex("([a-zA-Z_:][-a-zA-Z0-9_:.]*)(?:\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s\"'>]+)))?", Loose, 2000)]
    private static partial Regex Attribute();

    /// <summary>A video address the page gives, resolved to a full one, or null.</summary>
    public static string? Find(string pageAddress, string html)
    {
        if (!Uri.TryCreate(pageAddress, UriKind.Absolute, out var page))
            return null;
        try
        {
            return Pick(page, Candidates(page, Comments().Replace(html, " ")));
        }
        catch (RegexMatchTimeoutException)
        {
            return null; // a page built to be slow to read
        }
    }

    private sealed record Candidate(int Rank, string Address, Uri From);

    private static List<Candidate> Candidates(Uri page, string html)
    {
        var found = new List<Candidate>();
        var home = page;

        // The structured description first: it names the page's own video, not an advertisement or a decoration.
        foreach (Match script in Scripts().Matches(html))
        {
            if (!script.Groups[1].Value.Contains("ld+json", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                using var json = JsonDocument.Parse(script.Groups[2].Value, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                Walk(json.RootElement, found, page);
            }
            catch (JsonException)
            {
                // A broken description: the rest of the page may still do.
            }
        }

        // Tags, with the scripts gone: a video tag quoted inside a script is not on the page.
        string? declaredType = null;
        var previews = new List<(int Rank, string Address)>();
        var inVideo = false;
        foreach (Match tag in Tags().Matches(Scripts().Replace(html, " ")))
        {
            var name = tag.Groups[1].Value.ToLowerInvariant();
            if (name == "/video")
            {
                inVideo = false;
                continue;
            }

            var attributes = Attributes(tag.Groups[2].Value);
            switch (name)
            {
                case "base" when attributes.GetValueOrDefault("href") is { } href && Uri.TryCreate(page, href, out var based):
                    home = based;
                    break;
                case "meta":
                {
                    var key = (attributes.GetValueOrDefault("property") ?? attributes.GetValueOrDefault("name"))?.ToLowerInvariant();
                    var value = attributes.GetValueOrDefault("content");
                    if (value is null)
                        break;
                    if (key == "og:video:type")
                        declaredType ??= value;
                    else if (key is "og:video:secure_url" or "og:video:url" or "og:video")
                        previews.Add((key == "og:video:secure_url" ? 20 : 21, value));
                    else if (key == "twitter:player:stream")
                        previews.Add((30, value));
                    break;
                }

                case "video":
                    inVideo = !tag.Groups[2].Value.TrimEnd().EndsWith('/');
                    if (attributes.GetValueOrDefault("src") is { } own && IsVideoType(attributes.GetValueOrDefault("type")))
                        found.Add(new Candidate(40, own, home));
                    break;
                case "source" when inVideo:
                    if (attributes.GetValueOrDefault("src") is { } source && IsVideoType(attributes.GetValueOrDefault("type")))
                        found.Add(new Candidate(41, source, home));
                    break;
            }
        }

        // A link preview often names a player page, not a video: it counts only when its type or its name says video.
        foreach (var (rank, address) in previews)
        {
            if (declaredType is not null && IsVideoType(declaredType) || HasMediaExtension(address))
                found.Add(new Candidate(rank, address, home));
        }

        return found;
    }

    private static void Walk(JsonElement element, List<Candidate> found, Uri page, int depth = 0)
    {
        if (depth > 16)
            return;
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                Walk(item, found, page, depth + 1);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return;
        if (element.TryGetProperty("@type", out var type) && type.ToString().Contains("VideoObject", StringComparison.Ordinal)
            && element.TryGetProperty("contentUrl", out var content) && content.ValueKind == JsonValueKind.String)
        {
            found.Add(new Candidate(10, content.GetString()!, page));
        }

        foreach (var property in element.EnumerateObject())
            Walk(property.Value, found, page, depth + 1);
    }

    private static string? Pick(Uri page, List<Candidate> candidates) =>
        candidates
            .Select(c => (c.Rank, Address: Uri.TryCreate(c.From, c.Address.Trim(), out var uri) && Allowed(page, uri) ? uri : null))
            .Where(c => c.Address is not null)
            // DASH plays poorly here (no seeking, slow): taken only when the page offers nothing else.
            .OrderBy(c => c.Address!.AbsolutePath.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(c => c.Rank)
            .Select(c => c.Address!.AbsoluteUri)
            .FirstOrDefault();

    private static bool Allowed(Uri page, Uri video)
    {
        if (video.Scheme is not ("http" or "https") || video == page)
            return false;
        var extension = System.IO.Path.GetExtension(video.AbsolutePath).ToLowerInvariant();
        if (extension is ".html" or ".htm" or ".swf")
            return false;
        return !IsPrivate(video) || IsPrivate(page);
    }

    /// <summary>True for this device and the home network, by the address as written.</summary>
    private static bool IsPrivate(Uri uri)
    {
        var host = uri.Host.Trim('[', ']').TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || !host.Contains('.') && !host.Contains(':'))
            return true;
        if (!IPAddress.TryParse(host, out var address))
            return false;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal)
            return true;
        var b = address.GetAddressBytes();
        return b.Length == 4 && (b[0] is 0 or 10 or 127 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168 || b[0] == 169 && b[1] == 254);
    }

    /// <summary>No type given, or one that is video (HLS and DASH lists have "application" types).</summary>
    private static bool IsVideoType(string? type) =>
        type is null || type.Trim().Length == 0
        || type.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
        || type.Contains("mpegurl", StringComparison.OrdinalIgnoreCase)
        || type.Contains("dash+xml", StringComparison.OrdinalIgnoreCase);

    private static bool HasMediaExtension(string address)
    {
        var path = address.Split('?', '#')[0];
        return MediaExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string> Attributes(string text)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Attribute().Matches(text))
        {
            var value = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Success ? match.Groups[3].Value : match.Groups[4].Value;
            attributes.TryAdd(match.Groups[1].Value, WebUtility.HtmlDecode(value));
        }

        return attributes;
    }
}

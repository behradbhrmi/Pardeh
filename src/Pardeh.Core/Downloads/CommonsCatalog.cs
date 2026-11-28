using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pardeh.Core.LyricsKit;

namespace Pardeh.Core.Downloads;

/// <summary>An audio file a catalog lists.</summary>
/// <param name="Creator">As the catalog states it, which may be the composer rather than the performer.</param>
/// <param name="LicenseName">Empty when the catalog states none.</param>
/// <param name="SourcePage">The file's page at the catalog, or null when the catalog gave no usable one.</param>
/// <param name="File">The file itself, or null when it is not where catalog files are.</param>
/// <param name="WhyNot">Why Pardeh offers no download, or null when it does.</param>
public sealed record CatalogFile(string Title, string Creator, string LicenseName, string? LicenseUrl, Uri? SourcePage, Uri? File, long Size,
    string Extension, string TypeName, double Seconds, string? Sha1, string? WhyNot)
{
    public bool NonCommercial { get; init; }

    public bool NoDerivatives { get; init; }

    /// <summary>Stated to be in the public domain, or dedicated to it: no licence asks for credit.</summary>
    public bool PublicDomain { get; init; }

    /// <summary>The wording the creator asks to be credited with, where one is stated.</summary>
    public string Attribution { get; init; } = string.Empty;

    /// <summary>Where the catalog says the file comes from.</summary>
    public string Origin { get; init; } = string.Empty;

    /// <summary>Other limits the catalog notes, such as trademarks or rights of people shown.</summary>
    public string Restrictions { get; init; } = string.Empty;

    public bool CanDownload => WhyNot is null && File is not null && SourcePage is not null;

    /// <summary>What a download of it would be. Null when none is offered.</summary>
    public DownloadOffer? ToOffer(string catalog)
    {
        if (!CanDownload)
            return null;
        return new DownloadOffer(DownloadKind.Catalog, File!, File!, DownloadNames.Stem(Title + ".x"), Extension, TypeName, Size > 0 ? Size : null)
        {
            Sha1 = Sha1,
            Credit = new DownloadCredit(catalog, Title, Attribution.Length > 0 ? Attribution : Creator, LicenseName, LicenseUrl, SourcePage!.AbsoluteUri)
            {
                NonCommercial = NonCommercial,
                NoDerivatives = NoDerivatives,
            },
        };
    }
}

/// <summary>A catalog of freely licensed audio.</summary>
public interface IDownloadCatalog
{
    string Name { get; }

    /// <summary>Files for the words, possibly none. Throws <see cref="DownloadException"/> when the catalog could not be asked.</summary>
    Task<IReadOnlyList<CatalogFile>> SearchAsync(string words, CancellationToken cancel);
}

/// <summary>
/// Wikimedia Commons (https://commons.wikimedia.org): files its contributors state to be free to reuse, each with its
/// licence. Requests are made one at a time, with an agent that says how to reach the maintainers, as its policy asks.
/// </summary>
/// <remarks>Everything that comes back is untrusted: sizes are limited, hosts are checked, and markup becomes plain text.</remarks>
/// <param name="http">Must not follow redirects by itself.</param>
/// <param name="userAgent">"Name/version (contact address)".</param>
public sealed partial class CommonsCatalog(HttpClient http, string userAgent, RequestThrottle? throttle = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IDownloadCatalog
{
    private const string Api = "https://commons.wikimedia.org/w/api.php";
    private const int Results = 20, Attempts = 3, MaxAnswer = 4 * 1024 * 1024, MaxText = 300;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(25);

    private readonly RequestThrottle _throttle = throttle ?? new RequestThrottle(TimeSpan.FromSeconds(1.5));
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public string Name => "Wikimedia Commons";

    public long MaxSize { get; init; } = DownloadRules.MaxSize;

    public async Task<IReadOnlyList<CatalogFile>> SearchAsync(string words, CancellationToken cancel)
    {
        words = words.Trim();
        if (words.Length == 0)
            return [];
        var address = new Uri(Api + "?action=query&format=json&formatversion=2&generator=search&gsrnamespace=6"
            + "&gsrlimit=" + Results.ToString(CultureInfo.InvariantCulture) + "&gsrsearch=" + Uri.EscapeDataString("filetype:audio " + words)
            + "&prop=imageinfo&iiprop=" + Uri.EscapeDataString("url|size|mime|sha1|extmetadata") + "&iiextmetadatalanguage=en&iiextmetadatafilter="
            + Uri.EscapeDataString("ObjectName|Artist|Credit|Attribution|LicenseShortName|LicenseUrl|License|Restrictions|NonFree"));
        var body = await GetAsync(address, cancel).ConfigureAwait(false);
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out _))
                throw new DownloadException("Wikimedia Commons could not run this search. Try other words.");
            return Parse(json.RootElement, MaxSize);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new DownloadException("Wikimedia Commons sent an answer that can't be read.", ex);
        }
    }

    private async Task<byte[]> GetAsync(Uri address, CancellationToken cancel)
    {
        var busy = false;
        Exception? failure = null;
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            // Its policy: at least five seconds after being told to slow down, or what the answer names.
            var wait = TimeSpan.FromSeconds(5 * attempt);
            await _throttle.WaitAsync(cancel).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(Timeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, address);
                request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                if (status == 429 || status >= 500)
                {
                    busy = true;
                    if (response.Headers.RetryAfter?.Delta is { } asked && asked > wait)
                    {
                        // A long wait is the user's to sit out, not the app's.
                        if (asked > TimeSpan.FromSeconds(20))
                            break;
                        wait = asked;
                    }
                }
                else if (!response.IsSuccessStatusCode)
                {
                    throw new DownloadException(string.Create(CultureInfo.InvariantCulture, $"Wikimedia Commons refused the request ({status})."));
                }
                else
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                    using var buffer = new MemoryStream();
                    var chunk = new byte[32 * 1024];
                    int read;
                    while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
                    {
                        if (buffer.Length + read > MaxAnswer)
                            throw new DownloadException("Wikimedia Commons sent too much.");
                        buffer.Write(chunk, 0, read);
                    }

                    return buffer.ToArray();
                }
            }
            catch (OperationCanceledException ex) when (!cancel.IsCancellationRequested)
            {
                failure = ex;
                busy = false;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                failure = ex;
                busy = false;
            }

            if (attempt < Attempts)
                await _delay(wait, cancel).ConfigureAwait(false);
        }

        throw new DownloadException(busy ? "Wikimedia Commons is busy. Try again in a minute."
            : "Wikimedia Commons couldn't be reached. Check the internet connection.", failure);
    }

    // ---- reading the answer ------------------------------------------------------------------------

    internal static IReadOnlyList<CatalogFile> Parse(JsonElement root, long maxSize = DownloadRules.MaxSize)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("query", out var query) || query.ValueKind != JsonValueKind.Object
            || !query.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
            return [];
        var found = new List<(long Order, CatalogFile File)>();
        foreach (var page in pages.EnumerateArray().Take(100))
        {
            if (page.ValueKind != JsonValueKind.Object || !page.TryGetProperty("imageinfo", out var infos) || infos.ValueKind != JsonValueKind.Array
                || infos.GetArrayLength() == 0 || infos[0].ValueKind != JsonValueKind.Object)
                continue;
            var order = page.TryGetProperty("index", out var index) && index.TryGetInt64(out var at) ? at : long.MaxValue;
            if (Read(Text(page, "title"), infos[0], maxSize) is { } file)
                found.Add((order, file));
        }

        // The pages don't come in the search's order: their numbers give it.
        return [.. found.OrderBy(f => f.Order).Select(f => f.File)];
    }

    private static CatalogFile? Read(string pageTitle, JsonElement info, long maxSize)
    {
        var meta = info.TryGetProperty("extmetadata", out var data) && data.ValueKind == JsonValueKind.Object ? data : default;
        string Meta(string name) => meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty(name, out var entry) && entry.ValueKind == JsonValueKind.Object
            ? Plain(Text(entry, "value"))
            : string.Empty;

        var fileName = pageTitle.StartsWith("File:", StringComparison.Ordinal) ? pageTitle[5..] : pageTitle;
        var title = Meta("ObjectName") is { Length: > 0 } named ? named : Plain(Path.GetFileNameWithoutExtension(fileName));
        if (title.Length == 0)
            return null;

        var source = OnlineInfo.SafeLinks.Check(Text(info, "descriptionurl")) is { Host: "commons.wikimedia.org" } page ? page : null;
        Uri? file = null;
        if (Uri.TryCreate(Text(info, "url"), UriKind.Absolute, out var address) && address is { Scheme: "https", Host: "upload.wikimedia.org", IsDefaultPort: true }
            && address.UserInfo.Length == 0)
            file = new UriBuilder(address) { Query = string.Empty, Fragment = string.Empty, Port = -1 }.Uri; // without the counting parameters it adds

        var size = info.TryGetProperty("size", out var bytes) && bytes.TryGetInt64(out var n) && n > 0 ? n : 0;
        var seconds = info.TryGetProperty("duration", out var length) && length.ValueKind == JsonValueKind.Number && length.TryGetDouble(out var d) && d > 0 ? d : 0;
        var sha1 = Text(info, "sha1");
        if (sha1.Length != 40 || !sha1.All(char.IsAsciiHexDigit))
            sha1 = string.Empty;

        var (extension, type) = Kind(Text(info, "mime"), file?.AbsolutePath ?? fileName);
        var licenseName = Meta("LicenseShortName");
        var code = Meta("License").ToLowerInvariant();
        var licenseUrl = OnlineInfo.SafeLinks.Check(Meta("LicenseUrl"))?.AbsoluteUri;
        var parts = code.Split('-');

        string? whyNot = null;
        if (Meta("NonFree").Equals("true", StringComparison.OrdinalIgnoreCase))
            whyNot = "Commons marks this file as not free.";
        else if (licenseName.Length == 0 || !Permits(code))
            whyNot = "Pardeh doesn't recognise a licence here that permits downloading.";
        else if (extension.Length == 0)
            whyNot = "This kind of file isn't one Pardeh downloads.";
        else if (size > maxSize)
            whyNot = "The file is larger than Pardeh downloads.";
        else if (file is null || source is null)
            whyNot = "The file or its page is not where Commons files are.";

        return new CatalogFile(title, Meta("Artist"), licenseName, licenseUrl, source, file, size, extension, type, seconds,
            sha1.Length > 0 ? sha1 : null, whyNot)
        {
            NonCommercial = parts.Contains("nc"),
            NoDerivatives = parts.Contains("nd"),
            PublicDomain = code is "pd" or "cc0" or "cc-zero" || code.StartsWith("pd-", StringComparison.Ordinal) || code.StartsWith("cc-pd", StringComparison.Ordinal),
            Attribution = Meta("Attribution"),
            Origin = Meta("Credit"),
            Restrictions = Meta("Restrictions"),
        };
    }

    // The codes Commons uses for licences that allow anyone to download and keep a copy.
    private static bool Permits(string code) =>
        code is "pd" or "cc0" or "cc-zero" or "gfdl" or "fal" || code.StartsWith("cc-by", StringComparison.Ordinal)
        || code.StartsWith("cc-sa", StringComparison.Ordinal) || code.StartsWith("cc-pd", StringComparison.Ordinal)
        || code.StartsWith("pd-", StringComparison.Ordinal) || code.StartsWith("gfdl-", StringComparison.Ordinal);

    private static (string Extension, string Type) Kind(string mime, string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return mime.ToLowerInvariant() switch
        {
            "audio/mpeg" => (".mp3", "MP3"),
            "application/ogg" or "audio/ogg" => extension == ".opus" ? (".opus", "Opus") : (".ogg", "Ogg audio"),
            "audio/flac" or "audio/x-flac" => (".flac", "FLAC"),
            "audio/wav" or "audio/x-wav" => (".wav", "WAV"),
            "audio/mp4" or "audio/x-m4a" => (".m4a", "MP4 audio"),
            _ => (string.Empty, mime.Length > 0 ? mime : "unknown type"),
        };
    }

    [GeneratedRegex(@"<(?<tag>\w+)[^>]*style\s*=\s*""[^""]*display\s*:\s*none[^""]*""[^>]*>.*?</\k<tag>>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 200)]
    private static partial Regex Hidden();

    [GeneratedRegex(@"<(style|script)\b.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 200)]
    private static partial Regex Code();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.None, 200)]
    private static partial Regex Tag();

    /// <summary>The words of a bit of markup: its tags and anything it hides taken out, no control characters, not too long.</summary>
    internal static string Plain(string markup)
    {
        if (markup.Length > 20_000)
            markup = markup[..20_000];
        try
        {
            markup = Tag().Replace(Code().Replace(Hidden().Replace(markup, " "), " "), " ");
        }
        catch (RegexMatchTimeoutException)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        foreach (var c in WebUtility.HtmlDecode(markup))
        {
            if (char.IsWhiteSpace(c))
                text.Append(' ');
            // No controls, and none of the marks that turn text around on screen.
            else if (!char.IsControl(c) && c is not (>= '‪' and <= '‮' or >= '⁦' and <= '⁩'))
                text.Append(c);
        }

        var words = string.Join(' ', text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Replace(" ,", ",", StringComparison.Ordinal);
        if (words.Length > MaxText)
            words = words[..(char.IsHighSurrogate(words[MaxText - 1]) ? MaxText - 1 : MaxText)].TrimEnd() + "…";
        return words;
    }

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}

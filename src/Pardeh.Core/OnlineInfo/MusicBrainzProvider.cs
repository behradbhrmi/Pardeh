using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Pardeh.Core.LyricsKit;

namespace Pardeh.Core.OnlineInfo;

/// <summary>
/// MusicBrainz (https://musicbrainz.org) for song information and the Cover Art Archive for front covers. Free for
/// non-commercial use, no key. MusicBrainz asks for at most one request per second and a User-Agent that says how to reach
/// the maintainers. Genres are not asked for: they are under a non-commercial licence.
/// </summary>
/// <remarks>Everything that comes back is untrusted: sizes are limited, ids are checked, and only known hosts are followed.</remarks>
public sealed class MusicBrainzProvider : IMetadataProvider
{
    private const string SearchAddress = "https://musicbrainz.org/ws/2/recording";
    private const string CoverAddress = "https://coverartarchive.org/";
    private const int Attempts = 3, MaxAnswer = 8 * 1024 * 1024, MaxCover = 10 * 1024 * 1024, MaxHops = 5, MaxText = 500;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(25);

    private readonly HttpClient _http;
    private readonly string _userAgent;
    private readonly string _coverUserAgent;
    private readonly RequestThrottle _throttle;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <param name="http">Must not follow redirects by itself: cover addresses are checked at each step.</param>
    /// <param name="userAgent">For MusicBrainz: "Name/version (contact address)".</param>
    public MusicBrainzProvider(HttpClient http, string userAgent, string coverUserAgent = "Pardeh/1.0", RequestThrottle? throttle = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http;
        _userAgent = userAgent;
        _coverUserAgent = coverUserAgent;
        _throttle = throttle ?? new RequestThrottle(TimeSpan.FromMilliseconds(1100));
        _delay = delay ?? Task.Delay;
    }

    public string Name => "MusicBrainz";

    public async Task<IReadOnlyList<MetadataCandidate>> SearchAsync(MetadataSearch search, CancellationToken cancel)
    {
        if (string.IsNullOrWhiteSpace(search.Title))
            return [];
        var address = new Uri(SearchAddress + "?fmt=json&limit=100&query=" + Uri.EscapeDataString(Query(search)));
        var body = await GetAsync(address, "MusicBrainz", MaxAnswer, cancel).ConfigureAwait(false);
        if (body is null)
            return [];
        try
        {
            using var json = JsonDocument.Parse(body);
            return Parse(json.RootElement);
        }
        catch (JsonException ex)
        {
            throw new MetadataException("MusicBrainz sent an answer that can't be read.", ex);
        }
    }

    internal static string Query(MetadataSearch search)
    {
        // Inside a quoted phrase only the quote and the backslash mean something to the search.
        static string Quoted(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        var query = new StringBuilder("recording:").Append(Quoted(search.Title.Trim()));
        if (!string.IsNullOrWhiteSpace(search.Artist))
            query.Append(" AND artist:").Append(Quoted(search.Artist.Trim()));
        // A known song has hundreds of live takes, which would crowd the studio one out of the first answers.
        if (!search.IncludeLive)
            query.Append(" AND NOT comment:live");
        return query.ToString();
    }

    private const int KeptLinks = 60, KeptPerSite = 3;

    /// <summary>Two requests: the recording's own links, then those of the releases that hold it (the first hundred).</summary>
    public async Task<SongLinks> LinksAsync(string recordingId, CancellationToken cancel)
    {
        if (!Guid.TryParse(recordingId, out var id))
            return new SongLinks([]);
        var own = await GetAsync(new Uri($"{SearchAddress}/{id:D}?inc=url-rels&fmt=json"), "MusicBrainz", MaxAnswer, cancel).ConfigureAwait(false);
        var releases = await GetAsync(new Uri($"https://musicbrainz.org/ws/2/release?recording={id:D}&inc=url-rels&limit=100&fmt=json"), "MusicBrainz", MaxAnswer, cancel)
            .ConfigureAwait(false);
        try
        {
            using var first = JsonDocument.Parse(own ?? "{}"u8.ToArray());
            using var second = JsonDocument.Parse(releases ?? "{}"u8.ToArray());
            return ParseLinks(first.RootElement, second.RootElement);
        }
        catch (JsonException ex)
        {
            throw new MetadataException("MusicBrainz sent an answer that can't be read.", ex);
        }
    }

    internal static SongLinks ParseLinks(JsonElement recording, JsonElement releases)
    {
        var found = new List<SongLink>();
        var seen = new HashSet<(SongLinkKind, string)>();
        var insecure = new HashSet<string>(StringComparer.Ordinal);
        var perSite = new Dictionary<(SongLinkKind, string), int>();

        void Add(JsonElement entity, string? release)
        {
            foreach (var relation in Items(entity, "relations"))
            {
                // A link marked as ended no longer holds.
                if (relation.TryGetProperty("ended", out var ended) && ended.ValueKind == JsonValueKind.True)
                    continue;
                SongLinkKind? kind = Text(relation, "type") switch
                {
                    "download for free" => SongLinkKind.FreeDownloadPage,
                    "purchase for download" or "purchase for mail-order" => SongLinkKind.Buy,
                    "free streaming" => SongLinkKind.Stream,
                    "streaming" => SongLinkKind.Subscription,
                    _ => null,
                };
                if (kind is not { } what || !relation.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.Object
                    || !url.TryGetProperty("resource", out var resource) || resource.ValueKind != JsonValueKind.String || resource.GetString() is not { } address)
                    continue;
                if (SafeLinks.Check(address) is not { } page)
                {
                    insecure.Add(address);
                    continue;
                }

                var site = (what, page.IdnHost);
                if (!seen.Add((what, page.AbsoluteUri)) || perSite.GetValueOrDefault(site) >= KeptPerSite)
                    continue;
                perSite[site] = perSite.GetValueOrDefault(site) + 1;
                found.Add(new SongLink(what, page.AbsoluteUri, release));
            }
        }

        Add(recording, null);
        var looked = 0;
        foreach (var release in Items(releases, "releases"))
        {
            looked++;
            Add(release, Text(release, "title") is { Length: > 0 } title ? title : null);
        }

        // The sort keeps the recording's own links first within each kind.
        return new SongLinks([.. found.OrderBy(l => l.Kind).Take(KeptLinks)], insecure.Count, looked, Math.Max(looked, Number(releases, "release-count")));
    }

    public async Task<CoverImage?> CoverAsync(MetadataRelease release, CancellationToken cancel, bool small = false)
    {
        var places = new List<string>();
        if (Guid.TryParse(release.GroupId, out var group))
            places.Add($"release-group/{group:D}/front-");
        if (Guid.TryParse(release.Id, out var single))
            places.Add($"release/{single:D}/front-");

        // The large size of either first; some pictures have only the smaller one.
        foreach (var size in small ? ["500"] : new[] { "1200", "500" })
        {
            foreach (var place in places)
            {
                var data = await GetAsync(new Uri(CoverAddress + place + size), "The Cover Art Archive", MaxCover, cancel, cover: true).ConfigureAwait(false);
                if (data is not null && PictureType(data) is { } type)
                    return new CoverImage(data, type);
            }
        }

        return null;
    }

    private static string? PictureType(byte[] data) =>
        data.Length > 8 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF ? "image/jpeg"
        : data.Length > 8 && data.AsSpan(0, 4).SequenceEqual<byte>([0x89, 0x50, 0x4E, 0x47]) ? "image/png"
        : null;

    private static bool IsCoverHost(Uri address) =>
        address.Scheme == Uri.UriSchemeHttps && address.IsDefaultPort && address.UserInfo.Length == 0
        && (address.Host is "coverartarchive.org" or "archive.org" || address.Host.EndsWith(".archive.org", StringComparison.Ordinal));

    /// <summary>The body, or null for "not found". A busy service is asked again after the time it names.</summary>
    private async Task<byte[]?> GetAsync(Uri address, string service, int max, CancellationToken cancel, bool cover = false)
    {
        var busy = false;
        Exception? failure = null;
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var wait = TimeSpan.FromSeconds(2 * attempt);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            try
            {
                var current = address;
                for (var hop = 0; ; hop++)
                {
                    if (!cover || hop == 0)
                        await _throttle.WaitAsync(cancel).ConfigureAwait(false);
                    timeout.CancelAfter(Timeout);
                    using var request = new HttpRequestMessage(HttpMethod.Get, current);
                    request.Headers.TryAddWithoutValidation("User-Agent", cover ? _coverUserAgent : _userAgent);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(cover ? "image/*" : "application/json"));
                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                    var status = (int)response.StatusCode;
                    if (cover && status is 301 or 302 or 303 or 307 or 308)
                    {
                        var next = response.Headers.Location is { } location ? new Uri(current, location) : null;
                        if (next is null || !IsCoverHost(next) || hop >= MaxHops)
                            throw new MetadataException(service + " pointed somewhere unexpected.");
                        current = next;
                        continue;
                    }

                    if (response.StatusCode == HttpStatusCode.NotFound)
                        return null;
                    if (response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500)
                    {
                        busy = true;
                        if (response.Headers.RetryAfter?.Delta is { } asked && asked > wait)
                            wait = asked < TimeSpan.FromSeconds(10) ? asked : TimeSpan.FromSeconds(10);
                        break;
                    }

                    if (!response.IsSuccessStatusCode)
                        throw new MetadataException(string.Create(CultureInfo.InvariantCulture, $"{service} refused the request ({status})."));
                    await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                    return await ReadLimited(stream, max, service, timeout.Token).ConfigureAwait(false);
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

        throw new MetadataException(busy ? service + " is busy. Try again in a moment." : $"{service} couldn't be reached. Check the internet connection.", failure);
    }

    private static async Task<byte[]> ReadLimited(Stream body, int max, string service, CancellationToken cancel)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[32 * 1024];
        int read;
        while ((read = await body.ReadAsync(chunk, cancel).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > max)
                throw new MetadataException(service + " sent too much.");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    // ---- reading the answer ------------------------------------------------------------------------

    internal static IReadOnlyList<MetadataCandidate> Parse(JsonElement root)
    {
        var found = new List<MetadataCandidate>();
        foreach (var recording in Items(root, "recordings"))
        {
            var id = Text(recording, "id");
            var title = Text(recording, "title");
            if (id.Length == 0 || title.Length == 0)
                continue;
            var (credits, artistText) = Credits(recording);
            var releases = Items(recording, "releases").Select(r => Release(r, artistText)).Where(r => r.Title.Length > 0).ToList();
            var length = Number(recording, "length");
            // A length of a few milliseconds is a placeholder, not a length.
            found.Add(new MetadataCandidate(id, title, Text(recording, "disambiguation"), credits, artistText, length >= 1000 ? length : 0,
                Text(recording, "first-release-date") is { Length: > 0 } first ? first : null, releases));
        }

        return found;
    }

    private static (List<ArtistCredit> Credits, string Text) Credits(JsonElement element)
    {
        var credits = new List<ArtistCredit>();
        var joined = new StringBuilder();
        foreach (var credit in Items(element, "artist-credit"))
        {
            var name = Text(credit, "name");
            var others = new List<string>();
            if (credit.TryGetProperty("artist", out var artist) && artist.ValueKind == JsonValueKind.Object)
            {
                var main = Text(artist, "name");
                if (name.Length == 0)
                    name = main;
                else if (main.Length > 0 && main != name)
                    others.Add(main);
                others.AddRange(Items(artist, "aliases").Select(a => Text(a, "name")).Where(a => a.Length > 0 && a != name).Take(40));
            }

            if (name.Length == 0)
                continue;
            credits.Add(new ArtistCredit(name, others.Distinct().ToList()));
            joined.Append(name).Append(Text(credit, "joinphrase", trim: false));
        }

        return (credits, joined.ToString().Trim());
    }

    private static MetadataRelease Release(JsonElement release, string trackArtist)
    {
        var (_, albumArtist) = Credits(release);
        string? group = null, primary = null;
        var secondary = new List<string>();
        if (release.TryGetProperty("release-group", out var grouped) && grouped.ValueKind == JsonValueKind.Object)
        {
            group = Text(grouped, "id") is { Length: > 0 } g ? g : null;
            primary = Text(grouped, "primary-type") is { Length: > 0 } p ? p : null;
            if (grouped.TryGetProperty("secondary-types", out var types) && types.ValueKind == JsonValueKind.Array)
                secondary.AddRange(types.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => Clip(t.GetString())).Take(8));
        }

        uint track = 0;
        foreach (var medium in Items(release, "media"))
        {
            foreach (var entry in Items(medium, "track"))
            {
                // "A4" on a record is no track number; the place on the disc stands in for it.
                if (!uint.TryParse(Text(entry, "number"), NumberStyles.None, CultureInfo.InvariantCulture, out track))
                    track = Number(medium, "track-offset") is var offset && offset >= 0 && medium.TryGetProperty("track-offset", out _) ? (uint)offset + 1 : 0;
                break;
            }

            break;
        }

        return new MetadataRelease(Text(release, "id"), group, Text(release, "title"), albumArtist.Length > 0 ? albumArtist : trackArtist,
            Text(release, "date") is { Length: > 0 } date ? date : null, primary, secondary, Text(release, "status") == "Official", track < 10000 ? track : 0);
    }

    private static IEnumerable<JsonElement> Items(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).Take(1000)
            : [];

    private static string Text(JsonElement element, string name, bool trim = true) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? Clip(value.GetString(), trim) : string.Empty;

    // These end up in tags and file names: no control characters, and no essays.
    private static string Clip(string? text, bool trim = true)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var clean = string.Concat(text.Where(c => !char.IsControl(c)));
        if (clean.Length > MaxText)
            clean = clean[..(char.IsHighSurrogate(clean[MaxText - 1]) ? MaxText - 1 : MaxText)];
        return trim ? clean.Trim() : clean;
    }

    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 0 ? number : 0;
}

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Pardeh.Core.LyricsKit;

/// <summary>
/// What an online lookup sends: the title, artist, album and length of the track. Nothing else (no file name, no folder, no
/// device or user name) leaves the machine.
/// </summary>
public sealed record LyricsQuery(string Title, string Artist, string? Album, int DurationSeconds);

/// <summary>A website or service that can supply lyrics for a track. More than one can be registered with <see cref="LyricsService"/>.</summary>
public interface ILyricsProvider
{
    string Name { get; }

    /// <summary>The lyrics, or null when the service has none. Throws (HttpRequestException, TimeoutException ...) when it could not be asked: such a failure is not remembered as "not found".</summary>
    Task<Lyrics?> FindAsync(LyricsQuery query, CancellationToken cancel);
}

/// <summary>Spaces requests at least <c>interval</c> apart (1 request per second by default), shared by everything that talks to the network.</summary>
public sealed class RequestThrottle
{
    private readonly object _gate = new();
    private readonly TimeSpan _interval;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private DateTimeOffset _next = DateTimeOffset.MinValue;

    public RequestThrottle(TimeSpan? interval = null, Func<DateTimeOffset>? now = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _interval = interval ?? TimeSpan.FromSeconds(1);
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? Task.Delay;
    }

    /// <summary>Completes when the caller may send a request (immediately for the first one).</summary>
    public async Task WaitAsync(CancellationToken cancel)
    {
        TimeSpan wait;
        lock (_gate)
        {
            var now = _now();
            var slot = _next > now ? _next : now;
            _next = slot + _interval;
            wait = slot - now;
        }

        if (wait > TimeSpan.Zero)
            await _delay(wait, cancel).ConfigureAwait(false);
    }
}

/// <summary>
/// LRCLIB (https://lrclib.net): free, open, no API key. <c>/api/get</c> with the track, artist, album and length, then
/// <c>/api/search</c> as a fallback. Each request has an 8 second timeout and one retry, goes through the shared
/// <see cref="RequestThrottle"/> and carries only the track fields and the generic User-Agent <c>Pardeh/1.0</c>.
/// </summary>
public sealed class LrcLibProvider : ILyricsProvider
{
    public const string DefaultBaseAddress = "https://lrclib.net";
    public const string UserAgent = "Pardeh/1.0";
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    private readonly HttpClient _http;
    private readonly RequestThrottle _throttle;
    private readonly Uri _baseAddress;
    private readonly TimeSpan _timeout;

    public LrcLibProvider(HttpClient http, RequestThrottle? throttle = null, string baseAddress = DefaultBaseAddress, TimeSpan? timeout = null)
    {
        _http = http;
        _throttle = throttle ?? new RequestThrottle();
        _baseAddress = new Uri(baseAddress.TrimEnd('/') + "/");
        _timeout = timeout ?? RequestTimeout;
    }

    public string Name => "LRCLIB";

    public async Task<Lyrics?> FindAsync(LyricsQuery query, CancellationToken cancel)
    {
        if (string.IsNullOrWhiteSpace(query.Title) || string.IsNullOrWhiteSpace(query.Artist))
            return null;

        if (!string.IsNullOrWhiteSpace(query.Album) && query.DurationSeconds > 0)
        {
            var url = new Uri(_baseAddress, "api/get?" + string.Join('&',
                Pair("track_name", query.Title), Pair("artist_name", query.Artist), Pair("album_name", query.Album),
                Pair("duration", query.DurationSeconds.ToString(CultureInfo.InvariantCulture))));
            using var exact = await GetJsonAsync(url, cancel).ConfigureAwait(false);
            if (exact is not null && ToLyrics(exact.RootElement, query) is { } found)
                return found;
        }

        var search = new Uri(_baseAddress, "api/search?" + Pair("q", query.Artist + " " + query.Title));
        using var results = await GetJsonAsync(search, cancel).ConfigureAwait(false);
        if (results is null || results.RootElement.ValueKind != JsonValueKind.Array)
            return null;
        return PickBest(results.RootElement, query);
    }

    private static string Pair(string name, string value) => name + "=" + Uri.EscapeDataString(value);

    /// <summary>The search hit closest to the query: an equal title, a length within 3 seconds, synchronised lyrics first.</summary>
    private static Lyrics? PickBest(JsonElement hits, LyricsQuery query)
    {
        var title = LyricsTrackKey.Normalize(query.Title);
        Lyrics? best = null;
        var bestScore = int.MinValue;
        foreach (var hit in hits.EnumerateArray().Take(25))
        {
            if (hit.ValueKind != JsonValueKind.Object || ToLyrics(hit, query) is not { } lyrics)
                continue;
            var score = 0;
            if (LyricsTrackKey.Normalize(String(hit, "trackName")) == title)
                score += 4;
            if (query.DurationSeconds > 0 && hit.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number)
            {
                var diff = Math.Abs(d.GetDouble() - query.DurationSeconds);
                if (diff > 3)
                    continue;
                score += 2;
            }

            if (lyrics.Synced)
                score += 3;
            if (score > bestScore)
            {
                best = lyrics;
                bestScore = score;
            }
        }

        return best;
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Lyrics? ToLyrics(JsonElement element, LyricsQuery query)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        if (element.TryGetProperty("instrumental", out var instrumental) && instrumental.ValueKind == JsonValueKind.True)
            return null;
        var synced = LrcParser.Parse(String(element, "syncedLyrics"));
        var parsed = synced is { Synced: true } ? synced : LrcParser.Parse(String(element, "plainLyrics"));
        return parsed?.WithInfo(String(element, "trackName") ?? query.Title, String(element, "artistName") ?? query.Artist, String(element, "albumName") ?? query.Album);
    }

    /// <summary>GETs and parses JSON: null on 404, throws when the service cannot be reached (after one retry).</summary>
    private async Task<JsonDocument?> GetJsonAsync(Uri url, CancellationToken cancel)
    {
        Exception? failure = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await _throttle.WaitAsync(cancel).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(_timeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Pardeh", "1.0"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null;
                if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    failure = new HttpRequestException($"LRCLIB answered {(int)response.StatusCode}");
                    continue;
                }

                response.EnsureSuccessStatusCode();
                await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                var data = await ReadBodyLimited(body, timeout.Token).ConfigureAwait(false);
                return JsonDocument.Parse(data);
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
            {
                failure = new TimeoutException("LRCLIB did not answer in time");
            }
            catch (HttpRequestException ex) when (ex.StatusCode is not { } code || (int)code >= 500 || code == HttpStatusCode.TooManyRequests)
            {
                failure = ex;
            }
            catch (JsonException ex)
            {
                failure = ex;
            }
        }

        throw failure ?? new HttpRequestException("LRCLIB request failed");
    }

    /// <summary>Reads a response body, at most a few MB (a bigger answer is not lyrics).</summary>
    private static async Task<byte[]> ReadBodyLimited(Stream body, CancellationToken cancel)
    {
        const int Max = 4 * 1024 * 1024;
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await body.ReadAsync(chunk, cancel).ConfigureAwait(false)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > Max)
                throw new HttpRequestException("LRCLIB answer too large");
        }

        return buffer.ToArray();
    }
}

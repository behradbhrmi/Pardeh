using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Pardeh.Core.LyricsKit;

/// <summary>
/// A lyrics source the user added: a name and a web address with placeholders, e.g. <c>https://api.lyrics.ovh/v1/{artist}/{title}</c>.
/// Placeholders: <c>{title}</c>, <c>{artist}</c>, <c>{album}</c>, <c>{duration}</c> (seconds); each is URL-escaped.
/// </summary>
public sealed class LyricsEndpoint
{
    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>Ready-made addresses offered in the settings.</summary>
    public static readonly LyricsEndpoint[] Presets =
    [
        new() { Name = "lyrics.ovh", Url = "https://api.lyrics.ovh/v1/{artist}/{title}" },
        new() { Name = "LRCLIB search", Url = "https://lrclib.net/api/search?q={artist}%20{title}" },
    ];

    /// <summary>True for an http(s) address that names at least one placeholder.</summary>
    public static bool IsValid(string? url) =>
        Uri.TryCreate(url?.Replace("{title}", "x").Replace("{artist}", "x").Replace("{album}", "x").Replace("{duration}", "1"), UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && (url!.Contains("{title}", StringComparison.Ordinal) || url.Contains("{artist}", StringComparison.Ordinal));

    /// <summary>The address for a query (placeholders replaced by escaped values).</summary>
    public string Build(LyricsQuery query) => Url
        .Replace("{title}", Uri.EscapeDataString(query.Title))
        .Replace("{artist}", Uri.EscapeDataString(query.Artist))
        .Replace("{album}", Uri.EscapeDataString(query.Album ?? string.Empty))
        .Replace("{duration}", query.DurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>
/// Asks every enabled <see cref="LyricsEndpoint"/> in turn (they are read on each lookup, so settings changes apply at once). The answer
/// may be JSON (a <c>syncedLyrics</c>, <c>plainLyrics</c>, <c>lyrics</c>, <c>lrc</c> or <c>text</c> field anywhere in it, an array is
/// searched too) or plain / LRC text. Same limits as LRCLIB: throttled, 8 seconds, 4 MB, the generic <c>Pardeh/1.0</c> User-Agent and
/// only the placeholders the address names leave the device.
/// </summary>
public sealed class CustomLyricsProvider : ILyricsProvider
{
    private static readonly string[] SyncedFields = ["syncedLyrics", "synced", "lrc", "syncedlyrics"];
    private static readonly string[] PlainFields = ["plainLyrics", "lyrics", "lyric", "text", "plain", "lyrics_body", "content"];

    private readonly HttpClient _http;
    private readonly RequestThrottle _throttle;
    private readonly Func<IReadOnlyList<LyricsEndpoint>> _endpoints;

    public CustomLyricsProvider(HttpClient http, Func<IReadOnlyList<LyricsEndpoint>> endpoints, RequestThrottle? throttle = null)
    {
        _http = http;
        _endpoints = endpoints;
        _throttle = throttle ?? new RequestThrottle();
    }

    public string Name => "Custom sources";

    public async Task<Lyrics?> FindAsync(LyricsQuery query, CancellationToken cancel)
    {
        Exception? failure = null;
        foreach (var endpoint in _endpoints().Where(e => e.Enabled && LyricsEndpoint.IsValid(e.Url)))
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                if (await AskAsync(endpoint, query, cancel).ConfigureAwait(false) is { HasText: true } found)
                    return found;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failure = ex; // try the next source; a failure is not remembered as "not found"
            }
        }

        if (failure is not null)
            throw failure;
        return null;
    }

    private async Task<Lyrics?> AskAsync(LyricsEndpoint endpoint, LyricsQuery query, CancellationToken cancel)
    {
        await _throttle.WaitAsync(cancel).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(LrcLibProvider.RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Build(query));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Pardeh", "1.0"));
        request.Headers.Accept.ParseAdd("application/json, text/plain;q=0.9");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await body.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > 4 * 1024 * 1024)
                throw new HttpRequestException("answer too large");
        }

        var text = System.Text.Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('﻿');
        return Interpret(text, query);
    }

    /// <summary>Lyrics out of an answer: JSON fields or plain / LRC text. Null when there is nothing usable (a web page is not lyrics).</summary>
    public static Lyrics? Interpret(string answer, LyricsQuery query)
    {
        var trimmed = answer.TrimStart();
        if (trimmed.Length == 0)
            return null;
        if (trimmed[0] is '{' or '[')
        {
            try
            {
                using var json = JsonDocument.Parse(answer);
                var synced = Find(json.RootElement, SyncedFields, 0);
                var parsed = synced is null ? null : LrcParser.Parse(synced);
                if (parsed is not { Synced: true })
                    parsed = Find(json.RootElement, PlainFields, 0) is { } plain ? LrcParser.Parse(plain) : parsed;
                return parsed?.WithInfo(query.Title, query.Artist, query.Album);
            }
            catch (JsonException) when (trimmed[0] == '[')
            {
                // "[00:12.00] words" is LRC text, not a JSON array: read it as text below.
            }
            catch (JsonException)
            {
                return null;
            }
        }

        if (trimmed.StartsWith('<'))
            return null; // HTML page
        return LrcParser.Parse(answer)?.WithInfo(query.Title, query.Artist, query.Album);
    }

    private static string? Find(JsonElement element, string[] names, int depth)
    {
        if (depth > 4)
            return null;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var name in names)
                    foreach (var property in element.EnumerateObject())
                        if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String
                            && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                            return property.Value.GetString();
                foreach (var property in element.EnumerateObject())
                    if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array && Find(property.Value, names, depth + 1) is { } nested)
                        return nested;
                return null;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray().Take(10))
                    if (Find(item, names, depth + 1) is { } found)
                        return found;
                return null;
            default:
                return null;
        }
    }
}

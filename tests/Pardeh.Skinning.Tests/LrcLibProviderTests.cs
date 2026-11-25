using System.Net;
using System.Text;
using Pardeh.Core.LyricsKit;

namespace Pardeh.Skinning.Tests;

/// <summary>LRCLIB with a fake HTTP handler (no network). The real service runs only with PARDEH_NET_TESTS=1.</summary>
public sealed class LrcLibProviderTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requests.Add(request);
            return await respond(request, Requests.Count);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    private static readonly RequestThrottle NoWait = new(TimeSpan.Zero);

    private static LrcLibProvider Provider(HttpMessageHandler handler, TimeSpan? timeout = null) =>
        new(new HttpClient(handler), NoWait, timeout: timeout);

    private static readonly LyricsQuery Query = new("Never Gonna Give You Up", "Rick Astley", "Whenever You Need Somebody", 213);

    private const string Hit =
        """{"id":1,"trackName":"Never Gonna Give You Up","artistName":"Rick Astley","albumName":"Whenever You Need Somebody","duration":213,"instrumental":false,"plainLyrics":"We're no strangers\nto love","syncedLyrics":"[00:18.00] We're no strangers\n[00:22.00] to love"}""";

    [Fact]
    public async Task Gets_synced_lyrics_with_only_the_four_track_fields()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Json(Hit)));
        var lyrics = await Provider(handler).FindAsync(Query, CancellationToken.None);

        Assert.True(lyrics!.Synced);
        Assert.Equal(["We're no strangers", "to love"], lyrics.Lines.Select(l => l.Text));
        Assert.Equal("Rick Astley", lyrics.Artist);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("lrclib.net", request.RequestUri!.Host);
        Assert.Equal("https", request.RequestUri.Scheme);
        Assert.Equal("/api/get", request.RequestUri.AbsolutePath);
        Assert.Equal(
            "track_name=Never%20Gonna%20Give%20You%20Up&artist_name=Rick%20Astley&album_name=Whenever%20You%20Need%20Somebody&duration=213",
            request.RequestUri.Query.TrimStart('?'));
    }

    [Fact]
    public async Task Sends_a_generic_user_agent_and_nothing_personal()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Json(Hit)));
        await Provider(handler).FindAsync(Query, CancellationToken.None);

        var request = handler.Requests[0];
        Assert.Equal("Pardeh/1.0", request.Headers.UserAgent.ToString());
        Assert.Null(request.Headers.Authorization);
        Assert.Null(request.Headers.Referrer);
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.False(request.Headers.Contains("From"));
        Assert.Equal(["Accept", "User-Agent"], request.Headers.Select(h => h.Key).Order());
        Assert.Null(request.Content);
    }

    [Fact]
    public async Task Falls_back_to_a_search_when_the_exact_lookup_finds_nothing()
    {
        var handler = new FakeHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/api/get"
            ? Status(HttpStatusCode.NotFound)
            : Json("[" + Hit.Replace("\"duration\":213", "\"duration\":214") + "]")));
        var lyrics = await Provider(handler).FindAsync(Query, CancellationToken.None);

        Assert.True(lyrics!.Synced);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/api/search", handler.Requests[1].RequestUri!.AbsolutePath);
        Assert.Equal("q=Rick%20Astley%20Never%20Gonna%20Give%20You%20Up", handler.Requests[1].RequestUri!.Query.TrimStart('?'));
    }

    [Fact]
    public async Task Without_an_album_or_length_it_goes_straight_to_the_search()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Json("[" + Hit + "]")));
        Assert.NotNull(await Provider(handler).FindAsync(Query with { Album = null }, CancellationToken.None));
        Assert.NotNull(await Provider(handler).FindAsync(Query with { DurationSeconds = 0 }, CancellationToken.None));
        Assert.All(handler.Requests, r => Assert.Equal("/api/search", r.RequestUri!.AbsolutePath));
    }

    [Fact]
    public async Task The_search_picks_the_hit_with_the_right_length_and_synced_text()
    {
        var wrongLength = Hit.Replace("\"duration\":213", "\"duration\":300").Replace("We're no strangers", "wrong length");
        var plainOnly = Hit.Replace("\"duration\":213", "\"duration\":212").Replace("\"syncedLyrics\":", "\"x\":").Replace("We're no strangers", "plain only");
        var best = Hit.Replace("\"duration\":213", "\"duration\":214").Replace("We're no strangers", "the best");
        var handler = new FakeHandler((_, _) => Task.FromResult(Json($"[{wrongLength},{plainOnly},{best}]")));

        var lyrics = await Provider(handler).FindAsync(Query with { Album = null }, CancellationToken.None);

        Assert.StartsWith("the best", lyrics!.Lines[0].Text);
    }

    [Fact]
    public async Task Plain_lyrics_come_back_unsynchronised_and_instrumentals_as_nothing()
    {
        var plain = new FakeHandler((_, _) => Task.FromResult(Json("""{"plainLyrics":"one\ntwo","syncedLyrics":null,"instrumental":false}""")));
        var lyrics = await Provider(plain).FindAsync(Query, CancellationToken.None);
        Assert.False(lyrics!.Synced);
        Assert.Equal(["one", "two"], lyrics.Lines.Select(l => l.Text));

        var instrumental = new FakeHandler((_, _) => Task.FromResult(Json("""{"plainLyrics":null,"syncedLyrics":null,"instrumental":true}""")));
        Assert.Null(await Provider(instrumental).FindAsync(Query, CancellationToken.None));
    }

    [Fact]
    public async Task Not_found_everywhere_gives_null_without_retrying()
    {
        var handler = new FakeHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/api/get" ? Status(HttpStatusCode.NotFound) : Json("[]")));
        Assert.Null(await Provider(handler).FindAsync(Query, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Retries_once_after_a_server_error()
    {
        var handler = new FakeHandler((_, n) => Task.FromResult(n == 1 ? Status(HttpStatusCode.ServiceUnavailable) : Json(Hit)));
        Assert.NotNull(await Provider(handler).FindAsync(Query, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Gives_up_after_one_retry_and_reports_the_failure()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Status(HttpStatusCode.InternalServerError)));
        await Assert.ThrowsAsync<HttpRequestException>(() => Provider(handler).FindAsync(Query, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task A_rate_limit_answer_is_retried_once_too()
    {
        var handler = new FakeHandler((_, n) => Task.FromResult(n == 1 ? Status(HttpStatusCode.TooManyRequests) : Json(Hit)));
        Assert.NotNull(await Provider(handler).FindAsync(Query, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task A_client_error_is_not_retried()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Status(HttpStatusCode.BadRequest)));
        await Assert.ThrowsAsync<HttpRequestException>(() => Provider(handler).FindAsync(Query, CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_network_error_is_retried_once()
    {
        var handler = new FakeHandler((_, n) => n == 1 ? throw new HttpRequestException("reset") : Task.FromResult(Json(Hit)));
        Assert.NotNull(await Provider(handler).FindAsync(Query, CancellationToken.None));
    }

    [Fact]
    public async Task A_slow_server_times_out_and_is_retried_once()
    {
        var honouring = new HonouringHandler(); // never answers; only the provider's own timeout ends the wait
        await Assert.ThrowsAsync<TimeoutException>(() => Provider(honouring, TimeSpan.FromMilliseconds(80)).FindAsync(Query, CancellationToken.None));
        Assert.Equal(2, honouring.Calls);
    }

    private sealed class HonouringHandler : HttpMessageHandler
    {
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(Timeout.Infinite, cancel);
            return Json(Hit);
        }
    }

    [Fact]
    public async Task A_cancel_by_the_caller_is_not_a_failure_and_is_not_retried()
    {
        var honouring = new HonouringHandler();
        using var cancel = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(honouring).FindAsync(Query, cancel.Token));
        Assert.Equal(1, honouring.Calls);
    }

    [Fact]
    public async Task Broken_json_is_a_failure()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Json("<html>oops</html>")));
        await Assert.ThrowsAnyAsync<Exception>(() => Provider(handler).FindAsync(Query, CancellationToken.None));
    }

    [Fact]
    public async Task Needs_a_title_and_an_artist()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Json(Hit)));
        Assert.Null(await Provider(handler).FindAsync(Query with { Artist = " " }, CancellationToken.None));
        Assert.Null(await Provider(handler).FindAsync(Query with { Title = "" }, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_throttle_spaces_requests_one_second_apart()
    {
        var clock = DateTimeOffset.Parse("2026-11-18T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var waits = new List<TimeSpan>();
        var throttle = new RequestThrottle(TimeSpan.FromSeconds(1), () => clock, (wait, _) =>
        {
            waits.Add(wait);
            clock += wait;
            return Task.CompletedTask;
        });

        for (var i = 0; i < 4; i++)
            await throttle.WaitAsync(CancellationToken.None);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)], waits); // the first goes at once

        clock += TimeSpan.FromSeconds(5); // a quiet spell resets the spacing
        waits.Clear();
        await throttle.WaitAsync(CancellationToken.None);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task A_retry_waits_for_the_throttle_as_well()
    {
        var count = 0;
        var throttle = new RequestThrottle(TimeSpan.FromSeconds(1), () => DateTimeOffset.UnixEpoch, (_, _) =>
        {
            count++;
            return Task.CompletedTask;
        });
        var handler = new FakeHandler((_, n) => Task.FromResult(n == 1 ? Status(HttpStatusCode.BadGateway) : Json(Hit)));
        await new LrcLibProvider(new HttpClient(handler), throttle).FindAsync(Query, CancellationToken.None);
        Assert.Equal(1, count); // two requests at the same instant: the second had to wait
    }

    [Fact]
    public async Task The_real_service_answers()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("PARDEH_NET_TESTS") != "1", "network tests are off (PARDEH_NET_TESTS=1)");
        using var http = new HttpClient();
        var lyrics = await new LrcLibProvider(http).FindAsync(Query, CancellationToken.None);
        Assert.NotNull(lyrics);
    }
}

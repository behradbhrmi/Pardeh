using System.Net;
using System.Text;
using Pardeh.Core.Library;
using Pardeh.Core.LyricsKit;
using Pardeh.Core.Playback;
using Pardeh.Skinning.Imaging;
using Pardeh.UI;
using Pardeh.UI.FullMode;

namespace Pardeh.Skinning.Tests;

/// <summary>Custom lyrics sources, restoring the last queue paused, the queue-order sort, the logo in the skin colour, the keyboard.</summary>
public sealed class RoundTwoTests
{
    // ---- Custom lyrics sources ------------------------------------------------------------------------

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    private static readonly LyricsQuery Query = new("Dance", "TM Bax", "Sexy Dance", 177);

    [Fact]
    public void Endpoint_addresses_need_http_and_a_placeholder()
    {
        Assert.True(LyricsEndpoint.IsValid("https://api.lyrics.ovh/v1/{artist}/{title}"));
        Assert.True(LyricsEndpoint.IsValid("http://example.com/l?q={title}"));
        Assert.False(LyricsEndpoint.IsValid("https://example.com/static"));
        Assert.False(LyricsEndpoint.IsValid("ftp://example.com/{title}"));
        Assert.False(LyricsEndpoint.IsValid("not a url {title}"));
        Assert.False(LyricsEndpoint.IsValid(""));
        Assert.All(LyricsEndpoint.Presets, p => Assert.True(LyricsEndpoint.IsValid(p.Url)));
    }

    [Fact]
    public void The_address_gets_escaped_values()
    {
        var url = new LyricsEndpoint { Url = "https://x.test/{artist}/{title}?a={album}&d={duration}" }.Build(new LyricsQuery("A & B", "Zed/Yan", null, 90));
        Assert.Equal("https://x.test/Zed%2FYan/A%20%26%20B?a=&d=90", url);
    }

    [Fact]
    public void Json_text_and_lrc_answers_are_understood()
    {
        Assert.Equal(["one", "two"], CustomLyricsProvider.Interpret("""{"lyrics":"one\ntwo"}""", Query)!.Lines.Select(l => l.Text));
        var synced = CustomLyricsProvider.Interpret("""[{"id":1,"plainLyrics":"p","syncedLyrics":"[00:01.00] a\n[00:02.00] b"}]""", Query)!;
        Assert.True(synced.Synced);
        Assert.Equal(["a", "b"], synced.Lines.Select(l => l.Text));
        Assert.True(CustomLyricsProvider.Interpret("[00:05.00] hello\n[00:07.00] world", Query)!.Synced);
        Assert.Equal("la la", CustomLyricsProvider.Interpret("la la", Query)!.Lines[0].Text);
        Assert.Null(CustomLyricsProvider.Interpret("<html><body>Not found</body></html>", Query));
        Assert.Null(CustomLyricsProvider.Interpret("""{"error":"No lyrics found"}""", Query));
        Assert.Null(CustomLyricsProvider.Interpret("   ", Query));
        Assert.Null(CustomLyricsProvider.Interpret("{ broken json", Query));
    }

    [Fact]
    public async Task Sources_are_asked_in_order_skipping_disabled_and_failing_ones()
    {
        var handler = new FakeHandler(r => r.RequestUri!.Host switch
        {
            "down.test" => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            "empty.test" => new HttpResponseMessage(HttpStatusCode.NotFound),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"lyrics":"found it"}""", Encoding.UTF8, "application/json") },
        });
        List<LyricsEndpoint> endpoints =
        [
            new() { Name = "off", Url = "https://off.test/{title}", Enabled = false },
            new() { Name = "down", Url = "https://down.test/{title}" },
            new() { Name = "empty", Url = "https://empty.test/{title}" },
            new() { Name = "good", Url = "https://good.test/{artist}/{title}" },
            new() { Name = "never", Url = "https://never.test/{title}" },
        ];
        var provider = new CustomLyricsProvider(new HttpClient(handler), () => endpoints, new RequestThrottle(TimeSpan.Zero));

        var lyrics = await provider.FindAsync(Query, CancellationToken.None);

        Assert.Equal("found it", lyrics!.Lines[0].Text);
        Assert.Equal(["down.test", "empty.test", "good.test"], handler.Requests.Select(r => r.Host));
        Assert.Equal("/TM%20Bax/Dance", handler.Requests[2].AbsolutePath);
    }

    [Fact]
    public async Task A_failure_is_reported_so_it_is_not_remembered_as_not_found()
    {
        var provider = new CustomLyricsProvider(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError))),
            () => [new LyricsEndpoint { Url = "https://down.test/{title}" }], new RequestThrottle(TimeSpan.Zero));
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.FindAsync(Query, CancellationToken.None));

        var none = new CustomLyricsProvider(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))),
            () => [new LyricsEndpoint { Url = "https://empty.test/{title}" }], new RequestThrottle(TimeSpan.Zero));
        Assert.Null(await none.FindAsync(Query, CancellationToken.None));
    }

    // ---- Restoring the last queue ----------------------------------------------------------------------

    private static MediaItem Item(string name) => new($"/music/{name}.mp3") { Name = name, Duration = TimeSpan.FromMinutes(3) };

    [Fact]
    public void A_restored_queue_is_loaded_paused_and_plays_on_request()
    {
        var engine = new FakeEngine();
        var player = new MediaPlayer(engine, new FakeInfoReader());

        player.RestoreQueue([Item("a"), Item("b"), Item("c")], 1, 42);

        Assert.Equal(3, player.CurrentPlaylist.Count);
        Assert.Equal("b", player.CurrentMedia!.Name);
        Assert.Equal(PlayState.Paused, player.PlayState);
        Assert.False(engine.Playing);
        Assert.Equal(42, engine.Position.TotalSeconds);

        player.Play();
        Assert.Equal(PlayState.Playing, player.PlayState);
        Assert.True(engine.Playing);
        Assert.Equal("b", player.CurrentMedia!.Name);
    }

    [Fact]
    public void Restoring_does_not_replace_something_already_playing()
    {
        var player = new MediaPlayer(new FakeEngine(), new FakeInfoReader());
        player.OpenItems([Item("now")], 0);
        player.RestoreQueue([Item("old")], 0, 0);
        Assert.Equal("now", player.CurrentMedia!.Name);
        Assert.Equal(1, player.CurrentPlaylist.Count);
    }

    // ---- Sort by queue order --------------------------------------------------------------------------

    [Fact]
    public void Queue_order_is_a_named_sort_field_that_round_trips()
    {
        Assert.Equal("Playlist queue", SortSpec.Label(SortField.QueueOrder));
        Assert.Equal(new SortSpec(SortField.QueueOrder, true), SortSpec.Parse(new SortSpec(SortField.QueueOrder, true).ToString(), new SortSpec(SortField.Title)));
    }

    // ---- The logo in the skin colour ------------------------------------------------------------------

    [Fact]
    public void The_logo_takes_the_skin_hue()
    {
        LogoTests.EnsurePlatform(); // a decode without it fails, and LogoImage remembers the failure
        var logo = LogoImage.Get(LogoVariant.Curtain);
        Assert.NotNull(logo);
        var tinted = logo!.WithHue(20, 1, 1); // an orange skin
        Assert.Same(tinted, logo.WithHue(20, 1, 1));
        Assert.Equal(logo.Pixels.Length, tinted.Pixels.Length);

        int cool = 0, coolMoved = 0;
        for (var i = 0; i < logo.Pixels.Length; i += 4)
        {
            var a = logo.Pixels[i + 3];
            if (a < 250)
                continue;
            int b = logo.Pixels[i], g = logo.Pixels[i + 1], r = logo.Pixels[i + 2];
            var same = tinted.Pixels[i] == b && tinted.Pixels[i + 1] == g && tinted.Pixels[i + 2] == r;
            if (b > r + 40) // clearly blue
            {
                cool++;
                if (!same)
                    coolMoved++;
            }
        }

        Assert.True(cool > 100, "the logo has blue pixels");
        Assert.True(coolMoved > cool * 0.95, $"blue pixels re-coloured ({coolMoved}/{cool})");
    }

    // ---- Keyboard: plain arrows ------------------------------------------------------------------------

    [Fact]
    public void Clamped_seek_steps_stay_inside_the_track()
    {
        Assert.Equal(0, MediaPlayer.ClampSeek(3, -5, 100));
        Assert.True(MediaPlayer.ClampSeek(98, 5, 100) <= 100);
        Assert.Equal(35, MediaPlayer.ClampSeek(30, 5, 100));
    }

    [Fact]
    public void Embedded_subtitle_events_lose_their_styling_and_keep_line_breaks()
    {
        Assert.Equal("Hello\nthere", Subtitles.TextOfAssEvent(@"12,0,Default,,0,0,0,,{\an8}Hello\Nthere"));
        Assert.Equal("a, b, c", Subtitles.TextOfAssEvent("1,0,Default,,0,0,0,,a, b, c")); // commas inside the text stay
        Assert.Equal("italic", Subtitles.CleanText("<i>italic</i>"));
        Assert.Null(Subtitles.FromCues("empty", []));
        var subtitles = Subtitles.FromCues("English", [new SubtitleCue(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), "hi")])!;
        Assert.Equal("hi", subtitles.TextAt(TimeSpan.FromSeconds(3)));
        Assert.Null(subtitles.TextAt(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Aspect_ratio_cycles_through_every_shape_and_ignores_unknown_keys()
    {
        var video = new VideoSettings();
        Assert.Equal("auto", video.Aspect);
        var seen = new List<string>();
        for (var i = 0; i < VideoSettings.Aspects.Length; i++)
        {
            video.StepAspect(1);
            seen.Add(video.Aspect);
        }

        Assert.Equal("auto", video.Aspect); // a full cycle is back where it started
        Assert.Equal(VideoSettings.Aspects.Length, seen.Distinct().Count());
        video.Aspect = "no-such-shape";
        Assert.Equal("auto", video.Aspect);
        video.Aspect = "16:9";
        video.StepAspect(-1);
        Assert.Equal("auto", video.Aspect);
    }
}

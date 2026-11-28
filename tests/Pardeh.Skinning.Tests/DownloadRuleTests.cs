using System.Net;
using System.Text;
using Pardeh.Core.Cleanup;
using Pardeh.Core.Downloads;

namespace Pardeh.Skinning.Tests;

public sealed class DownloadRuleTests
{
    private static string? Why(string address, DownloadKind kind = DownloadKind.PastedLink) => new DownloadRules().WhyRefused(new Uri(address), kind);

    [Theory]
    [InlineData("https://example.org/music/song.mp3")]
    [InlineData("https://upload.wikimedia.org/wikipedia/commons/1/15/Song.ogg")]
    [InlineData("https://archive.org/download/x/y.mp3")]
    [InlineData("https://notyoutube.com/song.mp3")]
    public void An_ordinary_https_address_is_accepted(string address) => Assert.Null(Why(address));

    [Theory]
    [InlineData("http://example.org/song.mp3", "https")]
    [InlineData("ftp://example.org/song.mp3", "https")]
    [InlineData("file:///C:/Windows/win.ini", "https")]
    [InlineData("https://user:secret@example.org/song.mp3", "password")]
    [InlineData("https://example.org:8443/song.mp3", "port")]
    [InlineData("https://localhost/song.mp3", "this device")]
    [InlineData("https://127.0.0.1/song.mp3", "this device")]
    [InlineData("https://[::1]/song.mp3", "this device")]
    [InlineData("https://192.168.1.1/song.mp3", "this device")]
    [InlineData("https://2130706433/song.mp3", "this device")]
    [InlineData("https://0x7f.0.0.1/song.mp3", "this device")]
    [InlineData("https://8.8.8.8/song.mp3", "this device")] // no bare addresses at all
    [InlineData("https://router/song.mp3", "this device")]
    [InlineData("https://nas.local/song.mp3", "this device")]
    public void Anything_else_is_refused_with_the_reason(string address, string reason) => Assert.Contains(reason, Why(address));

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=x")]
    [InlineData("https://youtu.be/x")]
    [InlineData("https://rr3---sn-abc.googlevideo.com/videoplayback?x=1")]
    [InlineData("https://soundcloud.com/a/b")]
    [InlineData("https://cf-media.sndcdn.com/x.128.mp3")]
    [InlineData("https://open.spotify.com/track/x")]
    [InlineData("https://p.scdn.co/mp3-preview/x")]
    [InlineData("https://audio-ssl.itunes.apple.com/x.m4a")]
    [InlineData("https://cdns-preview-a.dzcdn.net/x.mp3")]
    [InlineData("https://t4.bcbits.com/stream/x")]
    [InlineData("https://www.radiojavan.com/mp3s/mp3/x")]
    [InlineData("https://SoundCloud.com./x")]
    public void Video_and_streaming_services_are_refused(string address) => Assert.Contains("streaming service", Why(address));

    [Fact]
    public void A_catalog_file_may_only_come_from_the_catalogs_file_host()
    {
        Assert.Null(Why("https://upload.wikimedia.org/a/b.ogg", DownloadKind.Catalog));
        Assert.NotNull(Why("https://commons.wikimedia.org/a/b.ogg", DownloadKind.Catalog));
        Assert.NotNull(Why("https://upload.wikimedia.org.example.org/a/b.ogg", DownloadKind.Catalog));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.10.34.36")]
    [InlineData("93.184.216.34,192.168.0.2")] // one private address among them is enough
    [InlineData("")]
    public async Task A_name_that_leads_to_this_network_is_refused(string resolved)
    {
        var rules = new DownloadRules { Resolve = (_, _) => Task.FromResult(resolved.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(IPAddress.Parse).ToArray()) };

        await Assert.ThrowsAsync<DownloadException>(() => rules.CheckAsync(new Uri("https://sneaky.example.org/a.mp3"), DownloadKind.PastedLink, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("198.18.0.102")]
    [InlineData("198.19.255.1")]
    public async Task A_tunnels_stand_in_answer_to_a_name_is_accepted_but_not_when_written_out(string address)
    {
        var rules = new DownloadRules { Resolve = (_, _) => Task.FromResult(new[] { IPAddress.Parse(address) }) };

        await rules.CheckAsync(new Uri("https://music.example.org/a.mp3"), DownloadKind.PastedLink, TestContext.Current.CancellationToken);

        Assert.Contains("this device", rules.WhyRefused(new Uri($"https://{address}/a.mp3"), DownloadKind.PastedLink));
        Assert.False(Pardeh.Core.Downloads.NetworkRules.IsPublicAddress(IPAddress.Parse(address)));
    }

    [Fact]
    public async Task A_stand_in_answer_does_not_cover_for_a_private_one()
    {
        var rules = new DownloadRules { Resolve = (_, _) => Task.FromResult(new[] { IPAddress.Parse("198.18.0.5"), IPAddress.Parse("192.168.0.2") }) };

        await Assert.ThrowsAsync<DownloadException>(
            () => rules.CheckAsync(new Uri("https://music.example.org/a.mp3"), DownloadKind.PastedLink, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_name_that_changes_between_the_check_and_the_connection_is_still_refused()
    {
        var looks = 0;
        var rules = new DownloadRules
        {
            UseProxy = false,
            Resolve = (_, _) => Task.FromResult(new[] { IPAddress.Parse(++looks == 1 ? "93.184.216.34" : "127.0.0.1") }),
        };
        var engine = new DownloadEngine(rules.CreateClient(), rules, new AudioCheck(() => true, (_, _) => null), new Pardeh.Nava.TagEditor(), "x");

        var refused = await Assert.ThrowsAsync<DownloadException>(
            () => engine.LookAsync(new Uri("https://rebind.example.org/a.mp3"), DownloadKind.PastedLink, TestContext.Current.CancellationToken));

        Assert.Contains("this device or its network", refused.Message);
        Assert.Equal(2, looks);
    }

    private sealed class Recorder : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Asked = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request);
            return Task.FromResult(Asked.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://files.example.org/real.mp3") } }
                : new HttpResponseMessage(HttpStatusCode.Forbidden));
        }
    }

    [Fact]
    public async Task Only_Wikimedia_gets_the_project_address_and_a_refusal_ends_it()
    {
        var web = new Recorder();
        var rules = new DownloadRules { Resolve = (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }) };
        var engine = new DownloadEngine(new HttpClient(web), rules, new AudioCheck(() => true, (_, _) => null), new Pardeh.Nava.TagEditor(),
            "Pardeh/9 (https://example.org/project)");

        var refused = await Assert.ThrowsAsync<DownloadException>(
            () => engine.LookAsync(new Uri("https://upload.wikimedia.org/a.mp3"), DownloadKind.PastedLink, TestContext.Current.CancellationToken));

        Assert.Contains("does not allow this download (403)", refused.Message);
        Assert.False(refused.CanResume);
        Assert.Equal(2, web.Asked.Count); // the redirect, and one refusal: no second try another way
        Assert.Equal("Pardeh/9 (https://example.org/project)", web.Asked[0].Headers.UserAgent.ToString());
        Assert.Equal("Pardeh/1.0", web.Asked[1].Headers.UserAgent.ToString());
        Assert.All(web.Asked, request => Assert.All(["Cookie", "Referer", "Authorization", "Origin"], name => Assert.False(request.Headers.Contains(name))));
    }

    [Theory]
    [InlineData("https://creativecommons.org/licenses/by-sa/4.0")]
    [InlineData("Some Maker. CC BY 3.0. https://creativecommons.org/licenses/by/3.0/")]
    [InlineData("Maker. CC0. https://creativecommons.org/publicdomain/zero/1.0/")]
    [InlineData("https://commons.wikimedia.org/wiki/File:For_Elise_(F%C3%BCr_Elise)_192kbps.ogg")]
    public void Clean_up_leaves_licences_and_file_pages_alone(string text) => Assert.False(JunkText.Clean(text).Changed);

    [Fact]
    public void Clean_up_still_takes_out_website_text_next_to_a_licence()
    {
        var result = JunkText.Clean("https://creativecommons.org/licenses/by/4.0 | www.best-music.ir");

        Assert.Equal("https://creativecommons.org/licenses/by/4.0", result.Text);
    }

    // ---- what a file is ----------------------------------------------------------------------------

    private static Sniffed Look(string start) => AudioSniffer.Look(Encoding.Latin1.GetBytes(start.PadRight(64, '\0')));

    [Theory]
    [InlineData("fLaC\0\0\0\u0022", ".flac")]
    [InlineData("OggS\0\u0002............................\u0001vorbis", ".ogg")]
    [InlineData("OggS\0\u0002............................OpusHead", ".opus")]
    [InlineData("RIFF\0\0\0\0WAVEfmt ", ".wav")]
    [InlineData("FORM\0\0\0\0AIFFCOMM", ".aiff")]
    [InlineData("\0\0\0\u0020ftypM4A ", ".m4a")]
    [InlineData("\u00FF\u00FB\u0090\u0064", ".mp3")]
    [InlineData("\u00FF\u00F1\u0050\u0080", ".aac")]
    [InlineData("ID3\u0004\0\0\0\0\0\0\u00FF\u00FB\u0090\u0064", ".mp3")]
    [InlineData("ID3\u0004\0\0\0\0\0\0fLaC", ".flac")]
    public void Audio_is_known_by_its_first_bytes(string start, string extension)
    {
        var found = Look(start);

        Assert.Equal(SniffedKind.Audio, found.Kind);
        Assert.Equal(extension, found.Extension);
    }

    [Theory]
    [InlineData("<!DOCTYPE html><html>", SniffedKind.WebPage)]
    [InlineData("\u00EF\u00BB\u00BF  <html><head>", SniffedKind.WebPage)]
    [InlineData("#EXTM3U\n#EXT-X-STREAM-INF", SniffedKind.Playlist)]
    [InlineData("[playlist]\nFile1=x", SniffedKind.Playlist)]
    [InlineData("<?xml version=\"1.0\"?><MPD xmlns=", SniffedKind.Playlist)]
    [InlineData("<?xml version=\"1.0\"?><playlist xmlns=\"http://xspf.org/ns/0/\">", SniffedKind.Playlist)]
    [InlineData("MZ\u0090\0\u0003", SniffedKind.Other)]
    [InlineData("PK\u0003\u0004", SniffedKind.Other)]
    [InlineData("\u001AE\u00DF\u00A3", SniffedKind.Other)] // Matroska and WebM
    [InlineData("OggS\0\u0002............................\u0080theora", SniffedKind.Other)]
    [InlineData("#!/bin/sh", SniffedKind.Other)]
    [InlineData("{\"error\":1}", SniffedKind.Other)]
    [InlineData("ID3\u0004\0\0\0\0\0\0MZ\u0090\0", SniffedKind.Other)]
    [InlineData("", SniffedKind.Other)]
    public void Anything_else_is_told_apart(string start, SniffedKind kind) => Assert.Equal(kind, Look(start).Kind);

    // ---- names -------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Quiet River.mp3", "Quiet River")]
    [InlineData("..\\..\\Windows\\evil.exe", "evil")]
    [InlineData("../../../etc/passwd", "passwd")]
    [InlineData("C:\\temp\\a.bat", "a")]
    [InlineData("song\u202E3pm.exe", "song3pm")]
    [InlineData("a:b*c?<d>|e\".mp3", "a b c d e")]
    [InlineData("CON.mp3", "_CON")]
    [InlineData("com1.old.mp3", "_com1.old")]
    [InlineData(".trashed-123-song.mp3", "trashed-123-song")]
    [InlineData(".pending-5-x.mp3", "pending-5-x")]
    [InlineData("name. . .", "name")]
    [InlineData("tab\there\r\nnew.mp3", "tab here new")]
    [InlineData("..", "download")]
    [InlineData("", "download")]
    [InlineData(null, "download")]
    [InlineData("می\u200Cخواهم.mp3", "می\u200Cخواهم")]
    [InlineData("Mr. Big Song", "Mr. Big Song")]
    public void A_servers_name_is_made_safe(string? suggested, string expected) => Assert.Equal(expected, DownloadNames.Stem(suggested));

    [Theory]
    [InlineData('a')]
    [InlineData('م')]
    [InlineData('漢')]
    public void A_long_name_is_cut_to_a_length_every_system_takes(char letter)
    {
        var stem = DownloadNames.Stem(new string(letter, 400) + ".mp3");

        Assert.InRange(stem.Length, 1, 100);
        Assert.True(Encoding.UTF8.GetByteCount(stem) <= 180);
    }

    [Fact]
    public void A_cut_never_splits_a_character_in_two()
    {
        var stem = DownloadNames.Stem(string.Concat(Enumerable.Repeat("\U0001F3B5", 200)));

        Assert.Equal(string.Concat(Enumerable.Repeat("\U0001F3B5", stem.Length / 2)), stem);
    }
}

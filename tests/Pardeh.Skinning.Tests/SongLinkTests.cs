using System.Net;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Pardeh.Core.Downloads;
using Pardeh.Core.LyricsKit;
using Pardeh.Core.OnlineInfo;
using Pardeh.UI.CleanupViews;
using Pardeh.UI.OnlineViews;

namespace Pardeh.Skinning.Tests;

public sealed class NetworkRuleTests
{
    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700::1111")]
    [InlineData("64:ff9b::808:808")] // 8.8.8.8 as an IPv6-only network writes it
    public void A_public_address_is_allowed(string address) => Assert.True(NetworkRules.IsPublicAddress(IPAddress.Parse(address)));

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.10.34.36")]
    [InlineData("10.0.2.2")]
    [InlineData("100.64.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.0.0.8")]
    [InlineData("192.168.1.1")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:192.168.0.1")]
    [InlineData("::127.0.0.1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2001:0:4136:e378:8000:63bf:3fff:fdd2")] // Teredo
    [InlineData("2002:7f00:1::")] // 6to4 around 127.0.0.1
    [InlineData("64:ff9b::7f00:1")]
    [InlineData("64:ff9b:1::1")]
    public void An_address_of_this_machine_or_its_network_is_refused(string address) => Assert.False(NetworkRules.IsPublicAddress(IPAddress.Parse(address)));

    [Theory]
    [InlineData("example.org", true)]
    [InlineData("sub.example.co.uk", true)]
    [InlineData("localhost", false)]
    [InlineData("localhost.", false)]
    [InlineData("router", false)]
    [InlineData("nas.local", false)]
    [InlineData("db.internal", false)]
    [InlineData("box.home.arpa", false)]
    [InlineData("api.localhost", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("[::1]", false)]
    public void Only_a_full_public_name_counts(string host, bool expected) => Assert.Equal(expected, NetworkRules.IsPublicName(host));

    [Theory]
    [InlineData("https://example.org/song", true)]
    [InlineData("http://example.org/song", false)]
    [InlineData("https://user:pass@example.org/", false)]
    [InlineData("https://example.org:8443/", false)]
    [InlineData("https://192.168.1.1/", false)]
    [InlineData("https://2130706433/", false)]
    [InlineData("https://127.1/", false)]
    [InlineData("https://localhost/", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///c:/windows/win.ini", false)]
    [InlineData("intent://scan/#Intent;end", false)]
    [InlineData("", false)]
    public void Only_a_secure_public_page_may_be_opened(string address, bool expected) => Assert.Equal(expected, SafeLinks.Check(address) is not null);
}

public sealed class SongLinkTests
{
    private static string Relation(string type, string address, bool ended = false) =>
        JsonSerializer.Serialize(new Dictionary<string, object> { ["type"] = type, ["ended"] = ended, ["url"] = new { resource = address } });

    private static string Recording(params string[] relations) => $$"""{"relations":[{{string.Join(',', relations)}}]}""";

    private static string Releases(int count, params string[] releases) => $$"""{"release-count":{{count}},"releases":[{{string.Join(',', releases)}}]}""";

    private static string Release(string title, params string[] relations) => $$"""{"title":"{{title}}","relations":[{{string.Join(',', relations)}}]}""";

    private static SongLinks Parse(string recording, string releases)
    {
        using var first = JsonDocument.Parse(recording);
        using var second = JsonDocument.Parse(releases);
        return MusicBrainzProvider.ParseLinks(first.RootElement, second.RootElement);
    }

    [Fact]
    public void Links_are_grouped_by_what_they_offer_and_anything_else_is_left_out()
    {
        var links = Parse(
            Recording(Relation("streaming", "https://tidal.example/track/1"), Relation("free streaming", "https://listen.example/1"),
                Relation("license", "https://creativecommons.org/licenses/by/4.0/"), Relation("download for free", "https://artist.example/free")),
            Releases(1, Release("Morning Album", Relation("purchase for download", "https://shop.example/a"), Relation("purchase for mail-order", "https://mail.example/a"),
                Relation("discography entry", "https://fan.example/a"))));

        Assert.Equal([SongLinkKind.FreeDownloadPage, SongLinkKind.Buy, SongLinkKind.Buy, SongLinkKind.Stream, SongLinkKind.Subscription],
            links.Links.Select(l => l.Kind));
        Assert.Equal("artist.example", links.Links[0].Site);
        Assert.Null(links.Links[0].Release);
        Assert.Equal("Morning Album", links.Links[1].Release);
        Assert.Equal(0, links.Insecure);
    }

    [Fact]
    public void Insecure_unsafe_ended_and_repeated_links_are_left_out()
    {
        var shop = Enumerable.Range(0, 9).Select(i => Release("R" + i, Relation("purchase for download", "https://shop.example/album/" + i),
            Relation("purchase for download", "https://same.example/x"))).ToArray();
        var links = Parse(
            Recording(Relation("download for free", "http://old.example/free"), Relation("download for free", "https://127.0.0.1/free"),
                Relation("download for free", "https://gone.example/free", ended: true), Relation("streaming", "ftp://files.example/x")),
            Releases(350, shop));

        Assert.Equal(3, links.Links.Count(l => l.Site == "shop.example")); // a few from one shop are enough
        Assert.Single(links.Links, l => l.Site == "same.example");
        Assert.DoesNotContain(links.Links, l => l.Kind != SongLinkKind.Buy);
        Assert.Equal(3, links.Insecure);
        Assert.Equal((9, 350), (links.ReleasesSeen, links.Releases));
    }

    [Fact]
    public async Task Asking_takes_two_requests_for_that_recording_with_the_agent()
    {
        var web = new FakeWeb { Reply = (address, _) => FakeWeb.Ok(address.AbsolutePath.EndsWith("release", StringComparison.Ordinal) ? Releases(0) : Recording()) };
        var provider = new MusicBrainzProvider(new HttpClient(web), "Pardeh/9 (https://example.org)", throttle: new RequestThrottle(TimeSpan.Zero));

        var links = await provider.LinksAsync(OnlineData.RecordingId, TestContext.Current.CancellationToken);

        Assert.Empty(links.Links);
        Assert.Equal(
            [$"https://musicbrainz.org/ws/2/recording/{OnlineData.RecordingId}?inc=url-rels&fmt=json",
                $"https://musicbrainz.org/ws/2/release?recording={OnlineData.RecordingId}&inc=url-rels&limit=100&fmt=json"],
            web.Asked.Select(a => a.Address));
        Assert.All(web.Asked, a => Assert.Equal("Pardeh/9 (https://example.org)", a.Agent));
    }

    [Fact]
    public async Task Something_that_is_no_id_is_never_sent()
    {
        var web = new FakeWeb();

        var links = await new MusicBrainzProvider(new HttpClient(web), "a").LinksAsync("../artist?x=", TestContext.Current.CancellationToken);

        Assert.Empty(links.Links);
        Assert.Empty(web.Asked);
    }

    [Fact]
    public void Links_found_are_kept_with_every_row_that_has_that_recording()
    {
        var history = new LookupHistory();
        var match = HistoryMatch.From(OnlineData.Candidate());
        history.AddSearch("MusicBrainz", "Quiet River", null, [match]);
        history.AddSearch("MusicBrainz", "Quiet River", "Some Singer", [match]);
        history.AddSearch("MusicBrainz", "Other", null, []);

        history.SetLinks("MusicBrainz", OnlineData.RecordingId, [new SongLink(SongLinkKind.Buy, "https://shop.example/a")]);

        Assert.Equal(2, LookupHistory.Filter(history.Rows, null, also: LookupHistory.HasLinks).Count());
    }
}

public sealed class LinksViewTests
{
    public LinksViewTests() => TestPlatform.Ensure();

    private static void Click(Control root, string text) =>
        root.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string Texts(Control root) => string.Join("\n", root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nothing_is_asked_without_agreement_and_a_link_opens_only_through_the_app(bool narrow)
    {
        var allow = false;
        var asked = 0;
        var opened = new List<Uri>();
        var view = new LinksView(narrow ? CleanupInk.Phone : CleanupInk.Desktop, new LinksContext
        {
            Allow = () => Task.FromResult(allow),
            OpenPage = (page, _) =>
            {
                opened.Add(page);
                return Task.CompletedTask;
            },
            Fetch = (_, _) =>
            {
                asked++;
                return Task.FromResult(new SongLinks([new SongLink(SongLinkKind.FreeDownloadPage, "https://artist.example/free")], Insecure: 2));
            },
        }, new LinkTarget(OnlineData.RecordingId, "Quiet River"), () => { });

        await view.Ask();
        Assert.Equal(0, asked);

        allow = true;
        await view.Ask();
        Assert.Contains("Free download page", Texts(view));
        Assert.Contains("2 listed links are not secure", Texts(view));
        Click(view, "Open");
        Assert.Equal("https://artist.example/free", Assert.Single(opened).AbsoluteUri);
    }

    [Fact]
    public async Task Without_links_it_says_so_and_offers_the_entry_on_the_most_releases()
    {
        var asked = new List<string>();
        var view = new LinksView(CleanupInk.Desktop, new LinksContext
        {
            Allow = () => Task.FromResult(true),
            OpenPage = (_, _) => Task.CompletedTask,
            Fetch = (id, _) =>
            {
                asked.Add(id);
                return Task.FromResult(new SongLinks([]));
            },
        }, new LinkTarget("first", "Quiet River"), () => { }, new LinkTarget("most", "Quiet River (album)"));

        await view.Ask();
        Assert.Contains("does not mean", Texts(view));
        Click(view, "Try the entry on the most releases");

        Assert.Equal(["first", "most"], asked);
    }
}

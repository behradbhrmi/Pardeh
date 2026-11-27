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

/// <summary>Made-up answers in the shape the catalog's API gives.</summary>
internal static class CommonsData
{
    private const string Sha1 = "0123456789abcdef0123456789abcdef01234567";

    public static object Page(int index, string name = "Morning Tune.ogg", string license = "cc-by-sa-4.0", string licenseName = "CC BY-SA 4.0",
        string mime = "application/ogg", long size = 5000, string? url = null, string? page = null, string artist = "Some Maker", string? nonFree = null)
    {
        var meta = new Dictionary<string, object>
        {
            ["ObjectName"] = new { value = Path.GetFileNameWithoutExtension(name) },
            ["Artist"] = new { value = artist },
            ["LicenseShortName"] = new { value = licenseName },
            ["License"] = new { value = license },
            ["LicenseUrl"] = new { value = "https://creativecommons.org/licenses/by-sa/4.0" },
        };
        if (nonFree is not null)
            meta["NonFree"] = new { value = nonFree };
        return new
        {
            title = "File:" + name, index,
            imageinfo = new[]
            {
                new
                {
                    url = url ?? "https://upload.wikimedia.org/wikipedia/commons/a/ab/" + Uri.EscapeDataString(name) + "?utm_source=commons.wikimedia.org&utm_campaign=imageinfo",
                    descriptionurl = page ?? "https://commons.wikimedia.org/wiki/File:" + Uri.EscapeDataString(name),
                    size, mime, sha1 = Sha1, duration = 61.5, extmetadata = meta,
                },
            },
        };
    }

    public static string Answer(params object[] pages) => JsonSerializer.Serialize(new { batchcomplete = true, query = new { pages } });

    public static IReadOnlyList<CatalogFile> Parse(params object[] pages)
    {
        using var json = JsonDocument.Parse(Answer(pages));
        return CommonsCatalog.Parse(json.RootElement);
    }
}

public sealed class CommonsCatalogTests
{
    private const string Agent = "Pardeh/9.9 (https://example.org/project)";

    private readonly FakeWeb _web = new();
    private readonly List<TimeSpan> _waits = [];

    private CommonsCatalog Catalog() => new(new HttpClient(_web), Agent, new RequestThrottle(TimeSpan.Zero), (wait, _) =>
    {
        _waits.Add(wait);
        return Task.CompletedTask;
    });

    [Fact]
    public async Task A_search_sends_the_words_for_audio_files_with_the_contact_agent()
    {
        _web.Reply = (_, _) => FakeWeb.Ok(CommonsData.Answer(CommonsData.Page(1)));

        await Catalog().SearchAsync("  Morning & Tune ", TestContext.Current.CancellationToken);

        var (address, agent) = Assert.Single(_web.Asked);
        Assert.Equal(Agent, agent);
        var sent = new Uri(address);
        Assert.Equal(("commons.wikimedia.org", "/w/api.php"), (sent.Host, sent.AbsolutePath));
        Assert.Contains("gsrsearch=filetype%3Aaudio%20Morning%20%26%20Tune&", sent.AbsoluteUri);
    }

    [Fact]
    public async Task Without_words_nothing_is_sent()
    {
        Assert.Empty(await Catalog().SearchAsync("   ", TestContext.Current.CancellationToken));
        Assert.Empty(_web.Asked);
    }

    [Fact]
    public void A_file_comes_with_its_licence_source_and_a_clean_address_in_the_searchs_order()
    {
        var files = CommonsData.Parse(CommonsData.Page(2, "Second.ogg"), CommonsData.Page(1, "First.ogg",
            artist: "<bdi><a href=\"https://example.org\"><span title=\"x\">Some &amp; Maker</span></a></bdi><span style=\"display: none;\">hidden</span>"));

        Assert.Equal(["First", "Second"], files.Select(f => f.Title));
        var first = files[0];
        Assert.True(first.CanDownload, first.WhyNot);
        Assert.Equal("Some & Maker", first.Creator);
        Assert.Equal("CC BY-SA 4.0", first.LicenseName);
        Assert.Equal("https://upload.wikimedia.org/wikipedia/commons/a/ab/First.ogg", first.File!.AbsoluteUri);
        var offer = first.ToOffer("Wikimedia Commons")!;
        Assert.Equal((DownloadKind.Catalog, "First.ogg", 5000L), (offer.Kind, offer.FileName, offer.Size!.Value));
        Assert.Equal(40, offer.Sha1!.Length);
        Assert.Equal("https://commons.wikimedia.org/wiki/File:First.ogg", offer.Credit!.SourcePage);
        Assert.Equal("Some & Maker. CC BY-SA 4.0. https://creativecommons.org/licenses/by-sa/4.0", offer.Credit.CopyrightLine);
    }

    [Theory]
    [InlineData("pd", "Public domain", true)]
    [InlineData("cc0", "CC0", true)]
    [InlineData("cc-by-3.0", "CC BY 3.0", true)]
    [InlineData("cc-by-nc-nd-4.0", "CC BY-NC-ND 4.0", true)]
    [InlineData("gfdl", "GFDL", true)]
    [InlineData("", "", false)]
    [InlineData("", "Public domain", false)] // a name without the code is not enough
    [InlineData("fair use", "Fair use", false)]
    [InlineData("copyrighted", "All rights reserved", false)]
    public void Only_a_recognised_licence_gets_a_download(string code, string name, bool offered)
    {
        var file = Assert.Single(CommonsData.Parse(CommonsData.Page(1, license: code, licenseName: name)));

        Assert.Equal(offered, file.CanDownload);
        Assert.Equal(offered, file.ToOffer("x") is not null);
        Assert.NotNull(file.SourcePage); // the page can be opened either way
    }

    [Fact]
    public void Non_commercial_and_no_derivatives_are_told_apart()
    {
        var file = Assert.Single(CommonsData.Parse(CommonsData.Page(1, license: "cc-by-nc-nd-4.0", licenseName: "CC BY-NC-ND 4.0")));

        Assert.True(file.NonCommercial && file.NoDerivatives);
        Assert.Equal("Non-commercial use only. No changed versions may be shared.", DownloadText.Terms(file.ToOffer("x")!.Credit!));
    }

    [Fact]
    public void Odd_entries_get_no_download()
    {
        var files = CommonsData.Parse(
            CommonsData.Page(1, nonFree: "true"),
            CommonsData.Page(2, "Notes.mid", mime: "audio/midi"),
            CommonsData.Page(3, size: DownloadRules.MaxSize + 1),
            CommonsData.Page(4, url: "https://evil.example.org/a.ogg"),
            CommonsData.Page(5, url: "http://upload.wikimedia.org/a.ogg"),
            CommonsData.Page(6, page: "https://evil.example.org/wiki/File:a"));

        Assert.Equal(6, files.Count);
        Assert.All(files, f => Assert.False(f.CanDownload));
        Assert.All(files, f => Assert.NotNull(f.WhyNot));
        Assert.Null(files[5].SourcePage);
    }

    [Theory]
    [InlineData("<a href='x'>Name</a>", "Name")]
    [InlineData("<script>alert(1)</script>Name", "Name")]
    [InlineData("A‮b\u0007\n\tC", "Ab C")]
    [InlineData("&lt;b&gt;", "<b>")]
    public void Markup_becomes_plain_text(string markup, string text) => Assert.Equal(text, CommonsCatalog.Plain(markup));

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"error\":{\"code\":\"x\"}}")]
    [InlineData("[1,2]")]
    public async Task An_answer_that_is_no_result_list_is_an_error_or_empty(string body)
    {
        _web.Reply = (_, _) => FakeWeb.Ok(body);
        try
        {
            Assert.Empty(await Catalog().SearchAsync("x", TestContext.Current.CancellationToken));
        }
        catch (DownloadException)
        {
        }
    }

    [Fact]
    public async Task A_busy_catalog_is_given_the_time_its_policy_asks_for()
    {
        _web.Reply = (_, n) => n == 1 ? FakeWeb.Busy(1) : FakeWeb.Ok(CommonsData.Answer(CommonsData.Page(1)));

        Assert.Single(await Catalog().SearchAsync("x", TestContext.Current.CancellationToken));

        Assert.Equal([TimeSpan.FromSeconds(5)], _waits); // never less than five seconds
    }

    [Fact]
    public async Task A_long_wait_is_left_to_the_user()
    {
        _web.Reply = (_, _) => FakeWeb.Busy(120);

        var busy = await Assert.ThrowsAsync<DownloadException>(() => Catalog().SearchAsync("x", TestContext.Current.CancellationToken));

        Assert.Contains("busy", busy.Message);
        Assert.Single(_web.Asked);
        Assert.Empty(_waits);
    }

    [Fact]
    public async Task A_refusal_is_not_tried_again()
    {
        _web.Reply = (_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden);

        await Assert.ThrowsAsync<DownloadException>(() => Catalog().SearchAsync("x", TestContext.Current.CancellationToken));

        Assert.Single(_web.Asked);
    }

    [Fact]
    public void A_catalog_row_in_the_history_opens_only_the_catalogs_page()
    {
        var match = new HistoryMatch("https://commons.wikimedia.org/wiki/File:A.ogg", "A", "B", "CC0", 0, 0);

        Assert.Equal("commons.wikimedia.org", LookupHistory.PageOf("Wikimedia Commons", match)!.Host);
        Assert.Null(LookupHistory.PageOf("Wikimedia Commons", match with { Id = "https://evil.example.org/x" }));
    }
}

public sealed class CatalogViewTests : IDisposable
{
    private readonly DownloadFixture _f;

    public CatalogViewTests()
    {
        TestPlatform.Ensure();
        _f = new DownloadFixture();
    }

    public void Dispose() => _f.Dispose();

    private sealed class Listed(IReadOnlyList<CatalogFile> files) : IDownloadCatalog
    {
        public readonly List<string> Asked = [];

        public string Name => "Wikimedia Commons";

        public Task<IReadOnlyList<CatalogFile>> SearchAsync(string words, CancellationToken cancel)
        {
            Asked.Add(words);
            return Task.FromResult(files);
        }
    }

    private static void Click(Control root, string text) =>
        root.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string Texts(Control root) => string.Join("\n", root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));

    private static bool Has(Control root, string text) => root.GetLogicalDescendants().OfType<Button>().Any(b => b.Content as string == text);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nothing_is_sent_while_downloads_are_not_agreed_to(bool narrow)
    {
        var catalog = new Listed(CommonsData.Parse(CommonsData.Page(1)));
        var view = new CatalogView(narrow ? CleanupInk.Phone : CleanupInk.Desktop, new CatalogContext(catalog, _f.Manager) { Allow = () => Task.FromResult(false) },
            () => { });
        view.WordsBox.Text = "Morning";

        await view.Search();

        Assert.Empty(catalog.Asked);
    }

    [Fact]
    public async Task Licence_author_and_size_show_before_a_download_and_without_a_licence_there_is_no_button()
    {
        var catalog = new Listed(CommonsData.Parse(CommonsData.Page(1, license: "", licenseName: "")));
        var context = new CatalogContext(catalog, _f.Manager) { Allow = () => Task.FromResult(true) };
        var view = new CatalogView(CleanupInk.Desktop, context, () => { });
        view.WordsBox.Text = "Morning";
        await view.Search();
        Assert.False(Has(view, "Download..."));
        Assert.True(Has(view, "Source page"));
        Assert.Contains("No download here", Texts(view));

        view = new CatalogView(CleanupInk.Desktop, context with { Catalog = new Listed(CommonsData.Parse(CommonsData.Page(1))) }, () => { });
        view.WordsBox.Text = "Morning";
        await view.Search();
        Click(view, "Download...");

        var shown = Texts(view);
        Assert.Contains("Author, as stated: Some Maker", shown);
        Assert.Contains("Licence: CC BY-SA 4.0", shown);
        Assert.Contains("Morning Tune.ogg (5 KB) from upload.wikimedia.org", shown);
        Assert.True(Has(view, "Licence"));
        Assert.False(_f.Manager.IsBusy); // asking is not downloading
    }
}

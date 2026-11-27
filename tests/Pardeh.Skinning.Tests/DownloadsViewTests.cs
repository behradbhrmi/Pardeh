using System.Net;
using System.Net.Http.Headers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Pardeh.Core.Downloads;
using Pardeh.Nava;
using Pardeh.UI.CleanupViews;
using Pardeh.UI.OnlineViews;

namespace Pardeh.Skinning.Tests;

public sealed class DownloadsViewTests : IDisposable
{
    private readonly TinyServer _server = new();
    private readonly DownloadFixture _f;
    private bool _on = true;
    private string? _clipboard;
    private int _asked;
    private readonly List<Uri> _opened = [];

    public DownloadsViewTests()
    {
        TestPlatform.Ensure();
        _f = new DownloadFixture(_server);
    }

    public void Dispose()
    {
        _f.Dispose();
        _server.Dispose();
    }

    // A screen belongs to one thread, and nothing carries a test back to it after a real wait: these answers are there at once.
    private sealed class InstantWeb(Func<Uri, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request.RequestUri!));
    }

    private DownloadManager Instant(Func<Uri, HttpResponseMessage> answer)
    {
        var rules = new DownloadRules { Resolve = (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }) };
        var engine = new DownloadEngine(new HttpClient(new InstantWeb(answer)), rules,
            new AudioCheck(() => true, (path, cancel) => AudioDigest.Scan(path, 0, cancel)), new TagEditor(), DownloadFixture.Contact);
        return new DownloadManager(engine, new DownloadStore(_f.Work, Path.Combine(_f.Root, "downloads.json")), _f.Target);
    }

    private static HttpResponseMessage Audio(byte[] file) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(file) { Headers = { ContentType = new MediaTypeHeaderValue("audio/mpeg") } } };

    private DownloadsContext Context(DownloadManager? manager = null) => new(manager ?? _f.Manager)
    {
        IsOn = () => _on,
        Allow = () =>
        {
            _asked++;
            return Task.FromResult(_on);
        },
        ReadClipboard = () => Task.FromResult(_clipboard),
        OpenPage = (page, _) =>
        {
            _opened.Add(page);
            return Task.CompletedTask;
        },
    };

    private static Button? Find(Control root, string text) => root.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => b.Content as string == text);

    private static void Click(Control root, string text) => Find(root, text)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string Texts(Control root) => string.Join("\n", root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void While_downloads_are_off_there_is_no_box_and_the_clipboard_is_left_alone(bool narrow)
    {
        _on = false;
        _clipboard = "https://example.org/a.mp3";
        var view = new DownloadsView(narrow ? CleanupInk.Phone : CleanupInk.Desktop, Context(), () => { });

        Assert.Contains("Downloads are off", Texts(view));
        Assert.Empty(view.GetLogicalDescendants().OfType<TextBox>());
        Assert.NotNull(Find(view, "Turn on downloads..."));
    }

    [Fact]
    public void The_box_starts_empty_and_takes_the_clipboard_only_when_Paste_is_pressed()
    {
        _clipboard = "  https://example.org/a.mp3 \n";
        var panel = new PasteLinkPanel(CleanupInk.Desktop, Context(), () => { });
        Assert.True(string.IsNullOrEmpty(panel.AddressBox.Text));

        Click(panel, "Paste");

        Assert.Equal("https://example.org/a.mp3", panel.AddressBox.Text);
        Assert.Equal(0, _asked); // pasting sends nothing
    }

    [Theory]
    [InlineData("", "Paste an address first")]
    [InlineData("not an address", "whole address")]
    [InlineData("https://a.example.org/1.mp3 https://a.example.org/2.mp3", "whole address")]
    [InlineData("http://example.org/a.mp3", "https")]
    [InlineData("https://youtu.be/abc", "streaming service")]
    [InlineData("https://10.0.2.2/a.mp3", "this device")]
    public async Task A_refused_address_says_why_before_anything_is_asked_or_sent(string text, string reason)
    {
        var panel = new PasteLinkPanel(CleanupInk.Desktop, Context(), () => { });
        panel.AddressBox.Text = text;

        await panel.Look();

        Assert.Contains(reason, Texts(panel));
        Assert.Equal(0, _asked);
        Assert.Empty(_server.Requests);
        Assert.Null(Find(panel, "Download"));
    }

    [Fact]
    public async Task What_the_link_has_is_shown_with_the_warning_before_anything_is_saved()
    {
        Assert.SkipWhen(FfmpegTool.Tool is null, "ffmpeg is not in tools/ffmpeg (scripts/get-ffmpeg.ps1)");
        var file = _f.Audio(seconds: 3);
        var manager = Instant(address => address.AbsolutePath == "/go"
            ? new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://files.example.org/My%20Tune.mp3") } }
            : Audio(file));
        var started = 0;
        var panel = new PasteLinkPanel(CleanupInk.Phone, Context(manager), () => started++);
        panel.AddressBox.Text = "https://music.example.org/go";

        await panel.Look();

        var shown = Texts(panel);
        Assert.Contains("My Tune.mp3", shown);
        Assert.Contains("From: files.example.org", shown);
        Assert.Contains("Your link is on music.example.org, which sends you on to files.example.org.", shown);
        Assert.Contains("Type: MP3", shown);
        Assert.Contains("KB", shown);
        Assert.Contains(PasteLinkPanel.NotKnown, shown);
        Assert.Contains("mobile data", shown);
        Assert.Empty(_f.Added);
        Assert.Empty(_f.Left);

        Click(panel, "Download");
        // Waited for without leaving this thread, which the screen below belongs to.
#pragma warning disable xUnit1031 // blocking is the point; the download runs on other threads and can't wait for this one
        Assert.True(manager.Completion.Wait(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken));
#pragma warning restore xUnit1031

        Assert.Equal(1, started);
        Assert.Single(_f.Added);
        var view = new DownloadsView(CleanupInk.Desktop, Context(manager), () => { });
        Assert.Contains("Pasted link: https://music.example.org/go", Texts(view));
        Assert.Contains("Licence: not known", Texts(view));
        Assert.Null(Find(view, "Licence"));
    }

    [Fact]
    public async Task Another_address_in_the_box_takes_the_offer_away()
    {
        Assert.SkipWhen(FfmpegTool.Tool is null, "ffmpeg is not in tools/ffmpeg (scripts/get-ffmpeg.ps1)");
        var file = _f.Audio(seconds: 3);
        var panel = new PasteLinkPanel(CleanupInk.Desktop, Context(Instant(_ => Audio(file))), () => { });
        panel.AddressBox.Text = "https://music.example.org/a.mp3";
        await panel.Look();
        Assert.NotNull(Find(panel, "Download"));

        panel.AddressBox.Text += "x";

        Assert.Null(Find(panel, "Download"));
    }

    [Fact]
    public async Task A_web_page_can_only_be_opened_in_the_browser()
    {
        Assert.SkipWhen(FfmpegTool.Tool is null, "ffmpeg is not in tools/ffmpeg (scripts/get-ffmpeg.ps1)");
        var panel = new PasteLinkPanel(CleanupInk.Desktop, Context(Instant(_ => FakeWeb.Ok("<html></html>"))), () => { });
        panel.AddressBox.Text = "https://music.example.org/page";

        await panel.Look();

        Assert.Contains("web page, not an audio file", Texts(panel));
        Assert.Null(Find(panel, "Download"));
        Assert.Empty(_opened);
        Click(panel, "Open in browser");
        Assert.Equal("https://music.example.org/page", Assert.Single(_opened).AbsoluteUri);
    }
}

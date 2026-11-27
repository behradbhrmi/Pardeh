using Avalonia.Threading;
using Pardeh.Core.Downloads;
using Pardeh.Core.Library;
using Pardeh.UI.OnlineViews;

namespace Pardeh.UI;

/// <summary>Downloads: their own switch, the engine, the screen, and adding what came to the library.</summary>
internal sealed partial class PlayerView
{
    /// <summary>What Pardeh says downloads send, wherever it asks or offers the switch.</summary>
    internal const string DownloadPrivacy =
        "Pardeh downloads only what you choose, and checks that it is a complete audio file before adding it."
        + " Searching free files sends the words you type to Wikimedia Commons, with Pardeh's name, version and project address;"
        + " downloads from it come from Wikimedia's file server, which gets the same."
#if PARDEH_LINK_DOWNLOADS
        + " A pasted link sends a request to the host in the link, as \"Pardeh/1.0\" (to Wikimedia, with Pardeh's version and project address)."
        + " Pardeh can't know the licence of a pasted link's file."
#endif
        + " Like any request, it shows that host your network address. No cookies, sign-ins, files or personal details are sent.";

    private DownloadManager? _downloads;
    private DownloadsView? _downloadsView;
    private bool _downloadsRefreshPosted;

    private void StartDownloads()
    {
        var rules = new DownloadRules();
        var engine = new DownloadEngine(rules.CreateClient(), rules,
            new AudioCheck(() => Pardeh.Nava.AudioDigest.IsAvailable, (path, cancel) => Pardeh.Nava.AudioDigest.Scan(path, 0, cancel)),
            new Pardeh.Nava.TagEditor(), ContactAgent);
        var store = new DownloadStore(Path.Combine(AppPaths.DataDirectory, "downloads"), Path.Combine(AppPaths.DataDirectory, "downloads.json"));
        _downloads = new DownloadManager(engine, store, Platform.CreateDownloadTarget());
        _downloads.Changed += (_, _) =>
        {
            // Reports come fast: one waiting redraw is enough.
            if (_downloadsRefreshPosted)
                return;
            _downloadsRefreshPosted = true;
            Dispatcher.UIThread.Post(() =>
            {
                _downloadsRefreshPosted = false;
                _downloadsView?.Fill();
            });
        };
        _downloads.Added += (_, record) => Dispatcher.UIThread.Post(() => AddDownload(record));
    }

    // Closing the app keeps what has come of a running download.
    private void StopDownloads()
    {
        if (_downloads is not { } downloads)
            return;
        downloads.Pause();
        downloads.Completion.Wait(TimeSpan.FromSeconds(2));
    }

    private void AddDownload(DownloadRecord record)
    {
        var source = record.Source;
        var watched = _library.Folders.Any(folder => source.StartsWith(folder.TrimEnd('/', '\\') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        if (watched)
        {
            // A file of this name may have been removed from the library before.
            _library.Unhide([source]);
            _ = _library.ScanAsync();
        }
        else
        {
            _library.AddFiles([source]);
        }

        _player.Announce("Downloaded: " + record.FileName);
        _downloadsView?.Fill();
    }

    private async Task<bool> AllowDownloads()
    {
        var settings = AppSettings.Current;
        if (settings.Downloads)
            return true;
        if (!await ConfirmAsync("Turn on downloads?", DownloadPrivacy + (Shell is null ? string.Empty : "\n\nTurn them on?"), "Turn on", "Not now"))
            return false;
        settings.Downloads = true;
        settings.Save();
        return true;
    }

    private DownloadsContext DownloadsContext() => new(_downloads!)
    {
        IsOn = () => AppSettings.Current.Downloads,
        Allow = AllowDownloads,
        OpenPage = OpenWebPage,
        ReadClipboard = ReadClipboard,
        Play = record => _player.OpenItems([_library.Find(record.Source)?.ToMediaItem() ?? new Core.Playback.MediaItem(record.Source)], 0),
        Reveal = Shell is null ? null : record => Platform.RevealInFileManager(record.Source),
        IsThere = record => MediaSources.Exists(record.Source),
        SearchCatalog = ShowCatalog,
    };

    private IDownloadCatalog? _catalog;
    private bool _catalogShown;

    // One for the app: it keeps to its pace.
    private IDownloadCatalog Catalog => _catalog ??= new CommonsCatalog(
        new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(10), AutomaticDecompression = System.Net.DecompressionMethods.All,
        }) { Timeout = Timeout.InfiniteTimeSpan }, // the catalog times each request
        ContactAgent);

    private static string ContactAgent => $"Pardeh/{AppInfo.Version} ({AppInfo.SourcePage})";

    private async Task ShowCatalog()
    {
        if (_downloads is not { } downloads || _catalogShown)
            return;
        _catalogShown = true;
        var started = false;
        try
        {
            await ShowPanel("Free files", close => new CatalogView(Ink, new CatalogContext(Catalog, downloads)
            {
                Allow = AllowDownloads,
                Remember = RememberCatalogSearch,
                OpenPage = OpenWebPage,
                Started = () =>
                {
                    started = true;
                    close();
                },
            }, close));
        }
        finally
        {
            _catalogShown = false;
        }

        // The download is shown where downloads are.
        if (started && _downloadsView is null)
            await ShowDownloads();
        else
            _downloadsView?.Fill();
    }

    private void RememberCatalogSearch(string words, IReadOnlyList<CatalogFile> files)
    {
        if (_history is not { } history)
            return;
        history.AddSearch(Catalog.Name, words, null,
        [
            .. files.Where(f => f.SourcePage is not null).Take(Core.OnlineInfo.LookupHistory.KeptMatches)
                .Select(f => new Core.OnlineInfo.HistoryMatch(f.SourcePage!.AbsoluteUri, f.Title, f.Creator, f.LicenseName, 0, (int)Math.Min(f.Seconds * 1000, int.MaxValue))),
        ]);
        history.Flush();
    }

    private async Task ShowDownloads()
    {
        if (_downloads is null || _downloadsView is not null)
            return;
        await ShowPanel("Downloads", close => _downloadsView = new DownloadsView(Ink, DownloadsContext(), close));
        _downloadsView = null;
    }

    private string DownloadsHeader() => _downloads?.Status is { IsRunning: true } ? "Downloads (downloading)..." : "Downloads...";
}

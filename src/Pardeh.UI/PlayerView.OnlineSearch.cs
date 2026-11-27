using Pardeh.Core.Cleanup;
using Pardeh.Core.Library;
using Pardeh.Core.OnlineInfo;
using Pardeh.UI.OnlineViews;

namespace Pardeh.UI;

/// <summary>Looking through what was looked up, and searching online.</summary>
internal sealed partial class PlayerView
{
    private LookupHistory? _history;
    private HistoryView? _historyView;

    private void StartLookupHistory()
    {
        _history = new LookupHistory(Path.Combine(AppPaths.DataDirectory, "lookup-history.json"), () => AppSettings.Current.KeepLookupHistory);
        if (_online is { } online && AppSettings.Current.FixInfoOnline)
            _history.Seed(Metadata.Provider.Name, online.All());
        // The switch in Settings: "keep none" also means none of what is there.
        AppSettings.Current.Changed += () =>
        {
            if (!AppSettings.Current.KeepLookupHistory && _history is { Count: > 0 } history)
                history.Clear();
        };
    }

    /// <summary>The search entries of the app and File menus.</summary>
    private IEnumerable<(string Header, Action Run)> OnlineSearchMenuEntries()
    {
        yield return ("Search Online...", () => _ = ShowSearchOnline());
        yield return ("Looked-Up Songs...", () => _ = ShowLookupHistory());
        yield return (DownloadsHeader(), () => _ = ShowDownloads());
    }

    private bool _searching;

    private async Task ShowSearchOnline()
    {
        if (_searching)
            return;
        _searching = true;
        try
        {
            await ShowPanel("Search Online", close =>
            {
                var free = Ink.Button("Search free files to download...", () => _ = ShowCatalog());
                free.HorizontalAlignment = Shell is null ? Avalonia.Layout.HorizontalAlignment.Stretch : Avalonia.Layout.HorizontalAlignment.Left;
                return new SearchOnlineView(Ink, new SearchOnlineContext
                {
                    Lookup = Metadata,
                    Allow = AllowOnlineInfo,
                    Remember = result => RememberSearch(result, null),
                    WhereToGet = (target, other) => WhereToGet(target, other),
                    OpenPage = OpenWebPage,
                }, close, free);
            });
        }
        finally
        {
            _searching = false;
        }
    }

    private HistoryContext HistoryContext() => new(_history!)
    {
        Keeps = () => AppSettings.Current.KeepLookupHistory,
        SetKeeps = KeepHistory,
        Clear = ClearHistory,
        Open = OpenHistoryEntry,
        FileState = row => row.Source is not { } source || _library.Find(source) is not null ? null
            : MediaSources.Exists(source) ? "This file is no longer in the library." : "This file is gone.",
        CanFixInfo = CanFixInfo,
        FixInfo = FixInfo,
        LookAgain = LookUpAgain,
        OpenPage = OpenWebPage,
        WhereToGet = target => WhereToGet(target),
    };

    private LinksContext LinksContext() => new()
    {
        Allow = AllowOnlineInfo,
        OpenPage = OpenWebPage,
        Fetch = async (id, cancel) =>
        {
            var links = await Metadata.Provider.LinksAsync(id, cancel);
            _history?.SetLinks(Metadata.Provider.Name, id, links.Links);
            return links;
        },
    };

    private Task WhereToGet(LinkTarget target, LinkTarget? other = null) =>
        ShowPanel("Where to get it", close =>
        {
            var view = new LinksView(Ink, LinksContext(), target, close, other);
            // Asked once the screen is there: the consent question belongs on top of it.
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = view.Ask());
            return view;
        }, 620, 560);

    private async Task ShowLookupHistory()
    {
        if (_history is null || _historyView is not null)
            return;
        await ShowPanel("Looked-Up Songs", close => _historyView = new HistoryView(Ink, HistoryContext(), close));
        _historyView = null;
    }

    private Task OpenHistoryEntry(LookupHistoryRow row) =>
        ShowPanel("Looked-up song", close => new HistoryEntryView(Ink, HistoryContext(), row, close), 620, 560);

    private async Task KeepHistory(bool keep)
    {
        var settings = AppSettings.Current;
        if (settings.KeepLookupHistory == keep)
            return;
        if (!keep && _history is { Count: > 0 }
            && !await ConfirmAsync("Keep no history?", "The history is deleted, and new look-ups are not listed.", "Delete", "Cancel"))
            return;
        settings.KeepLookupHistory = keep;
        settings.Save();
    }

    private async Task ClearHistory()
    {
        if (_history is { Count: > 0 } history
            && await ConfirmAsync("Clear history?", "Every look-up in the list is removed. Your songs are not changed.", "Clear", "Cancel"))
            history.Clear();
    }

    /// <summary>A look-up from the history, asked again. What to tell the user.</summary>
    private async Task<string> LookUpAgain(LookupHistoryRow row, CancellationToken cancel)
    {
        // A search of free files goes where it went before, under the downloads switch.
        var catalog = row.Service == Catalog.Name;
        if (!await (catalog ? AllowDownloads() : AllowOnlineInfo()))
            return string.Empty;
        try
        {
            if (catalog)
            {
                var files = await Catalog.SearchAsync(row.SearchedTitle, cancel);
                RememberCatalogSearch(row.SearchedTitle, files);
                return Said(files.Count);
            }

            if (row.Source is { } source && SongFor(source) is { } song && _checker is { } checker)
            {
                var entry = await checker.CheckAsync(song, cancel);
                _online?.Flush();
                _history?.Flush();
                return entry is null ? "This file can't be read, or has no title to search for." : Said(entry.Candidates.Count);
            }

            // No file any more (or never one): the same words are sent.
            var song2 = new LocalSong(row.SearchedTitle, row.SearchedArtist is { } a ? [a] : [], string.Empty, 0);
            var result = await Metadata.SearchAsync(row.SearchedTitle, row.SearchedArtist ?? string.Empty, song2, cancel);
            RememberSearch(result, null);
            return Said(result.Matches.Count);
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
        catch (Exception ex) when (ex is MetadataException or Core.Downloads.DownloadException or IOException or UnauthorizedAccessException or Core.Changes.TagEditException or NotSupportedException)
        {
            return ex.Message;
        }

        static string Said(int count) =>
            count == 0 ? "Nothing was found this time. It is listed in the history." : "The new result is at the top of the history.";
    }

    private void RememberSearch(LookupResult result, string? source)
    {
        if (result.Searched is not { } searched || _history is not { } history)
            return;
        history.AddSearch(Metadata.Provider.Name, searched.Title, searched.Artist,
            [.. result.Matches.Take(LookupHistory.KeptMatches).Select(m => HistoryMatch.From(m.Candidate))], source);
        history.Flush();
    }

    /// <summary>
    /// Opens a page in the browser, after showing where it is. Only https: the address may come from a database anyone can
    /// edit.
    /// </summary>
    private async Task OpenWebPage(Uri page, string note)
    {
        if (page.Scheme != Uri.UriSchemeHttps || page.UserInfo.Length > 0)
        {
            ShowMessage("Only secure (https) web pages are opened.");
            return;
        }

        if (await ConfirmAsync("Open in your browser?", $"{page.IdnHost}\n\n{note}\nThat site's own rules apply." + (Shell is null ? string.Empty : "\n\nOpen it?"),
                "Open", "Cancel"))
            Platform.OpenUrl(page.AbsoluteUri);
    }
}

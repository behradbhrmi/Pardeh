using Avalonia.Threading;
using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;
using Pardeh.Core.Library;
using Pardeh.Core.OnlineInfo;
using Pardeh.Core.Playback;
using Pardeh.UI.CleanupViews;

namespace Pardeh.UI;

/// <summary>Fix Info online: look-ups, saved results, Check Library Online and the playing song's check.</summary>
internal sealed partial class PlayerView
{
    /// <summary>What Pardeh says it sends, wherever it asks or offers the switch.</summary>
    internal const string OnlineInfoPrivacy =
        "To look a song up, sends its title and artist to MusicBrainz (if those find nothing, words from the file name, without website text), "
        + "with Pardeh's name, version and project address. That happens when you ask: for one song, or for your library with Check Library Online. "
        + "Covers come from the Cover Art Archive and the Internet Archive. No folders, files or personal details.";

    /// <summary>What the switch for the playing song says.</summary>
    internal const string CheckPlayingSummary =
        "With the switch above on, a song from your library that plays for a few seconds is looked up by itself, once, sending the same details. "
        + "Nothing is changed: a mark shows a match.";

    private MetadataLookup? _metadata;
    private OnlineStore? _online;
    private SongChecker? _checker;
    private LibraryCheck? _check;
    private PlayingSongCheck? _playingCheck;
    private LibraryCheckView? _checkView;
    private DispatcherTimer? _marksSoon;

    // One for the app: it keeps to MusicBrainz's one request a second, and remembers what it was told.
    private MetadataLookup Metadata => _metadata ??= new MetadataLookup(new MusicBrainzProvider(
        new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(8), AutomaticDecompression = System.Net.DecompressionMethods.All,
        }) { Timeout = Timeout.InfiniteTimeSpan }, // the provider times each request
        $"Pardeh/{AppInfo.Version} ({AppInfo.SourcePage})"));

    private static bool DatabaseScript() => AppSettings.Current.FixInfoDatabaseScript;

    private void StartOnlineInfo()
    {
        _online = new OnlineStore(Path.Combine(AppPaths.DataDirectory, "fixinfo-online.json"));
        _checker = new SongChecker(Metadata, _online, ReadSong, DatabaseScript);
        _check = new LibraryCheck(_checker, () => AppSettings.Current.FixInfoOnline);
        _playingCheck = new PlayingSongCheck(_checker, () => AppSettings.Current is { FixInfoOnline: true, FixInfoCheckPlaying: true }, PlayingSongDue);
        _online.Changed += (_, _) => Dispatcher.UIThread.Post(OnlineResultsChanged);
        _check.Changed += (_, _) => Dispatcher.UIThread.Post(OnlineResultsChanged);
        _player.CurrentMediaChanged += (_, _) => PlayingSongChanged();
        _player.PlayStateChanged += (_, _) => PlayingSongChanged();
        // The switch decides whether matches show. Settings are saved for many other things too.
        var shown = AppSettings.Current.FixInfoOnline;
        AppSettings.Current.Changed += () =>
        {
            if (shown == AppSettings.Current.FixInfoOnline)
                return;
            shown = AppSettings.Current.FixInfoOnline;
            Dispatcher.UIThread.Post(UpdateMarks);
        };
    }

    private void StopOnlineInfo()
    {
        _playingCheck?.Playing(null);
        _check?.Stop();
        _online?.Flush();
    }

    // A song's tags now and the local checks of it.
    private async Task<SongFacts?> ReadSong(string source)
    {
        // A song that can't be changed has no use for a look-up.
        if (_changes is not { } changes || _cleanup is not { } cleanup || await Task.Run(() => changes.WhyUnchangeable(source)).ConfigureAwait(false) is not null)
            return null;
        var tags = await changes.ReadTagsAsync(source).ConfigureAwait(false);
        var proposal = await Task.Run(() => cleanup.Analyze(source)).ConfigureAwait(false);
        return proposal is null ? null : new SongFacts(proposal, tags, cleanup.Store.Rules);
    }

    private OnlineEntry? SavedFor(LibraryItem item) => _online?.Get(item.Path, item.FileSize, item.Modified);

    private SongToCheck? SongFor(string source)
    {
        if (_library.Find(source) is not { IsVideo: false } item)
            return null;
        var seconds = item.DurationSeconds;
        if (seconds <= 0 && string.Equals(_player.CurrentMedia?.Source, source, StringComparison.OrdinalIgnoreCase))
            seconds = _player.Duration;
        return new SongToCheck(item.Path, item.FileSize, item.Modified, seconds);
    }

    // ---- the playing song ------------------------------------------------------------------------

    private void PlayingSongChanged()
    {
        if (_playingCheck is null || _player.IsResumingFile)
            return;
        if (_player.PlayState == PlayState.Playing)
            _playingCheck.Playing(_player.CurrentMedia?.Source);
        else if (_player.PlayState == PlayState.Stopped)
            _playingCheck.Playing(null);
    }

    private SongToCheck? PlayingSongDue(string source)
    {
        // A running library check will come to it.
        if (_cleanup is not { } cleanup || _online is not { } online || _check?.Progress.IsActive == true)
            return null;
        return SongFor(source) is { } song && online.IsDue(song.Source, song.Size, song.Modified) && !cleanup.Store.IsIgnored(song.Source) && CanFixInfo(song.Source)
            ? song
            : null;
    }

    // ---- saved results and marks -------------------------------------------------------------------

    private void OnlineResultsChanged()
    {
        _checkView?.Fill();
        // While a check runs results come every second or two: the lists behind are not redrawn that often.
        if (_check?.Progress.IsActive != true)
        {
            _marksSoon?.Stop();
            UpdateMarks();
            return;
        }

        _marksSoon ??= new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) =>
        {
            _marksSoon!.Stop();
            UpdateMarks();
        });
        if (!_marksSoon.IsEnabled)
            _marksSoon.Start();
    }

    private FixInfoState StateOf(string source, OnlineEntry? entry) =>
        FixInfoStates.Of(_cleanup!.Store.IsIgnored(source), _cleanup.ProposalFor(source), entry);

    /// <summary>The details of a file the user took from a match: it is not asked about again while it stays as it is.</summary>
    private void RememberApplied(string source, BatchResult result, OnlineUse used)
    {
        var mine = result.Results.Where(r => string.Equals(r.Source, source, StringComparison.Ordinal)).ToList();
        if (_online is not { } online || mine.Count == 0 || !mine.All(r => r.Status is ResultStatus.Done or ResultStatus.Unchanged))
            return;
        var now = mine.LastOrDefault(r => r.NewSource is not null)?.NewSource ?? source;
        if (_library.Find(now) is not { } item)
            return;
        online.Put(item.Path, new OnlineEntry
        {
            Size = item.FileSize, Modified = item.Modified, LookedUp = online.Now, Matcher = OnlineStore.MatcherVersion, Group = OnlineGroup.Checked,
            Song = used.Song, Candidates = [used.Candidate], Applied = true,
        });
        online.Flush();
    }

    // ---- one song in Fix Info ------------------------------------------------------------------------

    private OnlineContext OnlineFor(CleanupProposal proposal, TagFields current)
    {
        var song = SongFor(proposal.Source);
        var seconds = song?.Seconds ?? 0;
        if (seconds <= 0 && string.Equals(_player.CurrentMedia?.Source, proposal.Source, StringComparison.OrdinalIgnoreCase))
            seconds = _player.Duration;
        var rules = _cleanup!.Store.Rules;
        var steps = MetadataLookup.Steps(current, proposal, seconds, rules);
        // The song as the first search sees it; one typed by hand keeps only its length and album.
        var first = steps.Count > 0 ? steps[0].Song : new LocalSong(current.Title ?? string.Empty, current.Artists ?? [], current.Album ?? string.Empty, seconds);
        return new OnlineContext(Metadata, steps, first, AllowOnlineInfo, DatabaseScript)
        {
            Saved = AppSettings.Current.FixInfoOnline && song is not null ? _online?.Get(song.Source, song.Size, song.Modified) : null,
            Remember = result =>
            {
                if (song is null || steps.Count == 0)
                    return;
                _checker!.Save(song, new SongFacts(proposal, current, rules), steps, result);
                _online!.Flush();
            },
        };
    }

    private async Task<bool> AllowOnlineInfo()
    {
        var settings = AppSettings.Current;
        if (settings.FixInfoOnline)
            return true;
        if (!await ConfirmAsync("Look up songs online?", OnlineInfoPrivacy + (Shell is null ? string.Empty : "\n\nTurn it on?"), "Turn on", "Not now"))
            return false;
        settings.FixInfoOnline = true;
        settings.Save();
        return true;
    }

    // ---- Check Library Online ------------------------------------------------------------------------

    private async Task CheckLibraryOnline()
    {
        if (_changes is null || _cleanup is not { } cleanup || _check is not { } check || _checkView is not null)
            return;
        if (!cleanup.Complete && !cleanup.Progress.Running)
            _ = cleanup.ScanAsync();
        await ShowPanel("Check Library Online", close =>
        {
            LibraryCheckView? view = null;
            var context = new LibraryCheckContext(check, _online!, _library, cleanup)
            {
                Start = StartLibraryCheck,
                Review = ReviewFromList,
                EditPatterns = EditPatterns,
                Apply = (picks, covers, pattern) => _ = ApplyChecked(view!, picks, covers, pattern),
            };
            view = new LibraryCheckView(Ink, context, close);
            _checkView = view;
            return view;
        });
        _checkView = null;
    }

    private async Task StartLibraryCheck(IReadOnlyList<SongToCheck> songs)
    {
        if (_check is not { } check || songs.Count == 0 || check.Progress.IsActive)
            return;
        var count = songs.Count == 1 ? "1 song" : $"{songs.Count} songs";
        var text = $"The title and artist of {count} are sent to MusicBrainz (if those find nothing, words from the file name), with Pardeh's name, version and "
            + $"project address. No folders, files or personal details.\n\nIt takes {LibraryCheckView.Range(songs.Count)} and uses up to "
            + $"{LibraryCheckView.Data(songs.Count * LibraryCheckView.KilobytesPerSong)} of data"
            + (Shell is null ? ", mobile data too. " : ". ")
            + "You can pause or stop, and what was found is kept. Nothing in your files changes.";
        if (!await ConfirmAsync($"Check {count} online?", text + (Shell is null ? string.Empty : "\n\nStart?"), "Start", "Not now"))
            return;
        var settings = AppSettings.Current;
        if (!settings.FixInfoOnline)
        {
            settings.FixInfoOnline = true;
            settings.Save();
        }

        _playingCheck?.Playing(null);
        check.Start(songs);
    }

    private async Task<ReviewResult> ReviewFromList(string source)
    {
        try
        {
            return await Look(source) is var (fresh, tags) ? await ReviewSong(fresh, tags) : new ReviewResult(ReviewAnswer.Cancel);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TagEditException or NotSupportedException)
        {
            ShowMessage(ex.Message);
            return new ReviewResult(ReviewAnswer.Cancel);
        }
    }

    private async Task ApplyChecked(LibraryCheckView view, IReadOnlyList<BatchPick> picks, bool covers, string? pattern)
    {
        if (_changes is not { } changes || _fixingInfo || picks.Count == 0)
            return;
        _fixingInfo = true;
        var applied = false;
        var stop = new CancellationTokenSource();
        try
        {
            // The check waits while files are read and changed.
            using var held = _check!.Hold();
            var prepared = await OnlineBatch.PrepareAsync(picks, ReadSong, Metadata.Provider, covers, pattern, DatabaseScript(),
                new Progress<(int Done, int Total)>(view.Busy(covers ? "Preparing and getting covers" : "Preparing", stop.Cancel)),
                stop.Token);
            var plan = await CleanupPlanner.PlanAsync(changes, prepared.Choices, null, stop.Token);

            var notes = new List<string>();
            if (prepared.CoversFailed > 0)
                notes.Add($"Covers not fetched (the service didn't answer): {prepared.CoversFailed}.");
            if (prepared.CoversLeft > 0)
                notes.Add($"No cover added where the album is unsure: {prepared.CoversLeft}. Review those songs for one.");
            if (prepared.NotSure > 0)
                notes.Add($"Left out, as they need a look now: {prepared.NotSure}.");
            if (plan.Problems.Count > 0)
                notes.Add($"Can't be read and will be left alone: {plan.Problems.Count}.");
            var extra = notes.Count > 0 ? "\n" + string.Join("\n", notes) : string.Empty;
            if (plan.IsEmpty)
            {
                ShowMessage("Nothing to change." + extra);
                return;
            }

            if (!await ConfirmAsync("Apply these changes?",
                    plan.Summary + extra + (Shell is null ? "\nYou can undo them all afterwards." : "\n\nApply them? You can undo them all afterwards."), "Apply", "Cancel"))
                return;
            var result = await Apply($"Fix {plan.Files} {(plan.Files == 1 ? "song" : "songs")} from online results", plan, AskBeforeInterrupting() with
            {
                Cancel = stop.Token,
                Progress = new Progress<(int Done, int Total)>(view.Busy("Changing files", stop.Cancel, "Stop after this file")),
            });
            applied = true;
            foreach (var (source, used) in prepared.Used)
                RememberApplied(source, result, used);
            view.ShowList(changed: true);
            await Report(result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TagEditException or NotSupportedException)
        {
            ShowMessage("The changes could not be made. " + ex.Message);
        }
        finally
        {
            _fixingInfo = false;
            stop.Dispose();
            if (!applied)
                view.ShowList(changed: false);
        }
    }

    /// <summary>The menu entry for Check Library Online, saying how a running check or its finds stand.</summary>
    private string CheckLibraryHeader()
    {
        if (_check?.Progress is { IsActive: true } progress)
            return progress.State == CheckState.Paused
                ? $"Check Library Online (paused, {progress.Done} of {progress.Total})..."
                : $"Check Library Online ({progress.Done} of {progress.Total})...";
        return _matched.Count > 0 ? $"Check Library Online ({_matched.Count} {(_matched.Count == 1 ? "match" : "matches")})..." : "Check Library Online...";
    }
}

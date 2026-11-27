using Avalonia.Threading;
using Pardeh.Core.Changes;
using Pardeh.Core.Duplicates;
using Pardeh.Core.Library;
using Pardeh.Core.LyricsKit;
using Pardeh.UI.DuplicateViews;

namespace Pardeh.UI;

/// <summary>Duplicates: the check, the groups and what is done with them, "Copies and similar", and the removed files.</summary>
internal sealed partial class PlayerView
{
    private DuplicateScanner? _duplicates;
    private DuplicateReport? _duplicateReport;
    private DuplicatesView? _duplicatesView;
    private RelatedView? _relatedView;
    private readonly HashSet<string> _skippedGroups = new(StringComparer.Ordinal);
    private HashSet<string> _listedInPlaylists = new(StringComparer.OrdinalIgnoreCase);
    private bool _reporting, _reportAgain, _pausedInBackground;
    private long _reportedAt;
    private DispatcherTimer? _reportSoon;

    private void StartDuplicates()
    {
        var store = new DuplicateStore(Path.Combine(AppPaths.DataDirectory, "duplicates.json"));
        _duplicates = new DuplicateScanner(_library, new Pardeh.Nava.DuplicateAnalyzer(), store, Path.Combine(AppPaths.DataDirectory, "duplicates-cache.json"),
            () => _cleanup?.Store.Rules ?? Core.Cleanup.JunkRules.None);
        _duplicates.Changed += (_, _) => Dispatcher.UIThread.Post(DuplicatesChanged);
        store.Changed += (_, _) => Dispatcher.UIThread.Post(() => RefreshDuplicates(now: true));
        _library.Changed += (_, _) => Dispatcher.UIThread.Post(() => RefreshDuplicates());
        // A phone's battery: the check waits while the app is not in front.
        if (OperatingSystem.IsAndroid())
        {
            AppVisibility.Changed += foreground => Dispatcher.UIThread.Post(() =>
            {
                if (_duplicates is not { } scanner)
                    return;
                if (!foreground && scanner.Progress.State == DuplicateScanState.Running)
                {
                    _pausedInBackground = true;
                    scanner.Pause();
                }
                else if (foreground && _pausedInBackground)
                {
                    _pausedInBackground = false;
                    if (scanner.Progress.State == DuplicateScanState.Paused)
                        _ = scanner.Start();
                }
            });
        }
    }

    private void StopDuplicates() => _duplicates?.Stop();

    private void DuplicatesChanged()
    {
        _duplicatesView?.Fill();
        _relatedView?.Fill();
        // While a check runs the groups are worked out now and then; when it ends or pauses, at once.
        RefreshDuplicates(now: _duplicates?.Progress.State != DuplicateScanState.Running);
    }

    private bool DuplicatesShown => _duplicatesView is not null || _relatedView is not null;

    /// <summary>Works the groups out again, off the UI thread, while a screen that shows them is open.</summary>
    private void RefreshDuplicates(bool now = false)
    {
        if (_duplicates is not { } scanner || !DuplicatesShown)
            return;
        if (_reporting)
        {
            _reportAgain = true;
            return;
        }

        var wait = now ? 0 : 3000 - (Environment.TickCount64 - _reportedAt);
        if (wait > 0)
        {
            _reportSoon?.Stop();
            _reportSoon = new DispatcherTimer(TimeSpan.FromMilliseconds(wait), DispatcherPriority.Background, (_, _) =>
            {
                _reportSoon?.Stop();
                RefreshDuplicates(now: true);
            });
            _reportSoon.Start();
            return;
        }

        _reporting = true;
        _ = Task.Run(() =>
        {
            DuplicateReport? report = null;
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                report = scanner.Report();
                foreach (var name in _playlists.Names)
                    listed.UnionWith(_playlists.Read(name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log("Duplicates: " + ex.Message);
            }

            Dispatcher.UIThread.Post(() =>
            {
                _reporting = false;
                _reportedAt = Environment.TickCount64;
                if (report is not null)
                {
                    _duplicateReport = report;
                    _listedInPlaylists = listed;
                }

                _duplicatesView?.Fill();
                _relatedView?.Fill();
                if (_reportAgain)
                {
                    _reportAgain = false;
                    RefreshDuplicates();
                }
            });
        });
    }

    // What speaks for a copy beyond its own details: it is in a playlist, or has lyrics saved.
    private int CopyExtras(SongCopy copy) =>
        (_listedInPlaylists.Contains(copy.Source) ? 1 : 0)
        + (_lyrics.Service.Store is { } store && store.PiecesOf(LyricsTrackKey.From(copy.Item.ToMediaItem())) != LyricsPieces.None ? 1 : 0);

    private DuplicatesContext DuplicateContext() => new(_duplicates!)
    {
        Report = () => _duplicateReport,
        Open = OpenGroup,
        KeepBest = KeepBest,
        ShowRemoved = ShowRemovedFiles,
        Extra = CopyExtras,
        Skipped = _skippedGroups,
    };

    private async Task FindDuplicates()
    {
        if (_changes is null || _duplicates is null || _duplicatesView is not null)
            return;
        await ShowPanel("Find Duplicates", close =>
        {
            var view = new DuplicatesView(Ink, DuplicateContext(), close);
            _duplicatesView = view;
            RefreshDuplicates(now: true);
            return view;
        });
        _duplicatesView = null;
    }

    private SongActions CopyActions() => new()
    {
        Play = copy => _player.OpenItems([copy.Item.ToMediaItem()], 0),
        ShowText = Shell is null ? "Details" : "Show folder",
        Show = copy =>
        {
            if (Shell is not null && File.Exists(copy.Source))
                Platform.RevealInFileManager(copy.Source);
            else
                _ = Tell("The file", $"{copy.Source}\n{DuplicateText.Facts(copy)}");
        },
        CanFix = copy => CanFixInfo(copy.Source),
        FixInfo = copy => FixInfo(copy.Source),
    };

    private async Task OpenGroup(DuplicateGroup group)
    {
        var answer = new GroupAnswer(GroupAction.Close);
        var advice = CopyAdvisor.Advise(group, i => CopyExtras(group.Copies[i]));
        await ShowPanel("Copies", close => new GroupView(Ink, group, advice, CopyActions(), chosen =>
        {
            answer = chosen;
            close();
        }), 720, 640);

        switch (answer)
        {
            case { Action: GroupAction.KeepAndMerge or GroupAction.KeepSelected, Choice: { } choice }:
                await RemoveCopies([choice], previewed: false);
                break;
            case { Action: GroupAction.HideExtras, Choice: { } hidden }:
                await HideExtras(hidden);
                break;
            case { Action: GroupAction.NotDuplicates }:
            {
                var added = _duplicates!.Store.KeepApart(group.Copies);
                if (!await ConfirmAsync("Not duplicates", "These will not be grouped again."
                        + (Shell is null ? string.Empty : "\n\nKeep that? No takes it back."), "OK", "Undo", onBack: true))
                    _duplicates.Store.Rejoin(added);
                break;
            }

            case { Action: GroupAction.Skip }:
                _skippedGroups.Add(group.Id);
                break;
        }

        _duplicatesView?.Fill();
        RefreshDuplicates(now: true);
    }

    private async Task HideExtras(GroupChoice choice)
    {
        var copies = choice.Group.Copies;
        var paths = choice.Remove.Where(i => i != choice.Keep).SelectMany(i => copies[i].Aliases.Prepend(copies[i].Source)).ToList();
        if (paths.Count == 0)
            return;
        var what = DuplicateText.Count(paths.Count, "listing", "listings");
        if (!await ConfirmAsync("Hide extras?", $"Take {what} out of the library, keeping {copies[choice.Keep].Facts.FileName}? "
                + "The files stay where they are, and playlists that name them still play them.", "Hide", "Cancel"))
            return;
        var single = paths.Where(_library.IsAddedFile).ToList();
        _library.Remove(paths);
        if (!await ConfirmAsync("Hidden", $"{what} hidden from the library." + (Shell is null ? string.Empty : "\n\nKeep them hidden? No brings them back."),
                "OK", "Undo", onBack: true))
        {
            _library.Unhide(paths, single);
            await _library.ScanAsync();
        }
    }

    private async Task KeepBest(IReadOnlyList<GroupChoice> choices)
    {
        if (choices.Count == 0 || _changes is not { } changes)
            return;
        var go = false;
        var kinds = choices.SelectMany(c => c.Remove.Select(i => changes.Remove(c.Group.Copies[i].Source).Kind)).ToList();
        await ShowPanel("Keep the best of each", close => new BulkPreviewView(Ink, choices, Destination(kinds), yes =>
        {
            go = yes;
            close();
        }), 720, 640);
        if (go)
            await RemoveCopies(choices, previewed: true);
    }

    private string Destination(IReadOnlyCollection<ChangeKind> kinds)
    {
        var trash = Shell is null ? "the system trash" : "the Recycle Bin";
        const string held = "the Pardeh holding folder (see Removed Files)";
        return kinds.Contains(ChangeKind.Trash) && kinds.Contains(ChangeKind.Hold) ? $"{trash} or {held}"
            : kinds.Contains(ChangeKind.Hold) ? held : trash;
    }

    // Something to read, with nothing to choose: a desktop's box has one OK button; on a phone either button, or Back, closes.
    private async Task Tell(string title, string text)
    {
        if (Shell is null)
            await AskInView(title, text, "OK", "Close", onBack: true);
        else
            ShowMessage(title + "\n\n" + text);
    }

    // A removed copy and the kept one each with lyrics the user saved: only one set can show for the kept song.
    private List<string> LyricsOnBoth(IReadOnlyList<ChangeRequest> requests)
    {
        var both = new List<string>();
        if (_lyrics.Service.Store is not { } store)
            return both;
        var reader = new Pardeh.Nava.TagMediaInfoReader();
        bool Own(LyricsTrackKey key) => (store.PiecesOf(key) & (LyricsPieces.Own | LyricsPieces.Chords)) != 0;
        foreach (var request in requests)
        {
            if (request.Keep is not { } keep)
                continue;
            var theirs = LyricsTrackKey.From(reader.Read(request.Source));
            var kept = LyricsTrackKey.From(reader.Read(keep.Source));
            if (theirs.Hash != kept.Hash && Own(theirs) && Own(kept))
                both.Add(MediaSources.DisplayName(request.Source));
        }

        return both;
    }

    private string Describe(GroupChoice choice, DuplicatePlan plan)
    {
        var copies = choice.Group.Copies;
        var lines = new List<string> { $"Keep: {copies[choice.Keep].Facts.FileName}" };
        var removed = plan.Requests.Where(r => r.Keep is not null).Select(r => r.Source).ToHashSet(StringComparer.Ordinal);
        var going = copies.Where(c => removed.Contains(c.Source)).ToList();
        lines.AddRange(going.Take(4).Select(c => $"Remove: {c.Facts.FileName} ({DuplicateText.Folder(c.Source)})"));
        if (going.Count > 4)
            lines.Add($"and {going.Count - 4} more.");
        if (plan.Merged > 0)
            lines.Add("Added to the kept file: " + string.Join(", ", choice.Merge.Keys.Order().Select(k => k.ToString().ToLowerInvariant())) + ".");
        return string.Join("\n", lines);
    }

    private async Task RemoveCopies(IReadOnlyList<GroupChoice> choices, bool previewed)
    {
        if (_changes is not { } changes || _duplicates is not { } scanner || _fixingInfo)
            return;
        _fixingInfo = true;
        var stop = new CancellationTokenSource();
        var view = _duplicatesView;
        try
        {
            // First what it comes to, with every file looked at again; then the question; only then the files.
            var preparing = view?.Busy("Checking the files", stop.Cancel);
            var plan = await DuplicatePlanner.PlanAsync(changes, scanner.Analyzer, choices,
                preparing is null ? null : new Progress<(int Done, int Total)>(preparing), stop.Token);
            view?.ShowList();
            var problems = plan.Problems.Count == 0 ? string.Empty
                : $"\n\n{DuplicateText.Count(plan.Problems.Count, "thing", "things")} will be left alone:\n" + string.Join("\n", plan.Problems.Take(3))
                    + (plan.Problems.Count > 3 ? $"\nand {plan.Problems.Count - 3} more." : string.Empty);
            if (plan.IsEmpty)
            {
                await Tell("Nothing to do", "Nothing will be changed." + problems);
                return;
            }

            var lyrics = await Task.Run(() => LyricsOnBoth(plan.Requests));
            var lyricsNote = lyrics.Count == 0 ? string.Empty
                : "\n\nThe kept copy and a removed one both have lyrics or chords you saved. Only one can show for the kept song. The other is not deleted: "
                    + "it comes back if you undo or restore.";
            var destination = Destination([.. plan.Requests.Where(r => r.Keep is not null).Select(r => r.Kind)]);
            var summary = (previewed || choices.Count > 1
                    ? $"{DuplicateText.Count(plan.Removals, "file", "files")} ({DuplicateText.Size(plan.Bytes)}) will go to {destination}."
                    : Describe(choices[0], plan) + (plan.Removals > 0 ? $"\n\nRemoved files go to {destination}." : string.Empty))
                + " Playlists, ratings and play counts move to the kept copy." + lyricsNote + problems;
            // After the full preview, a second question only when there is something it did not say.
            if ((!previewed || problems.Length > 0 || lyricsNote.Length > 0)
                && !await ConfirmAsync("Remove copies?", summary + (Shell is null ? string.Empty : "\n\nGo ahead?"), "Remove", "Cancel"))
                return;

            BatchResult result;
            using (scanner.Hold())
            using (_cleanup!.Pause())
            using (_check?.Hold())
            {
                var changing = view?.Busy("Changing files", stop.Cancel);
                var description = plan.Removals == 1 ? "Remove a copy of a song" : $"Remove {plan.Removals} copies of songs";
                result = await changes.ApplyAsync(description, plan.Requests, AskBeforeInterrupting() with
                {
                    Cancel = stop.Token,
                    Progress = changing is null ? null : new Progress<(int Done, int Total)>(changing),
                });
            }

            view?.ShowList();
            var permanent = await ReportRemoval(result);
            if (permanent.Count > 0 && choices.Count == 1)
                await OfferPermanentDelete(choices[0], permanent);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TagEditException or NotSupportedException)
        {
            ShowMessage("The copies could not be removed. " + ex.Message);
        }
        finally
        {
            _fixingInfo = false;
            view?.ShowList();
            // Only what changed is read again.
            _ = scanner.Start();
        }
    }

    /// <returns>The files that could not be moved away, for which deleting for good may be offered.</returns>
    private async Task<IReadOnlyList<string>> ReportRemoval(BatchResult result)
    {
        var removed = result.Results.Count(r => r.Status == ResultStatus.Done && r.Kind is not ChangeKind.Tags);
        var other = result.Results.Where(r => r.Status is not (ResultStatus.Done or ResultStatus.Unchanged)).ToList();
        var text = $"{DuplicateText.Count(removed, "copy", "copies")} removed.";
        if (other.Count > 0)
        {
            text += $"\n{other.Count} not done:\n" + string.Join("\n", other.Take(3).Select(r => $"{MediaSources.DisplayName(r.Source)}: {r.Message}"));
            if (other.Count > 3)
                text += $"\nand {other.Count - 3} more.";
        }

        var notes = result.Results.Where(r => r.Status == ResultStatus.Done && r.Message is not null).Select(r => r.Message!).Distinct().Take(2).ToList();
        if (notes.Count > 0)
            text += "\n" + string.Join("\n", notes);

        // A desktop's Recycle Bin is emptied back by the user, so undoing right away could not work.
        var fromTheBin = Shell is not null && result.Results.Any(r => r.Status == ResultStatus.Done && r.Kind == ChangeKind.Trash);
        if (result.CanUndo && result.BatchId is { } batch && !fromTheBin)
        {
            if (!await ConfirmAsync("Finished", text + (Shell is null ? "\nUndo is also in the menu." : "\n\nKeep the changes? No undoes them now; Undo is also in the menu."),
                    "OK", "Undo", onBack: true))
                await Undo(batch);
        }
        else
        {
            if (fromTheBin)
                text += "\n\nTo undo, first restore the files from the Recycle Bin, then choose Undo in the menu.";
            await Tell("Finished", text);
        }

        // Not when the safety checks held a file back, nor when permission was refused: only when moving it failed.
        return [.. result.Results.Where(r => r.Status == ResultStatus.Failed && !r.Refused && r.Kind is ChangeKind.Trash or ChangeKind.Hold
            && _changes?.Stat(r.Source) is not null).Select(r => r.Source)];
    }

    private async Task OfferPermanentDelete(GroupChoice choice, IReadOnlyList<string> failed)
    {
        var copies = choice.Group.Copies.ToList();
        var which = Enumerable.Range(0, copies.Count).Where(i => failed.Contains(copies[i].Source)).ToList();
        if (which.Count == 0 || _changes is not { } changes || _duplicates is not { } scanner)
            return;
        var names = string.Join("\n", which.Take(4).Select(i => copies[i].Facts.FileName));
        if (!await ConfirmAsync("Delete for good?",
                $"{DuplicateText.Count(which.Count, "file", "files")} could not be moved to a trash or the holding folder:\n{names}\n\n"
                + "You can keep them. Deleting for good CANNOT be undone." + (Shell is null ? string.Empty : "\n\nDelete for good?"), "Delete for good", "Keep"))
            return;
        // The kept file may have been given new details just now; every other check is made again.
        if (_library.Find(copies[choice.Keep].Source) is { } kept)
            copies[choice.Keep] = copies[choice.Keep] with { Item = kept };
        var again = new GroupChoice(choice.Group with { Copies = copies }, choice.Keep, which, new Dictionary<MergeField, int>());
        var plan = await DuplicatePlanner.PlanAsync(changes, scanner.Analyzer, [again], permanent: true);
        if (plan.IsEmpty)
        {
            ShowMessage("Nothing was deleted. " + plan.Problems.FirstOrDefault());
            return;
        }

        var result = await changes.ApplyAsync("Delete copies for good", plan.Requests, AskBeforeInterrupting());
        var done = result.Count(ResultStatus.Done);
        var problem = result.Results.FirstOrDefault(r => r.Status != ResultStatus.Done)?.Message;
        ShowMessage($"{DuplicateText.Count(done, "file", "files")} deleted for good." + (problem is null ? string.Empty : " " + problem));
    }

    // ---- copies and similar songs of one song ---------------------------------------------------------

    /// <summary>True for a song in the library: videos and streams have no copies list.</summary>
    private bool CanFindCopies(string source) =>
        _duplicates is not null && _library.Find(source) is { IsVideo: false };

    private async Task FindCopies(string source)
    {
        if (_changes is null || _duplicates is not { } scanner || _relatedView is not null)
            return;
        await ShowPanel("Copies and Similar", close =>
        {
            var view = new RelatedView(Ink, new RelatedContext(scanner, source)
            {
                Report = () => _duplicateReport,
                Actions = CopyActions(),
                Manage = OpenGroup,
            }, close);
            _relatedView = view;
            RefreshDuplicates(now: true);
            return view;
        }, 720, 640);
        _relatedView = null;
    }

    // ---- removed files ----------------------------------------------------------------------------

    private async Task ShowRemovedFiles()
    {
        if (_changes is not { } changes)
            return;
        var context = new RemovedContext
        {
            List = () => Task.Run(changes.ListHeld),
            TrashNote = Shell is null
                ? "Songs moved to the system trash are not listed here: bring them back with Undo, or in your Files app. The system empties its trash by itself after a while."
                : "Songs moved to the Recycle Bin are not listed here: restore them there, then Undo in the menu gives them back their playlists and history.",
            Restore = async held =>
            {
                var result = await changes.RestoreHeldAsync(held, AskBeforeInterrupting());
                if (result.Status != ResultStatus.Done)
                    ShowMessage("It could not be restored. " + result.Message);
                else
                {
                    _player.Announce("Restored: " + held.Name);
                    // A check that had gone through everything reads the file that came back.
                    if (_duplicates?.Progress.State == DuplicateScanState.Done)
                        _ = _duplicates.Start();
                }
            },
            Delete = async files =>
            {
                var size = DuplicateText.Size(files.Sum(f => f.Size));
                var what = files.Count == 1 ? files[0].Name : DuplicateText.Count(files.Count, "file", "files");
                if (!await ConfirmAsync("Delete for good?", $"Delete {what} ({size})? This CANNOT be undone." + (Shell is null ? string.Empty : "\n\nDelete?"),
                        "Delete", "Cancel"))
                    return;
                var problems = await changes.DeleteHeldAsync(files);
                if (problems.Count > 0)
                    ShowMessage("Not all could be deleted. " + problems[0]);
            },
        };
        await ShowPanel("Removed Files", close => new RemovedView(Ink, context, close), 620, 560);
    }

    /// <summary>The duplicate entries of the app and File menus.</summary>
    private IEnumerable<(string Header, Action Run)> DuplicateMenuEntries()
    {
        if (_changes is null || _duplicates is not { } scanner)
            yield break;
        if (_player.CurrentMedia is { } playing && CanFindCopies(playing.Source))
        {
            // A count only when it is already known: the menu never waits for a check.
            var count = _duplicateReport?.CopyOf(playing.Source) is { } song
                ? DuplicateFinder.Related(song, _duplicateReport.Copies, _duplicateReport.Groups, scanner.Store).Count : 0;
            yield return (count > 0 ? $"Find Copies and Similar ({count})..." : "Find Copies and Similar...", () => _ = FindCopies(playing.Source));
        }

        var progress = scanner.Progress;
        yield return (progress.IsActive ? "Find Duplicates (checking)..." : "Find Duplicates...", () => _ = FindDuplicates());
        yield return ("Removed Files...", () => _ = ShowRemovedFiles());
    }
}

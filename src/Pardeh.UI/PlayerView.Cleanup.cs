using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;
using Pardeh.Core.Library;
using Pardeh.UI.CleanupViews;

namespace Pardeh.UI;

/// <summary>Local cleanup: the background checks, the "Needs cleanup" marks, Fix Info and Clean Up Library.</summary>
internal sealed partial class PlayerView
{
    private CleanupScanner? _cleanup;

    /// <summary>The phone's open full-screen panels, topmost last: Back closes that one.</summary>
    private readonly List<Action> _openPanels = [];

    private DispatcherTimer? _cleanupSoon;
    private long _cleanupSeen;
    private HashSet<string> _marked = [];
    private HashSet<string> _matched = [];
    private bool _fixingInfo;

    private void StartCleanup()
    {
        var store = new CleanupStore(Path.Combine(AppPaths.DataDirectory, "cleanup.json"));
        _cleanup = new CleanupScanner(_library, new Pardeh.Nava.TrackFactsReader(), store, Path.Combine(AppPaths.DataDirectory, "cleanup-cache.json"));
        _cleanup.Changed += (_, _) => Dispatcher.UIThread.Post(CleanupResultsChanged);
        store.Changed += (_, _) => Dispatcher.UIThread.Post(CleanupResultsChanged);
        // Checks follow the library: some seconds after it changes, and only when the songs themselves did.
        _cleanupSoon = new DispatcherTimer(TimeSpan.FromSeconds(OperatingSystem.IsAndroid() ? 6 : 3), DispatcherPriority.Background, (_, _) => CheckLibrary());
        _cleanupSoon.Stop();
        _library.Changed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            _cleanupSoon.Stop();
            _cleanupSoon.Start();
        });
        _cleanupSoon.Start();
        StartOnlineInfo();
        StartDuplicates();
    }

    private void CheckLibrary()
    {
        _cleanupSoon?.Stop();
        if (_library.IsScanning || _cleanup is null)
        {
            _cleanupSoon?.Start();
            return;
        }

        // Play counts and ratings change the library too; what the checks look at is the files.
        long signature = 17;
        foreach (var item in _library.Items)
            signature = unchecked(signature * 31 + HashCode.Combine(item.Path, item.FileSize, item.Modified));
        if (signature == _cleanupSeen)
            return;
        _cleanupSeen = signature;
        _online?.Keep(_library.Items.Select(i => i.Path).ToHashSet(StringComparer.Ordinal));
        _ = _cleanup.ScanAsync();
    }

    private void CleanupResultsChanged()
    {
        if (_cleanup is null)
            return;
        _batchView?.Fill();
        _checkView?.Fill();
        UpdateMarks();
    }

    // Marks: songs that need cleaning up, and songs with a match found online.
    private void UpdateMarks()
    {
        if (_cleanup is null)
            return;
        var marked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_cleanup.Store.ShowBadges)
        {
            // Saved matches show only while look-ups are on: that is when Fix Info shows them.
            var online = AppSettings.Current.FixInfoOnline;
            foreach (var item in _library.Items)
            {
                var state = StateOf(item.Path, online ? SavedFor(item) : null);
                if (state is Core.OnlineInfo.FixInfoState.NeedsCleanup or Core.OnlineInfo.FixInfoState.MatchFound)
                    marked.Add(item.Path);
                if (state == Core.OnlineInfo.FixInfoState.MatchFound)
                    matched.Add(item.Path);
            }
        }

        if (marked.SetEquals(_marked) && matched.SetEquals(_matched))
            return;
        _marked = marked;
        _matched = matched;
        _phoneLibrary?.Reload();
        _canvas.InvalidateVisual();
    }

    private bool NeedsCleanup(string source) => _marked.Contains(source);

    private bool HasOnlineMatch(string source) => _matched.Contains(source);

    private bool CanFixInfo(string source) => _changes?.WhyUnchangeable(source) is null && !MediaLibrary.VideoExtensions.Contains(Path.GetExtension(MediaSources.DisplayName(source)));

    // ---- panels: a window on the desktop, the whole screen on a phone ---------------------------------

    private CleanupInk Ink => Shell is null ? CleanupInk.Phone : CleanupInk.Desktop;

    private readonly List<Window> _panelWindows = [];

    /// <summary>The window a question belongs to: the cleanup window on top, else the player's.</summary>
    private TopLevel? DialogOwner => _panelWindows.Count > 0 ? _panelWindows[^1] : Shell;

    /// <summary>Shows a screen and completes when it is closed (by itself through the action given to it, or by the user).</summary>
    private Task ShowPanel(string title, Func<Action, Control> build, double width = 760, double height = 600)
    {
        if (Shell is { } shell)
        {
            var over = DialogOwner as Window ?? shell;
            var window = new Window
            {
                Title = title, Width = width, Height = height, MinWidth = 420, MinHeight = 320, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var view = build(window.Close);
            window.Content = new Border { Padding = new Thickness(14), Child = view };
            Rendering.UiTheme.Dialog(window);
            _panelWindows.Add(window);
            window.Closed += (_, _) => _panelWindows.Remove(window);
            return window.ShowDialog(over);
        }

        var done = new TaskCompletionSource();
        var root = _session?.CurrentView?.Root;
        Border? card = null;
        ThemeVariantScope? host = null;
        Action close = null!;
        close = () =>
        {
            _openPanels.Remove(close);
            if (host is not null)
                _overlay.Children.Remove(host);
            done.TrySetResult();
        };
        card = new Border
        {
            Width = root?.Width ?? 360, Height = root?.Height ?? 640, Padding = new Thickness(12),
            Background = new SolidColorBrush(Color.Parse("#FF1B1F2A")),
        };
        var inside = new DockPanel();
        var heading = CleanupInk.Phone.Label(title, 17, bold: true);
        heading.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(heading, Dock.Top);
        inside.Children.Add(heading);
        inside.Children.Add(build(close));
        card.Child = inside;
        host = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Dark, Child = card };
        _openPanels.Add(close);
        _overlay.Children.Add(host);
        return done.Task;
    }

    // ---- Fix Info for one song --------------------------------------------------------------------

    private async Task<ReviewResult> ReviewSong(CleanupProposal proposal, TagFields current)
    {
        var result = new ReviewResult(ReviewAnswer.Cancel);
        ReviewView? view = null;
        var saved = _library.Find(proposal.Source) is { } item ? SavedFor(item) : null;
        var state = Core.OnlineInfo.FixInfoStates.Of(_cleanup!.Store.IsIgnored(proposal.Source), proposal, saved);
        await ShowPanel("Fix Info", close => view = new ReviewView(Ink, proposal, current, _cleanup!.Store, EditPatterns, answer =>
        {
            result = answer;
            close();
        }, OnlineFor(proposal, current), Core.OnlineInfo.FixInfoStates.Describe(state, saved)));
        view?.Online?.Stop();
        return result;
    }

    // Looks at the song as it is now: the scan's result may be older than the file.
    private async Task<(CleanupProposal Proposal, TagFields Tags)?> Look(string source)
    {
        var changes = _changes!;
        var tags = await changes.ReadTagsAsync(source);
        var proposal = await Task.Run(() => _cleanup!.Analyze(source));
        return proposal is null ? null : (proposal, tags);
    }

    private async Task FixInfo(string source)
    {
        if (_changes is not { } changes || _cleanup is not { } cleanup || _fixingInfo)
            return;
        _fixingInfo = true;
        try
        {
            if (changes.WhyUnchangeable(source) is { } why)
            {
                ShowMessage("This song can't be changed. " + why);
                return;
            }

            if (await Look(source) is not var (proposal, tags))
            {
                ShowMessage("This file's information can't be read.");
                return;
            }

            var answer = await ReviewSong(proposal, tags);
            if (answer.Answer == ReviewAnswer.Ignore)
            {
                cleanup.Store.SetIgnored(source, true);
                return;
            }

            if (answer.Answer != ReviewAnswer.Apply || answer.Choice is not { } choice)
                return;
            var plan = await CleanupPlanner.PlanAsync(changes, [choice]);
            if (plan.IsEmpty)
            {
                ShowMessage(plan.Problems.Count > 0 ? plan.Problems[0] : "Nothing to change.");
                return;
            }

            // Asked for again by the user, so no longer left alone.
            cleanup.Store.SetIgnored(source, false);
            var name = MediaSources.DisplayName(source);
            var result = await Apply("Fix info: " + name, plan, AskBeforeInterrupting());
            if (answer.Used is { } used)
                RememberApplied(source, result, used);
            var failed = result.Results.Where(r => r.Status is not (ResultStatus.Done or ResultStatus.Unchanged)).ToList();
            if (failed.Count > 0)
            {
                ShowMessage((failed.Count == result.Results.Count ? "Nothing was changed. " : "Some of it could not be changed. ") + failed[0].Message);
            }
            else if (Shell is null && result.BatchId is { } batch
                && !await AskInView("Fixed", plan.Summary, "OK", "Undo", onBack: true))
            {
                await Undo(batch);
            }
            else if (Shell is not null)
            {
                _player.Announce("Fixed: " + name);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TagEditException or NotSupportedException)
        {
            ShowMessage("This song's information could not be fixed. " + ex.Message);
        }
        finally
        {
            _fixingInfo = false;
        }
    }

    private async Task<BatchResult> Apply(string description, CleanupPlan plan, ChangeOptions options)
    {
        using var paused = _cleanup!.Pause();
        using var held = _check?.Hold();
        return await _changes!.ApplyAsync(description, plan.Requests, options); // its Changed event has the files looked at again
    }

    // ---- Clean Up Library -------------------------------------------------------------------------

    private BatchView? _batchView;
    private Dictionary<string, Core.OnlineInfo.OnlineUse> _batchUsed = [];

    private async Task CleanUpLibrary()
    {
        if (_changes is null || _cleanup is not { } cleanup || _batchView is not null)
            return;
        if (!cleanup.Complete && !cleanup.Progress.Running)
            _ = cleanup.ScanAsync();
        // A review's online choice is remembered once the batch has applied it.
        var used = new Dictionary<string, Core.OnlineInfo.OnlineUse>(StringComparer.Ordinal);
        _batchUsed = used;
        await ShowPanel("Clean Up Library", close =>
        {
            BatchView? view = null;
            view = new BatchView(Ink, cleanup, async proposal =>
            {
                var result = await ReviewFromList(proposal.Source);
                if (result.Used is { } taken)
                    used[proposal.Source] = taken;
                else
                    used.Remove(proposal.Source);
                return result;
            }, EditPatterns, EditRules, choices => _ = ApplyBatch(view!, choices), close, () => _ = CheckLibraryOnline());
            _batchView = view;
            return view;
        });
        _batchView = null;
    }

    private async Task ApplyBatch(BatchView view, IReadOnlyList<CleanupChoice> choices)
    {
        if (_changes is not { } changes || _fixingInfo)
            return;
        _fixingInfo = true;
        var stop = new CancellationTokenSource();
        try
        {
            // First what it comes to, then the question, and only then the files.
            var plan = await CleanupPlanner.PlanAsync(changes, choices,
                new Progress<(int Done, int Total)>(view.Busy("Preparing", stop.Cancel)), stop.Token);
            var problems = plan.Problems.Count > 0 ? $"\n{plan.Problems.Count} can't be read and will be left alone." : string.Empty;
            if (plan.IsEmpty)
            {
                ShowMessage("Nothing to change." + problems);
                return;
            }

            if (!await ConfirmAsync("Apply these changes?", plan.Summary + problems + (Shell is null ? "\nYou can undo them all afterwards." : "\n\nApply them? You can undo them all afterwards."),
                    "Apply", "Cancel"))
                return;
            var result = await Apply($"Clean up {plan.Files} {(plan.Files == 1 ? "song" : "songs")}", plan, AskBeforeInterrupting() with
            {
                Cancel = stop.Token,
                Progress = new Progress<(int Done, int Total)>(view.Busy("Changing files", stop.Cancel)),
            });
            foreach (var choice in choices)
                if (_batchUsed.TryGetValue(choice.Source, out var taken))
                    RememberApplied(choice.Source, result, taken);
            view.ShowList(); // before the question: nothing is going on any more
            await Report(result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage("The changes could not be made. " + ex.Message);
        }
        finally
        {
            _fixingInfo = false;
            view.ShowList();
        }
    }

    private async Task Report(BatchResult result)
    {
        var done = result.Count(ResultStatus.Done);
        var other = result.Results.Where(r => r.Status is not (ResultStatus.Done or ResultStatus.Unchanged)).ToList();
        var text = $"{done} {(done == 1 ? "change" : "changes")} made.";
        if (other.Count > 0)
        {
            text += $" {other.Count} not made:\n" + string.Join("\n", other.Take(3).Select(r => $"{MediaSources.DisplayName(r.Source)}: {r.Message}"));
            if (other.Count > 3)
                text += $"\nand {other.Count - 3} more.";
        }

        if (result.CanUndo && result.BatchId is { } batch)
        {
            // The desktop's question box has only Yes and No, so it is worded for them.
            if (!await ConfirmAsync("Finished", text + (Shell is null ? "\nUndo is also in the menu." : "\n\nKeep the changes? No undoes them now; Undo is also in the menu."), "OK", "Undo",
                    onBack: true))
                await Undo(batch);
        }
        else
        {
            ShowMessage(text);
        }
    }

    // ---- patterns and junk words ---------------------------------------------------------------------

    private async Task EditPatterns()
    {
        if (_cleanup is not { } cleanup || _changes is not { } changes)
            return;
        // Real songs to try the pattern on.
        var samples = new List<(TagFields Tags, string FileName)>();
        foreach (var item in _library.Music.Take(20))
        {
            if (samples.Count == 3)
                break;
            try
            {
                if (CanFixInfo(item.Path))
                {
                    var tags = await changes.ReadTagsAsync(item.Path);
                    // As the song will be once cleaned: that is what a rename uses.
                    var safe = cleanup.ProposalFor(item.Path)?.Suggestions.Where(s => s.Selected) ?? [];
                    var change = tags.Differences(CleanupAnalyzer.ToChange(tags, safe, CleanupAnalyzer.HashCover));
                    var cleaned = tags with { Title = change.Title ?? tags.Title, Artists = change.Artists ?? tags.Artists, Album = change.Album ?? tags.Album };
                    samples.Add((cleaned, MediaSources.DisplayName(item.Path)));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TagEditException or NotSupportedException)
            {
            }
        }

        await ShowPanel("File name patterns", close => new PatternView(Ink, cleanup.Store, samples, close), 620, 560);
    }

    private Task EditRules() =>
        _cleanup is { } cleanup ? ShowPanel("Junk words", close => new RulesView(Ink, cleanup.Store, close), 560, 540) : Task.CompletedTask;

    /// <summary>The cleanup entries of the app and File menus.</summary>
    private IEnumerable<(string Header, Action Run)> CleanupMenuEntries()
    {
        if (_changes is null || _cleanup is null)
            yield break;
        if (_player.CurrentMedia is { } playing && CanFixInfo(playing.Source))
            yield return ("Fix Info for This Song...", () => _ = FixInfo(playing.Source));
        var waiting = _cleanup.Store.ShowBadges ? _cleanup.Pending().Count : 0;
        yield return (waiting > 0 ? $"Clean Up Library ({waiting})..." : "Clean Up Library...", () => _ = CleanUpLibrary());
        yield return (CheckLibraryHeader(), () => _ = CheckLibraryOnline());
        foreach (var entry in OnlineSearchMenuEntries())
            yield return entry;
        foreach (var entry in DuplicateMenuEntries())
            yield return entry;
    }
}

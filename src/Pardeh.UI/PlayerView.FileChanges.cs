using Avalonia.Threading;
using Pardeh.Core.Changes;
using Pardeh.Nava;

namespace Pardeh.UI;

/// <summary>Changing the user's files: the change service, Edit Info and Undo.</summary>
internal sealed partial class PlayerView
{
    private FileChangeService? _changes;

    /// <summary>Set while the phone's Edit Info card is open: what Back runs.</summary>
    private Action? _closeTagEditor;

    private bool _editingInfo;

    /// <summary>
    /// Builds the change service and finishes whatever it was doing when the app last stopped. Called once the library is
    /// loaded, before it is scanned and before the queue is restored.
    /// </summary>
    private void StartFileChanges()
    {
        try
        {
            StartCleanup();
            var references = new LibraryReferences(_library, new TagMediaInfoReader())
            {
                Cleanup = _cleanup!.Store,
                Duplicates = _duplicates!.Store,
                Lyrics = _lyrics.Service.Store,
                LyricsChanged = item => _lyrics.Service.Invalidate(item),
                Playlists = _playlists,
                Favorites = _player.Favorites,
                Bookmarks = AppBookmarks.Store,
                VideoPositions = _player.VideoPositions,
                Queue = new QueueStore.SavedQueue(),
            };
            var changes = new FileChangeService(Platform.CreateFileBackends(), new TagEditor(), references,
                new ChangeJournal(Path.Combine(AppPaths.DataDirectory, "changes")), _library);
            foreach (var message in changes.Recover())
                Log("File change recovery: " + message);

            var link = new PlayerFileLink(_player);
            references.Queue = link;
            changes.Playback = link;
            changes.Changed += (_, results) =>
            {
                _cleanup?.Forget(results.SelectMany(r => new[] { r.Source, r.NewSource }).OfType<string>());
                // What was found online held for the files as they were.
                _online?.Forget(results.Where(r => r.Status != ResultStatus.Unchanged).SelectMany(r => new[] { r.Source, r.NewSource }).OfType<string>());
                _ = _cleanup?.ScanAsync(); // the library has the new details already
                _duplicates?.Forget(results.Where(r => r.Status != ResultStatus.Unchanged).SelectMany(r => new[] { r.Source, r.NewSource }).OfType<string>());
                RefreshDuplicates(now: true);
                _phoneLibrary?.FilesChanged(results);
                _lyrics.Refresh();
                _queueStore?.Save();
            };
            _changes = changes;
            if (changes.PendingRepairs.Count > 0)
                Dispatcher.UIThread.Post(() => _ = OfferRepair(changes), DispatcherPriority.Background);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Log($"File changes are not available: {ex}");
        }
    }

    // A file left half written needs the user's permission again before its original can be put back (Android).
    private async Task OfferRepair(FileChangeService changes)
    {
        await Task.Delay(TimeSpan.FromSeconds(3)); // the first screen comes first
        var waiting = changes.PendingRepairs;
        if (waiting.Count == 0)
            return;
        var what = waiting.Count == 1 ? Pardeh.Core.Library.MediaSources.DisplayName(waiting[0]) : $"{waiting.Count} files";
        if (!await ConfirmAsync("A change was interrupted",
                $"Pardeh stopped while saving {what}. Put the original back now? Your device may ask for permission.", "Repair", "Later"))
            return;
        try
        {
            var left = await changes.RepairAsync();
            ShowMessage(changes.PendingRepairs.Count == 0 ? "The original was put back." : "It could not be repaired yet. " + left.FirstOrDefault());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ShowMessage("It could not be repaired yet. " + ex.Message);
        }
    }

    private ChangeOptions AskBeforeInterrupting() => new()
    {
        MayInterruptPlayback = files => ConfirmAsync("The song is playing",
            files.Count == 1
                ? "Stop it for a moment to make the change? It carries on where it was."
                : "Some of these songs are playing. Stop them for a moment to make the change?",
            "Continue", "Not now"),
    };

    /// <summary>Edit Info: shows the file's tags, writes what the user changed, and says what happened.</summary>
    private async Task EditTags(string path)
    {
        if (_changes is not { } changes || _editingInfo)
            return;
        _editingInfo = true;
        try
        {
            if (changes.WhyUnchangeable(path) is { } why)
            {
                ShowMessage("This file's information can't be edited. " + why);
                return;
            }

            var name = Pardeh.Core.Library.MediaSources.DisplayName(path);
            var current = await changes.ReadTagsAsync(path);
            var edit = Shell is { } shell
                ? await Rendering.TagEditorDialog.Ask(shell, name, current)
                : await Rendering.InViewPrompt.EditTags(_overlay, _session?.CurrentView?.Root.Width ?? 360, _session?.CurrentView?.Root.Height ?? 640,
                    "Edit info - " + name, current, close => _closeTagEditor = close);
            _closeTagEditor = null;
            if (edit is null || current.Differences(edit).IsEmpty)
                return;

            var result = await changes.ApplyAsync("Edit info: " + name, [ChangeRequest.EditTags(path, edit)], AskBeforeInterrupting());
            var outcome = result.Results[0];
            switch (outcome.Status)
            {
                case ResultStatus.Done when Shell is null && result.BatchId is { } batch:
                    // A phone has no menu bar: the way back is offered right away (and stays in the menu).
                    if (!await AskInView("Saved", outcome.Message ?? "The new details are in the file.", "OK", "Undo", onBack: true))
                        await Undo(batch);
                    break;
                case ResultStatus.Done or ResultStatus.Unchanged:
                    if (outcome.Message is { } note)
                        ShowMessage(note);
                    break;
                case ResultStatus.Deferred:
                    ShowMessage("Nothing was changed: the song is playing.");
                    break;
                default:
                    ShowMessage("Nothing was changed. " + outcome.Message);
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TagEditException or NotSupportedException)
        {
            ShowMessage("This file's information can't be edited. " + ex.Message);
        }
        finally
        {
            _editingInfo = false;
            _closeTagEditor = null;
        }
    }

    /// <summary>The menu entry for the last change that can still be undone, or null.</summary>
    private (string Header, Action Run)? UndoFileChangeEntry()
    {
        if (_changes?.LastUndoable() is not { } last)
            return null;
        var what = last.Description.Length > 48 ? last.Description[..47] + "…" : last.Description;
        return ($"Undo \"{what}\"", () => Dispatcher.UIThread.Post(async () =>
        {
            if (await ConfirmAsync("Undo", $"Undo \"{last.Description}\"? The files get their old details, names and places back.", "Undo", "Cancel"))
                await Undo(last.Id);
        }));
    }

    private async Task Undo(string batchId)
    {
        if (_changes is not { } changes)
            return;
        try
        {
            var result = await changes.UndoAsync(batchId, AskBeforeInterrupting());
            var failed = result.Results.Where(r => r.Status is not (ResultStatus.Done or ResultStatus.Unchanged)).ToList();
            if (failed.Count == 0)
                _player.Announce("The change was undone");
            else
                ShowMessage($"{failed.Count} of {result.Results.Count} could not be undone. {failed[0].Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage("The change could not be undone. " + ex.Message);
        }
    }
}

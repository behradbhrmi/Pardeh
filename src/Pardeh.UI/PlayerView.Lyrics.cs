using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Pardeh.Core.LyricsKit;
using Pardeh.Skinning.Runtime;

namespace Pardeh.UI;

/// <summary>The lyrics view's actions: pasting, importing, going online, deleting (the LYRICS element calls <c>pardeh.command("lyrics", action)</c>).</summary>
internal sealed partial class PlayerView
{
    private readonly Pardeh.UI.LyricsKit.LyricsController _lyrics;
    private readonly Pardeh.UI.LyricsKit.LyricsFeed _lyricsFeed;

    Pardeh.UI.LyricsKit.LyricsController? Rendering.ISettingsHost.Lyrics => _lyrics;

    /// <summary>The feed the skins read (tests and the Windows head may use it).</summary>
    internal ILyricsFeed LyricsFeed => _lyricsFeed;

    private object? LyricsCommand(IReadOnlyList<object?> args)
    {
        var action = args.Count > 0 ? args[0]?.ToString() ?? string.Empty : string.Empty;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await RunLyricsAction(action);
            }
            catch (Exception ex)
            {
                Log($"Lyrics action {action} failed: {ex.Message}");
                ShowMessage("That did not work: " + ex.Message);
            }
        });
        return null;
    }

    private async Task RunLyricsAction(string action)
    {
        var settings = AppSettings.Current;
        switch (action)
        {
            case "enable":
                settings.LyricsEnabled = true;
                settings.Save();
                break;
            case "search":
                if (!settings.LyricsOnline && !await ConfirmOnline())
                    return;
                _lyrics.SearchAgain();
                break;
            case "refresh":
                _lyrics.Refresh();
                break;
            case "delete":
                if (await ConfirmAsync("Delete your lyrics?", "The lyrics you pasted or imported for this song are removed. Other lyrics (tags, files, online) show again.", "Delete", "Keep"))
                    _lyrics.DeleteOverride();
                break;
            case "paste":
                await EditLyrics();
                break;
            case "import":
                await ImportLyrics();
                break;
            case "chords":
                await EditChords();
                break;
            case "deletechords":
                if (await ConfirmAsync("Delete your chords?", "Your own chords for this song are removed. Chords from a file next to the song show again.", "Delete", "Keep"))
                    _lyrics.DeleteChords();
                break;
        }
    }

    /// <summary>The "own chords" editor: starts with the saved text, else the lyrics on screen, so chords can be typed into the words.</summary>
    private async Task EditChords()
    {
        if (_lyrics.Item is null)
        {
            ShowMessage("Play a song first.");
            return;
        }

        var current = _lyrics.Current;
        var initial = _lyrics.ChordsText ?? (current is null ? string.Empty : current.Synced ? current.ToLrc(applyOffset: false) : current.ToPlainText());
        const string hint = "Put each chord in brackets before its word: [Am]Hello [F]world";
        var text = Shell is { } shell
            ? await Rendering.PromptDialog.Ask(shell, "My chords - " + hint, initial, multiline: true)
            : await Rendering.InViewPrompt.Edit(_overlay, _session?.CurrentView?.Root.Width ?? 360, _session?.CurrentView?.Root.Height ?? 640, "My chords", hint, initial, ReadClipboard);
        if (text is null)
            return;
        if (text.Trim().Length > 0 && LrcParser.Parse(text) is not { HasChords: true })
        {
            ShowMessage("No chords were found. Write them like [Am] or [F#m7] inside the words (at least two).");
            return;
        }

        try
        {
            _lyrics.SaveChords(text);
        }
        catch (IOException ex)
        {
            ShowMessage("The chords could not be saved: " + ex.Message);
        }
    }

    /// <summary>The privacy text and a choice; turning it on is the user's own decision.</summary>
    private async Task<bool> ConfirmOnline()
    {
        const string body = "Look up lyrics online? Sends artist, title, album and duration of the playing track to lrclib.net. Nothing else.";
        if (!await ConfirmAsync("Search online?", body, "Turn on", "Not now"))
            return false;
        AppSettings.Current.LyricsOnline = true;
        AppSettings.Current.Save();
        return true;
    }

    private Task<bool> ConfirmAsync(string title, string body, string yes, string no) =>
        DialogOwner is { } owner
            ? Task.FromResult(Platform.Confirm(owner, body, title))
            : Rendering.InViewPrompt.Confirm(_overlay, _session?.CurrentView?.Root.Width ?? 360, title, body, yes, no);

    private async Task EditLyrics()
    {
        if (_lyrics.Item is null)
        {
            ShowMessage("Play a song first.");
            return;
        }

        var current = _lyrics.Current;
        var initial = current is null ? string.Empty : current.Synced ? current.ToLrc(applyOffset: false) : current.ToPlainText();
        var width = _session?.CurrentView?.Root.Width ?? 360;
        var height = _session?.CurrentView?.Root.Height ?? 640;
        var title = current is null ? "Paste lyrics" : "Edit lyrics";
        var hint = "Paste plain text, or LRC text with [mm:ss.xx] time stamps";
        string? text;
        if (Shell is { } shell)
            text = await Rendering.PromptDialog.Ask(shell, title + ":", initial, multiline: true);
        else
            text = await Rendering.InViewPrompt.Edit(_overlay, width, height, title, hint, initial, ReadClipboard);
        if (text is null)
            return;
        SaveLyricsText(text, null);
    }

    private async Task<string?> ReadClipboard() => Top?.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;

    /// <summary>Saves text (LRC or plain) as the user's lyrics for the playing track. Returns false when it held no lyrics.</summary>
    internal bool SaveLyricsText(string text, string? fileName)
    {
        var lyrics = fileName is null ? LrcParser.Parse(text, LyricsSources.Override) : SidecarLyricsSource.ParseFile(text, fileName);
        if (lyrics is null || !lyrics.HasText)
        {
            ShowMessage("There were no lyrics in that text.");
            return false;
        }

        lyrics.OffsetMs = _lyrics.OffsetMs;
        try
        {
            _lyrics.SaveOverride(lyrics);
        }
        catch (IOException ex)
        {
            ShowMessage("The lyrics could not be saved: " + ex.Message);
            return false;
        }

        return true;
    }

    private async Task ImportLyrics()
    {
        if (_lyrics.Item is null)
        {
            ShowMessage("Play a song first.");
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import lyrics or chords (.lrc, .txt, .srt, .vtt, .cho, .crd)",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Lyrics") { Patterns = ["*.lrc", "*.txt", "*.srt", "*.vtt", "*.cho", "*.chordpro", "*.crd"],MimeTypes = ["text/*", "application/x-subrip", "application/octet-stream"] }, FilePickerFileTypes.All],
        });
        if (files.FirstOrDefault() is not { } file)
            return;
        await using var stream = await file.OpenReadAsync();
        var buffer = new byte[LyricsLimits.MaxBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total))) > 0)
            total += read;
        if (total > LyricsLimits.MaxBytes)
        {
            ShowMessage("That file is too large to be lyrics.");
            return;
        }

        SaveLyricsText(LyricsDecoder.Decode(buffer.AsSpan(0, total)), file.Name);
    }
}

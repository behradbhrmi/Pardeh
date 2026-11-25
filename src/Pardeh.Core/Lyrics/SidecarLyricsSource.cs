using Pardeh.Core.Library;
using Pardeh.Core.Playback;

namespace Pardeh.Core.LyricsKit;

/// <summary>A place lyrics can be read from for a track (the file's tags, a file next to it ...). Called on a background thread.</summary>
public interface ILyricsSource
{
    /// <summary>One of <see cref="LyricsSources"/>.</summary>
    string Name { get; }

    /// <summary>The lyrics for the track, or null when this source has none. Never throws for a missing or damaged file.</summary>
    Lyrics? Read(MediaItem item, CancellationToken cancel);
}

/// <summary>
/// Lyrics from a file with the same name next to the audio file: <c>.lrc</c> first, then <c>.txt</c>, <c>.srt</c> and <c>.vtt</c>.
/// The file's encoding is detected (<see cref="LyricsDecoder"/>). A content URI (Android document tree) asks the head's
/// <see cref="MediaSources.SiblingResolver"/>; without one, such a track simply has no sidecar. Nothing is ever written here.
/// </summary>
public sealed class SidecarLyricsSource : ILyricsSource
{
    /// <summary>The chord sheets next to a track, as a source of their own (see <see cref="LyricsService.ChordSource"/>).</summary>
    public sealed class Chords : ILyricsSource
    {
        public string Name => LyricsSources.ChordFile;

        public Lyrics? Read(MediaItem item, CancellationToken cancel) => ReadChordSheet(item.Source, cancel);
    }

    /// <summary>The extensions looked for, in order of preference.</summary>
    public static readonly string[] Extensions = [".lrc", ".txt", ".srt", ".vtt"];

    public string Name => LyricsSources.Sidecar;

    public Lyrics? Read(MediaItem item, CancellationToken cancel)
    {
        foreach (var location in Find(item.Source))
        {
            cancel.ThrowIfCancellationRequested();
            var bytes = ReadLimited(location);
            if (bytes is null)
                continue;
            var lyrics = ParseFile(LyricsDecoder.Decode(bytes), location);
            if (lyrics is not null)
            {
                lyrics.Source = LyricsSources.Sidecar;
                return lyrics;
            }
        }

        return null;
    }

    /// <summary>Parses a lyrics file's text by its extension (.srt / .vtt as timed cues, anything else as LRC or plain text).</summary>
    public static Lyrics? ParseFile(string text, string fileNameOrPath)
    {
        var extension = Path.GetExtension(MediaSources.IsContent(fileNameOrPath) ? MediaSources.DisplayName(fileNameOrPath) : fileNameOrPath);
        if (extension.Equals(".srt", StringComparison.OrdinalIgnoreCase) || extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase))
            return FromCues(text);
        return LrcParser.Parse(text);
    }

    private static Lyrics? FromCues(string text)
    {
        if (text.Length > LyricsLimits.MaxBytes)
            text = text[..LyricsLimits.MaxBytes];
        var lines = Subtitles.ParseSrt(text)
            .Take(LyricsLimits.MaxLines)
            .Select(c => new LyricLine(c.Start, Lyrics.Clean(c.Text.Replace('\n', ' '))))
            .OrderBy(l => l.Time)
            .ToList();
        var lyrics = new Lyrics(lines, synced: true);
        return lyrics.HasText ? lyrics : null;
    }

    /// <summary>The sidecar files that exist for a track (paths or content URIs), best first.</summary>
    public static IReadOnlyList<string> Find(string source) => Find(source, Extensions);

    /// <summary>The files with the track's name and one of <paramref name="extensions"/> next to it, in the order of the extensions.</summary>
    public static IReadOnlyList<string> Find(string source, string[] extensions)
    {
        if (string.IsNullOrEmpty(source))
            return [];
        try
        {
            if (MediaSources.IsContent(source))
                return MediaSources.SiblingResolver?.Invoke(source, extensions) ?? [];
            if (source.Contains("://", StringComparison.Ordinal) || Path.GetDirectoryName(source) is not { Length: > 0 } folder || !Directory.Exists(folder))
                return [];

            var stem = Path.GetFileNameWithoutExtension(source);
            var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true, RecurseSubdirectories = false };
            var found = Directory.EnumerateFiles(folder, stem + ".*", options)
                .Where(f => Path.GetFileNameWithoutExtension(f).Equals(stem, StringComparison.OrdinalIgnoreCase)
                    && extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => Array.FindIndex(extensions,e => e.Equals(Path.GetExtension(f), StringComparison.OrdinalIgnoreCase)))
                .ToList();
            return found;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>The bytes of a lyrics file, or null when it cannot be read or is larger than <see cref="LyricsLimits.MaxBytes"/>.</summary>
    public static byte[]? ReadLimited(string location)
    {
        try
        {
            using var stream = MediaSources.IsContent(location) ? MediaSources.ContentOpener?.Invoke(location) : File.OpenRead(location);
            if (stream is null)
                return null;
            return ReadLimited(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The first chord sheet next to the track (<see cref="ChordNotation.Extensions"/>) that really has chords, or null.</summary>
    public static Lyrics? ReadChordSheet(string source, CancellationToken cancel = default)
    {
        foreach (var location in Find(source, ChordNotation.Extensions))
        {
            cancel.ThrowIfCancellationRequested();
            if (ReadLimited(location) is { } bytes && LrcParser.Parse(LyricsDecoder.Decode(bytes)) is { HasChords: true } sheet)
            {
                sheet.Source = LyricsSources.ChordFile;
                return sheet;
            }
        }

        return null;
    }

    /// <summary>The track's guitar tab: a <c>.tab</c> file next to it, else the tab sections of a chord sheet next to it, or null.</summary>
    public static string? ReadTab(string source, CancellationToken cancel = default)
    {
        foreach (var location in Find(source, [TabNotation.Extension]))
        {
            cancel.ThrowIfCancellationRequested();
            if (ReadLimited(location) is { } bytes && TabNotation.FromTabFile(LyricsDecoder.Decode(bytes)) is { } tab)
                return tab;
        }

        foreach (var location in Find(source, ChordNotation.Extensions))
        {
            cancel.ThrowIfCancellationRequested();
            if (ReadLimited(location) is { } bytes && TabNotation.FromChordPro(LyricsDecoder.Decode(bytes)) is { } tab)
                return tab;
        }

        return null;
    }

    internal static byte[]? ReadLimited(Stream stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > LyricsLimits.MaxBytes)
                return null;
        }

        return buffer.ToArray();
    }
}

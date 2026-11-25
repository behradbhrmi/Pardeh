using Pardeh.Core.LyricsKit;
using Pardeh.Core.Playback;
using TagLib.Id3v2;

namespace Pardeh.Nava;

/// <summary>
/// Lyrics stored inside the audio file, read with TagLib# (only the tag region of the file is read, never the audio, and a
/// content URI is read through the same seekable stream as the other tags):
/// ID3v2 SYLT (synchronised, millisecond stamps) and USLT, Vorbis comments SYNCEDLYRICS / LYRICS / UNSYNCEDLYRICS (FLAC, Ogg, Opus),
/// MP4 <c>©lyr</c>, WMA / ASF <c>WM/Lyrics</c> and APEv2 <c>Lyrics</c>. Text that holds LRC time stamps counts as synchronised.
/// </summary>
public sealed class TagLyricsSource : ILyricsSource
{
    private static readonly string[] SyncedFields = ["SYNCEDLYRICS", "LYRICS_SYNCED", "LRC"];
    private static readonly string[] TextFields = ["LYRICS", "UNSYNCEDLYRICS", "LYRICS_UNSYNCED"];

    public string Name => LyricsSources.Embedded;

    public Lyrics? Read(MediaItem item, CancellationToken cancel)
    {
        try
        {
            using var file = TagMediaInfoReader.Open(item.Source, TagLib.ReadStyle.None);
            if (file is null)
                return null;
            return Pick(Candidates(file));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unsupported or damaged file: no embedded lyrics.
            return null;
        }
    }

    /// <summary>The synchronised lyrics if any candidate is one, else the first plain one.</summary>
    internal static Lyrics? Pick(IEnumerable<Lyrics> candidates)
    {
        Lyrics? plain = null;
        foreach (var lyrics in candidates)
        {
            if (!lyrics.HasText)
                continue;
            if (lyrics.Synced)
                return lyrics;
            plain ??= lyrics;
        }

        return plain;
    }

    private static IEnumerable<Lyrics> Candidates(TagLib.File file)
    {
        // 1. Synchronised frames and fields.
        var id3 = file.GetTag(TagLib.TagTypes.Id3v2, false) as TagLib.Id3v2.Tag;
        var xiph = file.GetTag(TagLib.TagTypes.Xiph, false) as TagLib.Ogg.XiphComment;
        if (id3 is not null)
        {
            foreach (var frame in id3.GetFrames<SynchronisedLyricsFrame>())
                if (FromSylt(frame) is { } synced)
                    yield return synced;
        }

        if (xiph is not null)
        {
            foreach (var name in SyncedFields)
                foreach (var text in xiph.GetField(name))
                    if (LrcParser.Parse(text, LyricsSources.Embedded) is { } parsed)
                        yield return parsed;
        }

        // Some taggers (foobar2000, ffmpeg's "lyrics=") keep lyrics in a user text frame, TXXX, instead of USLT.
        var userText = id3 is null ? [] : id3.GetFrames<UserTextInformationFrame>().Select(f => (Kind: UserTextKind(f.Description), Text: string.Join("\n", f.Text))).ToList();
        foreach (var (kind, text) in userText)
            if (kind == 1 && FromText(text) is { Synced: true } synced)
                yield return synced;

        // 2. Text: every USLT frame, every Vorbis field, then whatever the combined tag offers (MP4 ©lyr, ASF WM/Lyrics, APE Lyrics).
        if (id3 is not null)
        {
            foreach (var frame in id3.GetFrames<UnsynchronisedLyricsFrame>())
                if (FromText(frame.Text) is { } text)
                    yield return text;
        }

        foreach (var (kind, text) in userText)
            if (kind != 0 && FromText(text) is { } fromUserText)
                yield return fromUserText;

        if (xiph is not null)
        {
            foreach (var name in TextFields)
                foreach (var value in xiph.GetField(name))
                    if (FromText(value) is { } text)
                        yield return text;
        }

        if (FromText(file.Tag.Lyrics) is { } any)
            yield return any;
    }

    /// <summary>0: not a lyrics frame; 1: a synchronised-lyrics name; 2: a lyrics name.</summary>
    private static int UserTextKind(string? description)
    {
        var name = (description ?? string.Empty).ToUpperInvariant().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
        return name switch
        {
            "SYNCEDLYRICS" or "LYRICSSYNCED" or "LRC" => 1,
            "LYRICS" or "UNSYNCEDLYRICS" or "LYRICSUNSYNCED" or "USLT" => 2, // ffmpeg writes its "lyrics" tag as a TXXX frame described "USLT"
            _ => 0,
        };
    }

    private static Lyrics? FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return LrcParser.Parse(text, LyricsSources.Embedded);
    }

    /// <summary>A SYLT frame in absolute milliseconds as synchronised lyrics (frame-counted stamps are not supported).</summary>
    private static Lyrics? FromSylt(SynchronisedLyricsFrame frame)
    {
        if (frame.Format != TimestampFormat.AbsoluteMilliseconds || frame.Text is not { Length: > 0 } entries)
            return null;
        var lines = entries
            .Take(LyricsLimits.MaxLines)
            .Select(e => new LyricLine(TimeSpan.FromMilliseconds(Math.Max(0, e.Time)), Clean(e.Text)))
            .OrderBy(l => l.Time)
            .ToList();
        var lyrics = new Lyrics(lines, synced: true) { Source = LyricsSources.Embedded };
        return lyrics.HasText ? lyrics : null;
    }

    // SYLT entries often carry the line break that ended the previous line.
    private static string Clean(string? text) => (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
}

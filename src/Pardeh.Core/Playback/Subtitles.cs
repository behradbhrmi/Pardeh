using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pardeh.Core.Playback;

/// <summary>One subtitle line (or block) and when it shows.</summary>
public sealed record SubtitleCue(TimeSpan Start, TimeSpan End, string Text);

/// <summary>Subtitles from .srt, .vtt and .ass / .ssa files (styling tags removed).</summary>
public sealed partial class Subtitles
{
    public static readonly string[] Extensions = [".srt", ".vtt", ".ass", ".ssa"];

    private readonly List<SubtitleCue> _cues;

    private Subtitles(string path, List<SubtitleCue> cues)
    {
        Path = path;
        _cues = cues.OrderBy(c => c.Start).ToList();
    }

    public string Path { get; }

    /// <summary>Subtitles read from inside the media file; <paramref name="label"/> names the track (it stands in for a path).</summary>
    public static Subtitles? FromCues(string label, IEnumerable<SubtitleCue> cues)
    {
        var list = cues.ToList();
        return list.Count > 0 ? new Subtitles(label, list) : null;
    }

    /// <summary>The text of an ASS event as FFmpeg hands it over inside a container: "ReadOrder,Layer,Style,Name,MarginL,MarginR,MarginV,Effect,Text".</summary>
    public static string TextOfAssEvent(string ev)
    {
        var fields = ev.Split(',', 9);
        return CleanText(fields.Length == 9 ? fields[8] : ev);
    }

    /// <summary>Removes styling tags ({\an8}, &lt;i&gt;) and turns ASS line breaks into real ones.</summary>
    public static string CleanText(string raw) => Tags().Replace(raw, string.Empty).Replace("\\N", "\n").Replace("\\n", "\n").Trim();

    public IReadOnlyList<SubtitleCue> Cues => _cues;

    /// <summary>The text showing at <paramref name="position"/>, or null.</summary>
    public string? TextAt(TimeSpan position)
    {
        var lines = _cues.Where(c => c.Start <= position && position < c.End).Select(c => c.Text).ToList();
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    /// <summary>A subtitle file next to the media with the same name (movie.srt, movie.en.srt …), or null.</summary>
    public static string? FindFor(string mediaPath)
    {
        if (mediaPath.Contains("://") || System.IO.Path.GetDirectoryName(mediaPath) is not { Length: > 0 } folder || !Directory.Exists(folder))
            return null;
        var name = System.IO.Path.GetFileNameWithoutExtension(mediaPath);
        try
        {
            return Directory.EnumerateFiles(folder, name + "*")
                .Where(f => Extensions.Contains(System.IO.Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => f.Length)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static Subtitles? Load(string path)
    {
        try
        {
            var text = LyricsKit.LyricsDecoder.Decode(File.ReadAllBytes(path)); // UTF-8, UTF-16 and Windows-1256 (Persian / Arabic) files
            var cues = System.IO.Path.GetExtension(path).ToLowerInvariant() is ".ass" or ".ssa" ? ParseAss(text) : ParseSrt(text);
            return cues.Count > 0 ? new Subtitles(path, cues) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"(\d+):(\d\d):(\d\d)[,.](\d{1,3})\s*-->\s*(\d+):(\d\d):(\d\d)[,.](\d{1,3})|(\d\d):(\d\d)[.](\d{3})\s*-->\s*(\d\d):(\d\d)[.](\d{3})")]
    private static partial Regex TimeLine();

    [GeneratedRegex(@"<[^>]+>|\{[^}]*\}")]
    private static partial Regex Tags();

    /// <summary>SRT and WebVTT: blocks of a time line followed by text lines.</summary>
    internal static List<SubtitleCue> ParseSrt(string text)
    {
        var cues = new List<SubtitleCue>();
        var blocks = text.Replace("\r", string.Empty).Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var block in blocks)
        {
            var lines = block.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var match = TimeLine().Match(lines[i]);
                if (!match.Success)
                    continue;
                TimeSpan start, end;
                if (match.Groups[1].Success)
                {
                    start = Time(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value);
                    end = Time(match.Groups[5].Value, match.Groups[6].Value, match.Groups[7].Value, match.Groups[8].Value);
                }
                else
                {
                    start = Time("0", match.Groups[9].Value, match.Groups[10].Value, match.Groups[11].Value);
                    end = Time("0", match.Groups[12].Value, match.Groups[13].Value, match.Groups[14].Value);
                }

                var body = Tags().Replace(string.Join("\n", lines.Skip(i + 1)), string.Empty).Trim();
                if (body.Length > 0)
                    cues.Add(new SubtitleCue(start, end, body));
                break;
            }
        }

        return cues;
    }

    /// <summary>ASS / SSA: "Dialogue:" lines; the text is the tenth field.</summary>
    internal static List<SubtitleCue> ParseAss(string text)
    {
        var cues = new List<SubtitleCue>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
                continue;
            var fields = line["Dialogue:".Length..].Split(',', 10);
            if (fields.Length < 10 || !TryAssTime(fields[1], out var start) || !TryAssTime(fields[2], out var end))
                continue;
            var body = Tags().Replace(fields[9], string.Empty).Replace("\\N", "\n").Replace("\\n", "\n").Trim();
            if (body.Length > 0)
                cues.Add(new SubtitleCue(start, end, body));
        }

        return cues;
    }

    private static bool TryAssTime(string value, out TimeSpan time) =>
        TimeSpan.TryParseExact(value.Trim(), [@"h\:mm\:ss\.ff", @"h\:mm\:ss\.fff"], CultureInfo.InvariantCulture, out time);

    private static TimeSpan Time(string h, string m, string s, string ms) =>
        new(0, int.Parse(h, CultureInfo.InvariantCulture), int.Parse(m, CultureInfo.InvariantCulture), int.Parse(s, CultureInfo.InvariantCulture),
            int.Parse(ms.PadRight(3, '0'), CultureInfo.InvariantCulture));
}

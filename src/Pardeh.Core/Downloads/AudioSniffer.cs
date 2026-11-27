using System.Text;

namespace Pardeh.Core.Downloads;

public enum SniffedKind
{
    Audio,
    WebPage,

    /// <summary>A playlist or a stream's list of parts. Pardeh makes no file out of one.</summary>
    Playlist,

    Other,
}

/// <param name="Extension">For audio: the extension the content calls for, with its dot.</param>
/// <param name="TypeName">For audio: what to call it.</param>
public sealed record Sniffed(SniffedKind Kind, string Extension = "", string TypeName = "");

/// <summary>
/// Says what a file is from its first bytes. A server's file name and type are only its word; this decides what is
/// accepted and which extension it gets, so a program or a script never keeps its own.
/// </summary>
public static class AudioSniffer
{
    /// <summary>How much of a file is enough to tell, tags in front aside.</summary>
    public const int Enough = 4096;

    private static readonly byte[] Asf = [0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C];

    private static Sniffed Audio(string extension, string name) => new(SniffedKind.Audio, extension, name);

    private static bool At(ReadOnlySpan<byte> data, int offset, string magic)
    {
        if (data.Length < offset + magic.Length)
            return false;
        for (var i = 0; i < magic.Length; i++)
            if (data[offset + i] != (byte)magic[i])
                return false;
        return true;
    }

    /// <param name="start">The first bytes of the file.</param>
    public static Sniffed Look(ReadOnlySpan<byte> start)
    {
        if (At(start, 0, "ID3") && start.Length >= 10)
        {
            // A tag in front: what follows it decides. A big cover can push that past what we have, and then it is MP3.
            var size = (start[6] & 0x7F) << 21 | (start[7] & 0x7F) << 14 | (start[8] & 0x7F) << 7 | start[9] & 0x7F;
            var after = 10 + size + ((start[5] & 0x10) != 0 ? 10 : 0);
            if (after + 4 > start.Length)
                return Audio(".mp3", "MP3");
            var rest = start[after..];
            return At(rest, 0, "fLaC") ? Audio(".flac", "FLAC")
                : IsAdts(rest) ? Audio(".aac", "AAC")
                : IsMpegFrame(rest) || rest[0] == 0 ? Audio(".mp3", "MP3") // zeros: padding after the tag
                : new Sniffed(SniffedKind.Other);
        }

        if (At(start, 0, "fLaC"))
            return Audio(".flac", "FLAC");
        if (At(start, 0, "OggS"))
        {
            var text = Encoding.Latin1.GetString(start[..Math.Min(start.Length, 200)]);
            return text.Contains("theora", StringComparison.Ordinal) || text.Contains("fishead", StringComparison.Ordinal) ? new Sniffed(SniffedKind.Other)
                : text.Contains("OpusHead", StringComparison.Ordinal) ? Audio(".opus", "Opus")
                : Audio(".ogg", "Ogg audio");
        }

        if (At(start, 0, "RIFF") && At(start, 8, "WAVE"))
            return Audio(".wav", "WAV");
        if (At(start, 0, "FORM") && (At(start, 8, "AIFF") || At(start, 8, "AIFC")))
            return Audio(".aiff", "AIFF");
        // An MP4 file may hold a film too: the full check of the file refuses one with moving pictures.
        if (At(start, 4, "ftyp"))
            return Audio(".m4a", "MP4 audio");
        if (start.StartsWith(Asf))
            return Audio(".wma", "Windows Media audio");
        if (At(start, 0, "wvpk"))
            return Audio(".wv", "WavPack");
        if (At(start, 0, "MAC "))
            return Audio(".ape", "Monkey's Audio");
        if (At(start, 0, "MPCK") || At(start, 0, "MP+"))
            return Audio(".mpc", "Musepack");
        if (IsAdts(start))
            return Audio(".aac", "AAC");
        if (IsMpegFrame(start))
            return Audio(".mp3", "MP3");
        if (start.Length > 6 && start[0] == 0x0B && start[1] == 0x77)
            return Audio(".ac3", "AC-3");

        return LookAtText(start);
    }

    private static bool IsAdts(ReadOnlySpan<byte> data) => data.Length >= 4 && data[0] == 0xFF && (data[1] & 0xF6) == 0xF0 && (data[2] & 0x3C) >> 2 < 13;

    // Eleven set bits, a real version, layer II or III, and a bit rate and sample rate that exist.
    private static bool IsMpegFrame(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && data[0] == 0xFF && (data[1] & 0xE0) == 0xE0 && (data[1] & 0x18) != 0x08
        && (data[1] & 0x06) is 0x02 or 0x04 && (data[2] & 0xF0) is not (0xF0 or 0x00) && (data[2] & 0x0C) != 0x0C;

    private static Sniffed LookAtText(ReadOnlySpan<byte> start)
    {
        var text = Encoding.Latin1.GetString(start[..Math.Min(start.Length, 1024)]).TrimStart('ï', '»', '¿', ' ', '\t', '\r', '\n', '\0');
        if (text.StartsWith("#EXTM3U", StringComparison.Ordinal) || text.StartsWith("[playlist]", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<MPD", StringComparison.Ordinal) || text.StartsWith("<asx", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("<playlist", StringComparison.OrdinalIgnoreCase) || text.StartsWith("<?wpl", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<smil", StringComparison.OrdinalIgnoreCase) || text.Contains("xspf.org", StringComparison.OrdinalIgnoreCase))
            return new Sniffed(SniffedKind.Playlist);
        return text.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) || text.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith('<') && text.Contains("<head", StringComparison.OrdinalIgnoreCase)
            ? new Sniffed(SniffedKind.WebPage)
            : new Sniffed(SniffedKind.Other);
    }

    /// <summary>The type a media store wants for the extension.</summary>
    public static string MimeType(string extension) => extension.ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".m4a" => "audio/mp4",
        ".aac" => "audio/aac",
        ".flac" => "audio/flac",
        ".ogg" or ".opus" => "audio/ogg",
        ".wav" => "audio/x-wav",
        ".aiff" => "audio/x-aiff",
        ".wma" => "audio/x-ms-wma",
        ".ac3" => "audio/ac3",
        ".wv" => "audio/x-wavpack",
        ".ape" => "audio/x-ape",
        ".mpc" => "audio/x-musepack",
        _ => "application/octet-stream",
    };
}

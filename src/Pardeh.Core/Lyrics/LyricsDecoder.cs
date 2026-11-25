using System.Text;

namespace Pardeh.Core.LyricsKit;

/// <summary>
/// Turns the bytes of a lyrics file into text without being told the encoding: UTF-8 (with or without a BOM), UTF-16 (BOM, or
/// recognised by its zero bytes), and for old files Windows-1256 (Persian, Arabic) or Windows-1252 (Western European).
/// </summary>
public static class LyricsDecoder
{
    private static readonly Lazy<Encoding?> Win1256 = new(() => TryGet(1256));
    private static readonly Lazy<Encoding?> Win1252 = new(() => TryGet(1252));

    private static Encoding? TryGet(int codePage)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    public static string Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return string.Empty;

        // Byte order marks.
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return Encoding.UTF8.GetString(data[3..]);
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            return Encoding.Unicode.GetString(data[2..]);
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(data[2..]);

        // UTF-16 without a BOM: mostly Latin text has a zero byte in every other position.
        if (data.Length >= 4)
        {
            var even = 0;
            var odd = 0;
            var sample = Math.Min(data.Length, 512);
            for (var i = 0; i < sample; i++)
            {
                if (data[i] != 0)
                    continue;
                if ((i & 1) == 0)
                    even++;
                else
                    odd++;
            }

            var pairs = sample / 2;
            if (odd > pairs * 0.3 && even == 0)
                return Encoding.Unicode.GetString(data);
            if (even > pairs * 0.3 && odd == 0)
                return Encoding.BigEndianUnicode.GetString(data);
        }

        // Strict UTF-8 accepts plain ASCII and every real UTF-8 text, and almost never a legacy 8-bit one.
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data);
        }
        catch (DecoderFallbackException)
        {
        }

        return DecodeLegacy(data);
    }

    /// <summary>A legacy 8-bit file: Windows-1256 when most of its non-ASCII bytes are Arabic letters, else Windows-1252.</summary>
    private static string DecodeLegacy(ReadOnlySpan<byte> data)
    {
        var west = Win1252.Value;
        var arabic = Win1256.Value;
        if (arabic is null || west is null)
            return Encoding.Latin1.GetString(data);

        var high = 0;
        foreach (var b in data)
            if (b >= 0x80)
                high++;

        var asArabic = arabic.GetString(data);
        var letters = 0;
        foreach (var c in asArabic)
            if (c is >= '؀' and <= 'ۿ')
                letters++;

        return high > 0 && letters >= high * 0.5 ? asArabic : west.GetString(data);
    }
}

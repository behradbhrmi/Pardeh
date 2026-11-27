using System.Text;

namespace Pardeh.Core.Cleanup;

/// <summary>
/// Persian (and Arabic) text that an old program stored or read with the wrong character set: "Ø³Ù„Ø§Ù…" (UTF-8 read as
/// Western) and "ÓáÇã" (Windows-1256 read as Western). A repair is only ever offered with a preview, never applied unasked.
/// </summary>
public static class Mojibake
{
    private static readonly Lazy<Encoding?> Western = new(() => CodePage(1252));
    private static readonly Lazy<Encoding?> Arabic = new(() => CodePage(1256));

    private static Encoding? CodePage(int number)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(number, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    private static bool IsArabicLetter(char c) => c is >= '؀' and <= 'ۿ' && char.IsLetter(c);

    /// <summary>The repaired text, or null when the text does not look broken.</summary>
    public static string? Repair(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Any(IsArabicLetter))
            return null;
        // Broken text is mostly signs above ASCII; real Western text only has a few accented letters among plain ones.
        var visible = text.Count(c => !char.IsWhiteSpace(c) && !(char.IsAscii(c) && !char.IsAsciiLetter(c)));
        var high = text.Count(c => c >= '\u0080');
        if (high < 3 || visible == 0 || high < visible * 0.7)
            return null;
        if (Bytes(text) is not { } bytes)
            return null;

        try
        {
            if (Check(new UTF8Encoding(false, true).GetString(bytes)) is { } fromUtf8)
                return fromUtf8;
        }
        catch (DecoderFallbackException)
        {
        }

        try
        {
            // Any bytes read as Windows-1256 give Arabic letters, so the result must also read like words: Persian and Arabic
            // text of any length has one of the commonest letters in it.
            return Arabic.Value is { } arabic && Check(arabic.GetString(bytes)) is { } read && read.IndexOfAny(['ا', 'ل', 'ی', 'ي', 'م', 'ن', 'و']) >= 0
                ? read
                : null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    // The bytes the wrong reading came from. Positions Windows-1252 leaves empty read as the control character of the same number.
    private static byte[]? Bytes(string text)
    {
        var bytes = new byte[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c <= 'ÿ')
            {
                bytes[i] = (byte)c;
                continue;
            }

            try
            {
                if (Western.Value?.GetBytes([c]) is not [var one])
                    return null;
                bytes[i] = one;
            }
            catch (EncoderFallbackException)
            {
                return null;
            }
        }

        return bytes;
    }

    private static string? Check(string candidate)
    {
        var above = candidate.Count(c => c >= '\u0080' && c != '‌');
        var arabic = candidate.Count(IsArabicLetter);
        return arabic >= 2 && arabic >= above * 0.8 && !candidate.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')) ? candidate : null;
    }
}

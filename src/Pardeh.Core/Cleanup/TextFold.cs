using System.Globalization;
using System.Text;

namespace Pardeh.Core.Cleanup;

/// <summary>
/// Text made comparable: Persian and Arabic forms of the same letter and digit, half-spaces, vowel marks and case. Only
/// for comparing; what is saved is never folded. (The lyrics key has its own, older folding, which must not change.)
/// </summary>
public static class TextFold
{
    /// <summary>One character, folded. The length of a text never changes, so places in it stay valid.</summary>
    public static char Char(char c) => c switch
    {
        'ي' or 'ى' => 'ی', // Arabic yeh forms -> Persian yeh
        'ك' => 'ک', // Arabic kaf -> Persian keheh
        >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
        >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
        '‌' => ' ', // half-space
        _ => char.ToLowerInvariant(c),
    };

    public static string SameLength(string text) => string.Create(text.Length, text, static (span, source) =>
    {
        for (var i = 0; i < source.Length; i++)
            span[i] = Char(source[i]);
    });

    /// <summary>For equality: folded, without vowel marks, punctuation runs and extra spaces.</summary>
    public static string Key(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var result = new StringBuilder(text.Length);
        var space = false;
        foreach (var raw in text.Normalize(NormalizationForm.FormKC))
        {
            var c = Char(raw);
            if (char.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark || c == 'ـ')
                continue;
            if (char.IsLetterOrDigit(c))
            {
                if (space && result.Length > 0)
                    result.Append(' ');
                space = false;
                result.Append(c);
            }
            else
            {
                space = true;
            }
        }

        return result.ToString();
    }
}

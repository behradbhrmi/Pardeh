namespace Pardeh.Ffmpeg;

/// <summary>Reading what a file says about its streams, for sound and picture alike.</summary>
internal static class FfmpegMetadata
{
    internal static string LanguageName(string code)
    {
        // ISO 639-2/B codes some files use, mapped to the /T codes .NET knows.
        code = code.ToLowerInvariant() switch
        {
            "per" => "fas", "ger" => "deu", "fre" => "fra", "chi" => "zho", "dut" => "nld", "cze" => "ces",
            "gre" => "ell", "arm" => "hye", "geo" => "kat", "ice" => "isl", "mac" => "mkd", "may" => "msa",
            "bur" => "mya", "rum" => "ron", "slo" => "slk", "tib" => "bod", "wel" => "cym", "alb" => "sqi", "baq" => "eus",
            var other => other,
        };
        try
        {
            var culture = System.Globalization.CultureInfo.GetCultures(System.Globalization.CultureTypes.NeutralCultures)
                .FirstOrDefault(c => c.ThreeLetterISOLanguageName.Equals(code, StringComparison.OrdinalIgnoreCase)
                    || c.TwoLetterISOLanguageName.Equals(code, StringComparison.OrdinalIgnoreCase));
            return culture is null || culture.Equals(System.Globalization.CultureInfo.InvariantCulture) ? code : culture.EnglishName;
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            return code;
        }
    }
}

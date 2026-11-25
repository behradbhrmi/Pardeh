using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Pardeh.Core.Playback;

namespace Pardeh.Core.LyricsKit;

/// <summary>
/// What identifies a track for lyrics, independent of where its file lives: title, artist, album and the length in whole seconds.
/// Its <see cref="Hash"/> names the track's files in the lyrics folder.
/// </summary>
public sealed record LyricsTrackKey(string Title, string Artist, string Album, int DurationSeconds)
{
    public static LyricsTrackKey From(MediaItem item) => new(
        Normalize(item.GetAttribute("Title")),
        Normalize(item.GetAttribute("Author")),
        Normalize(item.GetAttribute("Album")),
        (int)Math.Round(item.Duration.TotalSeconds));

    /// <summary>32 hex characters (SHA-256 of the normalised fields, cut to 128 bits), stable across runs and machines.</summary>
    public string Hash
    {
        get
        {
            var text = string.Create(CultureInfo.InvariantCulture, $"{Title}\n{Artist}\n{Album}\n{DurationSeconds}");
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16)).ToLowerInvariant();
        }
    }

    /// <summary>Compatibility form, lower case, one space between words; Arabic yeh / kaf variants unified (so the same Persian title matches however it was typed).</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var text = value.Normalize(NormalizationForm.FormKC)
            .Replace('ي', 'ی') // Arabic yeh -> Persian yeh
            .Replace('ى', 'ی')
            .Replace('ك', 'ک') // Arabic kaf -> Persian keheh
            .ToLowerInvariant();
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

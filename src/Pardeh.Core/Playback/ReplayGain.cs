using System.Globalization;

namespace Pardeh.Core.Playback;

/// <summary>Which ReplayGain tag evens out the loudness: none, each track's own, or its album's (keeps the album's loudness balance).</summary>
public enum ReplayGainMode
{
    Off,
    Track,
    Album,
}

/// <summary>ReplayGain: the loudness correction stored in the tags of a file ("-6.50 dB"), turned into a volume factor.</summary>
public static class ReplayGain
{
    /// <summary>The largest boost applied (+12 dB): a quiet file is lifted, never more.</summary>
    public const double MaxFactor = 3.98;

    /// <summary>The attribute names the tag reader fills on a <see cref="MediaItem"/>.</summary>
    public const string TrackGain = "ReplayGainTrackGain", TrackPeak = "ReplayGainTrackPeak", AlbumGain = "ReplayGainAlbumGain", AlbumPeak = "ReplayGainAlbumPeak";

    /// <summary>"-6.50 dB", "+3.2", "0.98" → the number; NaN when it is not one.</summary>
    public static double ParseDb(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return double.NaN;
        var cleaned = text.Replace("dB", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : double.NaN;
    }

    /// <summary>
    /// The amplitude factor for a gain of <paramref name="gainDb"/> decibels. A boost never lifts the file's <paramref name="peak"/> (1 = full scale)
    /// above full scale (that would clip); a missing gain gives 1 (unchanged).
    /// </summary>
    public static double Factor(double gainDb, double peak)
    {
        if (double.IsNaN(gainDb))
            return 1;
        var factor = Math.Pow(10, gainDb / 20);
        if (double.IsFinite(peak) && peak > 0 && factor * peak > 1)
            factor = 1 / peak;
        return Math.Clamp(factor, 0.05, MaxFactor);
    }

    /// <summary>The factor for an item under <paramref name="mode"/>: the album gain when asked and present, else the track's, else 1.</summary>
    public static double FactorFor(MediaItem? item, ReplayGainMode mode)
    {
        if (item is null || mode == ReplayGainMode.Off)
            return 1;
        double Read(string key) => ParseDb(item.GetAttribute(key));
        var album = mode == ReplayGainMode.Album ? Read(AlbumGain) : double.NaN;
        return !double.IsNaN(album) ? Factor(album, Read(AlbumPeak)) : Factor(Read(TrackGain), Read(TrackPeak));
    }
}

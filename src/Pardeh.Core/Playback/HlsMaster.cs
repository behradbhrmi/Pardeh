using System.Globalization;
using System.Text.RegularExpressions;

namespace Pardeh.Core.Playback;

/// <summary>What to open from an HLS master playlist: the playlist with the picture (null for sound only) and the one to take the sound from.</summary>
public sealed record HlsChoice(string? Picture, string Sound, int Height);

/// <summary>
/// An HLS master playlist offers the same video in several sizes. Given the whole list, FFmpeg opens and probes every one of
/// them before it plays anything, which takes tens of seconds over the internet. Pardeh reads the list itself and opens only
/// what it will play: one size for the picture, and for the sound the separate sound playlist, or else the smallest size
/// that carries the same kind of sound (so the big one is not downloaded twice).
/// </summary>
public static partial class HlsMaster
{
    private sealed record Variant(string Url, long Bandwidth, int Height, string Codecs, string? AudioGroup);

    private sealed record Rendition(string Group, string Url, bool Default);

    [GeneratedRegex("""([A-Z0-9-]+)=("[^"]*"|[^,]*)""")]
    private static partial Regex Attribute();

    /// <param name="address">Where the list came from: relative entries are resolved against it.</param>
    /// <param name="text">The list.</param>
    /// <param name="maxHeight">The tallest picture to ask for; a smaller one is taken when there is none this size or under.</param>
    /// <returns>Null when this is not a master playlist (a plain list of segments is opened as it is).</returns>
    public static HlsChoice? Choose(string address, string text, int maxHeight)
    {
        if (!text.TrimStart('﻿', ' ', '\r', '\n').StartsWith("#EXTM3U", StringComparison.Ordinal) || !Uri.TryCreate(address, UriKind.Absolute, out var home))
            return null;
        var variants = new List<Variant>();
        var renditions = new List<Rendition>();
        Dictionary<string, string>? pending = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal))
            {
                pending = Attributes(line);
            }
            else if (line.StartsWith("#EXT-X-MEDIA:", StringComparison.Ordinal))
            {
                var media = Attributes(line);
                if (media.GetValueOrDefault("TYPE") == "AUDIO" && media.TryGetValue("GROUP-ID", out var group)
                    && media.TryGetValue("URI", out var uri) && Resolve(home, uri) is { } resolved)
                {
                    renditions.Add(new Rendition(group, resolved, media.GetValueOrDefault("DEFAULT") == "YES"));
                }
            }
            else if (pending is not null && line.Length > 0 && line[0] != '#')
            {
                if (Resolve(home, line) is { } url)
                {
                    _ = long.TryParse(pending.GetValueOrDefault("BANDWIDTH"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bandwidth);
                    var size = pending.GetValueOrDefault("RESOLUTION")?.Split('x');
                    var height = size is { Length: 2 } && int.TryParse(size[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) ? h : 0;
                    variants.Add(new Variant(url, bandwidth, height, pending.GetValueOrDefault("CODECS") ?? string.Empty, pending.GetValueOrDefault("AUDIO")));
                }

                pending = null;
            }
        }

        if (variants.Count == 0)
            return null;

        // Sizes with a picture. A list that names no sizes and no codecs at all is taken to be video.
        var pictured = variants.Where(v => v.Height > 0 || HasPictureCodec(v.Codecs)).ToList();
        if (pictured.Count == 0 && variants.All(v => v.Codecs.Length == 0))
            pictured = variants;
        if (pictured.Count == 0)
        {
            var best = variants.MaxBy(v => v.Bandwidth)!;
            return new HlsChoice(null, SeparateSound(renditions, best) ?? best.Url, 0);
        }

        // H.264 and AAC where there is a choice: every device decodes them cheaply.
        pictured = Prefer(pictured, v => v.Codecs.Contains("avc1", StringComparison.Ordinal));
        pictured = Prefer(pictured, v => v.Codecs.Contains("mp4a", StringComparison.Ordinal));
        var fitting = pictured.Where(v => v.Height <= maxHeight).ToList();
        var height2 = fitting.Count > 0 ? fitting.Max(v => v.Height) : pictured.Min(v => v.Height);
        var sameHeight = pictured.Where(v => v.Height == height2).ToList();
        // Several data rates for one size: the lowest is the one a modest connection and processor keep up with. Without sizes, the data rate is all there is to go by.
        var picture = height2 > 0 ? sameHeight.MinBy(v => v.Bandwidth)! : sameHeight.MaxBy(v => v.Bandwidth)!;

        if (SeparateSound(renditions, picture) is { } separate)
            return new HlsChoice(picture.Url, separate, picture.Height);

        var sound = AudioCodec(picture.Codecs) is { } codec
            ? variants.Where(v => AudioCodec(v.Codecs) == codec).MinBy(v => v.Bandwidth)!
            : picture;
        return new HlsChoice(picture.Url, sound.Url, picture.Height);
    }

    private static string? SeparateSound(List<Rendition> renditions, Variant variant)
    {
        if (variant.AudioGroup is not { } group)
            return null;
        var inGroup = renditions.Where(r => r.Group == group).ToList();
        return (inGroup.FirstOrDefault(r => r.Default) ?? inGroup.FirstOrDefault())?.Url;
    }

    private static List<Variant> Prefer(List<Variant> from, Func<Variant, bool> wanted)
    {
        var some = from.Where(wanted).ToList();
        return some.Count > 0 ? some : from;
    }

    private static bool HasPictureCodec(string codecs) =>
        codecs.Split(',').Any(c => c.Trim() is var name
            && (name.StartsWith("avc", StringComparison.Ordinal) || name.StartsWith("hvc", StringComparison.Ordinal) || name.StartsWith("hev", StringComparison.Ordinal)
                || name.StartsWith("vp0", StringComparison.Ordinal) || name.StartsWith("av01", StringComparison.Ordinal)));

    private static string? AudioCodec(string codecs) =>
        codecs.Split(',').Select(c => c.Trim()).FirstOrDefault(c =>
            c.StartsWith("mp4a", StringComparison.Ordinal) || c is "ac-3" or "ec-3" or "opus" or "flac");

    private static Dictionary<string, string> Attributes(string line) =>
        Attribute().Matches(line[(line.IndexOf(':') + 1)..])
            .GroupBy(m => m.Groups[1].Value)
            .ToDictionary(g => g.Key, g => g.First().Groups[2].Value.Trim('"'));

    private static string? Resolve(Uri home, string entry) =>
        Uri.TryCreate(home, entry, out var uri) && uri.Scheme is "http" or "https" ? uri.ToString() : null;
}

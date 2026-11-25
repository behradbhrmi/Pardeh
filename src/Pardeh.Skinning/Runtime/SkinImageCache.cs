using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Model;

namespace Pardeh.Skinning.Runtime;

/// <summary>Decodes and caches the skin's images, keyed by file name and transparency colour.</summary>
public sealed class SkinImageCache(SkinPackage package, IImageDecoder decoder, Action<string> log, Func<Pardeh.Skinning.Wmp.WmpResources?>? resources = null)
{
    private readonly Dictionary<(string Name, int? Key), SkinBitmap?> _cache = [];

    private readonly Dictionary<(string Name, int? Key, double Hue, double Saturation), SkinBitmap?> _shifted = [];

    /// <summary>An image with the colour chooser's hue shift (degrees) and saturation (multiplier) applied.</summary>
    public SkinBitmap? Get(string? name, string? transparencyColor, double hueShift, double saturation)
    {
        var bitmap = Get(name, transparencyColor);
        if (bitmap is null || (Math.Abs(hueShift % 360) < 1e-6 && Math.Abs(saturation - 1) < 1e-6))
            return bitmap;

        var key = (name!.Trim().ToLowerInvariant(), SkinValue.ParseColor(transparencyColor), Math.Round(hueShift, 2), Math.Round(saturation, 3));
        if (!_shifted.TryGetValue(key, out var shifted))
        {
            // Dragging the chooser's sliders makes many variants; keep the cache bounded.
            if (_shifted.Count > 2000)
                _shifted.Clear();
            shifted = bitmap.WithHueSaturation(hueShift, saturation);
            _shifted[key] = shifted;
        }

        return shifted;
    }

    public SkinBitmap? Get(string? name, string? transparencyColor)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        // "auto" (a classic-skin value) means: the colour of the image's top-left pixel is transparent.
        var auto = string.Equals(transparencyColor?.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
        var colorKey = auto ? -2 : SkinValue.ParseColor(transparencyColor);
        var cacheKey = (name.Trim().ToLowerInvariant(), colorKey);
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        SkinBitmap? bitmap;
        if (name.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
        {
            // The player's own images when the user has imported them; Pardeh's stand-ins otherwise.
            var original = resources?.Invoke()?.GetImage(name);
            bitmap = original is null ? ResourceImages.Get(name) : decoder.Decode(original);
            if (bitmap is not null && original is not null && colorKey is not null)
                bitmap = bitmap.WithColorKey(colorKey == -2 ? bitmap.GetRgb(0, 0) : colorKey.Value);
        }
        else
        {
            var data = package.ReadFile(name.Trim());
            if (data is null)
            {
                log($"Image not found in skin: {name}");
                bitmap = null;
            }
            else
            {
                bitmap = decoder.Decode(data);
                if (bitmap is not null && PngDensity.Read(data) is > 1 and var density)
                    bitmap = bitmap.WithScale(density); // opt-in high-resolution art (marker chunk); ordinary images stay at 1
                if (bitmap is null)
                    log($"Unsupported image format: {name}");
                else if (colorKey is { } key)
                    bitmap = bitmap.WithColorKey(key == -2 ? bitmap.GetRgb(0, 0) : key);
            }
        }

        _cache[cacheKey] = bitmap;
        return bitmap;
    }
}

/// <summary>
/// Original replacements for the small built-in images classic skins borrow from the player
/// (res://wmploc/RT_IMAGE/#id): the green play / pause / stop status icons.
/// </summary>
public static class ResourceImages
{
    private const int Size = 11;
    private const int Green = 0x00FF00;

    public static SkinBitmap? Get(string reference)
    {
        var hash = reference.LastIndexOf('#');
        if (hash < 0 || !int.TryParse(reference.AsSpan(hash + 1), out var id))
            return null;

        return id switch
        {
            2024 => Draw((x, y) => x is >= 2 and <= 8 && y is >= 2 and <= 8),                              // stopped
            2023 => Draw((x, y) => y is >= 2 and <= 8 && (x is >= 2 and <= 4 || x is >= 6 and <= 8)),      // paused
            1784 or (>= 1770 and <= 1781) => Draw((x, y) => x >= 2 && x <= 2 + (5 - Math.Abs(y - 5)) * 6 / 5 && y is >= 0 and <= 10), // playing
            _ => null,
        };
    }

    private static SkinBitmap Draw(Func<int, int, bool> inside)
    {
        var pixels = new byte[Size * Size * 4];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (!inside(x, y))
                    continue;
                var i = (y * Size + x) * 4;
                pixels[i] = unchecked((byte)Green);
                pixels[i + 1] = unchecked((byte)(Green >> 8));
                pixels[i + 2] = unchecked((byte)(Green >> 16));
                pixels[i + 3] = 255;
            }
        }

        return new SkinBitmap(Size, Size, pixels);
    }
}

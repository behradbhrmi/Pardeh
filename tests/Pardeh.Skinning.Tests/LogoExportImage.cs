using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Pardeh.UI.FullMode;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// Everything that is a copy of the picture logo, made from the master PNG of a <see cref="LogoVariant"/>:
/// the Android launcher layers (foreground, monochrome, tile), the large notification icon, the status-bar icon
/// at five densities, the Windows .ico and the 512 px PNG. <c>scripts/export-logo-image.ps1</c> writes them
/// (<see cref="LogoTests.Export_logo_files"/>); <see cref="LogoTests"/> checks the committed files against what this makes.
/// </summary>
internal static partial class LogoExport
{
    /// <summary>The adaptive-icon layers are 108 dp; 4 px per dp.</summary>
    public const int LayerPx = 432;

    /// <summary>The radius (dp) the mark is scaled to inside a 108 dp layer: the safe zone is a 66 dp circle (radius 33).</summary>
    public const double MarkRadiusDp = 32.5;

    /// <summary>The status icon sizes by density folder: 24 dp at 1x, 1.5x, 2x, 3x and 4x.</summary>
    public static readonly (string Folder, int Px)[] StatusSizes = [("drawable-mdpi", 24), ("drawable-hdpi", 36), ("drawable-xhdpi", 48), ("drawable-xxhdpi", 72), ("drawable-xxxhdpi", 96)];

    /// <summary>The variants that are pictures.</summary>
    public static IEnumerable<LogoVariant> ImageVariants => Enum.GetValues<LogoVariant>().Where(PardehLogoArt.IsImage);

    public static LogoImage Master(LogoVariant variant) => LogoImage.Get(variant) ?? throw new InvalidOperationException($"{variant} is not a picture or its master cannot be decoded");

    private static float Smooth(double v, double a, double b)
    {
        var t = Math.Clamp((v - a) / (b - a), 0, 1);
        return (float)(t * t * (3 - 2 * t));
    }

    /// <summary>Where the mark is in the master: bounding box of everything at least half opaque (master pixels), and the largest distance from its centre.</summary>
    /// <param name="mask">Measure this master-sized one-tone mask instead of the picture (for a picture whose mark sits on its own tile).</param>
    public static (double Cx, double Cy, double Width, double Height, double Radius) Content(LogoVariant variant, float[]? mask = null)
    {
        var m = Master(variant);
        bool Solid(int x, int y) => mask is null ? m.Pixels[(y * m.Size + x) * 4 + 3] >= 128 : mask[(y * m.Size + x) * 4 + 3] >= 0.5f;
        int minX = m.Size, minY = m.Size, maxX = -1, maxY = -1;
        for (var y = 0; y < m.Size; y++)
        {
            for (var x = 0; x < m.Size; x++)
            {
                if (!Solid(x, y))
                    continue;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        double cx = (minX + maxX + 1) / 2.0, cy = (minY + maxY + 1) / 2.0, radius = 0;
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                if (!Solid(x, y))
                    continue;
                var dx = x + 0.5 - cx;
                var dy = y + 0.5 - cy;
                radius = Math.Max(radius, Math.Sqrt(dx * dx + dy * dy));
            }
        }

        return (cx, cy, maxX - minX + 1, maxY - minY + 1, radius);
    }

    /// <summary>A white mask of the mark for the one-tone icons, premultiplied RGBA floats at master size.</summary>
    public static float[] Mask(LogoVariant variant)
    {
        var m = Master(variant);
        var mask = new float[m.Size * m.Size * 4];
        for (var i = 0; i < m.Size * m.Size; i++)
        {
            float b = m.Pixels[i * 4] / 255f, g = m.Pixels[i * 4 + 1] / 255f, r = m.Pixels[i * 4 + 2] / 255f, a = m.Pixels[i * 4 + 3] / 255f;
            float value;
            if (variant == LogoVariant.Curtain)
            {
                // The ribbons and the play triangle are bright and the navy tile is not. The tile's lit rim is, so only the part
                // of the picture where the ribbons lie counts (they end 10% in from each side).
                var v = a > 0 ? Math.Max(r, Math.Max(g, b)) / a : 0;
                var (x, y) = (i % m.Size / (double)m.Size, i / m.Size / (double)m.Size);
                var inside = x is > 0.085 and < 0.915 && y is > 0.085 and < 0.915;
                // In the middle only the white triangle counts: its blue glow would round it into a blob.
                var middle = Math.Pow(x - 0.512, 2) + Math.Pow(y - 0.5, 2) < 0.176 * 0.176; // the circle between the ribbons that holds it
                var whiteness = a > 0 ? Math.Min(r, Math.Min(g, b)) / a : 0;
                value = !inside ? 0 : middle ? a * Smooth(whiteness, 0.62, 0.82) : a * Smooth(v, 0.46, 0.54);
                if (value < 0.2f)
                    value = 0; // faint specks outside the mark would show in the 24 dp icon
            }
            else
            {
                value = a;
            }

            mask[i * 4] = mask[i * 4 + 1] = mask[i * 4 + 2] = mask[i * 4 + 3] = value;
        }

        return mask;
    }

    /// <summary>Resamples the master-sized <paramref name="source"/> to a <paramref name="size"/> square in which the mark (centred) has a radius of <paramref name="radiusFraction"/> of the half size.</summary>
    private static byte[] Place(float[] source, LogoVariant variant, int size, double radiusFraction, float[]? measure = null)
    {
        var m = Master(variant);
        var c = Content(variant, measure);
        var k = radiusFraction * size / 2 / c.Radius; // output pixels per master pixel
        var side = size / k;
        return LogoResampler.ToBytes(LogoResampler.Resize(source, m.Size, m.Size, c.Cx - side / 2, c.Cy - side / 2, side, side, size, size));
    }

    /// <summary>The adaptive-icon foreground (432 px = 108 dp): the mark centred, inside the safe zone.</summary>
    public static byte[] Foreground(LogoVariant variant) => variant == LogoVariant.Curtain
        ? CurtainForeground()
        : Place(LogoResampler.ToFloat(Master(variant).Pixels), variant, LayerPx, MarkRadiusDp / 54);

    /// <summary>The themed-icon layer (alpha only matters): the white mask in the same place as the foreground.</summary>
    public static byte[] Monochrome(LogoVariant variant)
    {
        var mask = Mask(variant);
        // The curtain's picture is opaque all over its tile, so the mark is measured on the mask.
        return Place(mask, variant, LayerPx, MarkRadiusDp / 54, variant == LogoVariant.Curtain ? mask : null);
    }

    /// <summary>The full-bleed artwork of the icon pack: the tile runs to the picture's edges.</summary>
    private static string CurtainFullBleed => System.IO.Path.Combine(RepoRoot(), "tools", "logo-master", "source-curtain-fullbleed-1024.png");

    /// <summary>The side of the full-bleed picture in the 108 dp layer: its ribbons then reach 32.5 dp from the centre, just inside the safe circle.</summary>
    private const int CurtainDp = 60;

    /// <summary>
    /// The curtain's launcher foreground: the full-bleed art, small enough that no icon mask cuts the ribbons, its outer
    /// edge faded away so that it runs into the background layer's navy without a seam.
    /// </summary>
    private static byte[] CurtainForeground()
    {
        var (width, height, bgra) = Decode(File.ReadAllBytes(CurtainFullBleed));
        const int Side = CurtainDp * LayerPx / 108;
        var small = LogoResampler.ResizeBytes(bgra, width, height, Side, Side);
        var layer = new byte[LayerPx * LayerPx * 4];
        const int At = (LayerPx - Side) / 2;
        const double Fade = Side * 0.062; // the ribbons start 7% in
        for (var y = 0; y < Side; y++)
        {
            for (var x = 0; x < Side; x++)
            {
                var edge = Math.Min(Math.Min(x, Side - 1 - x), Math.Min(y, Side - 1 - y));
                var keep = Smooth(edge, 0, Fade);
                for (var channel = 0; channel < 4; channel++)
                    layer[((At + y) * LayerPx + At + x) * 4 + channel] = (byte)Math.Round(small[(y * Side + x) * 4 + channel] * keep);
            }
        }

        return layer;
    }

    /// <summary>The status-bar icon: a solid white silhouette filling its box with a 1 dp margin (24 dp = <paramref name="px"/> pixels at its density).</summary>
    public static byte[] Status(LogoVariant variant, int px)
    {
        var mask = Mask(variant);
        var c = Content(variant, variant == LogoVariant.Curtain ? mask : null);
        var m = Master(variant);
        var k = px * (22.0 / 24) / Math.Max(c.Width, c.Height);
        var side = px / k;
        var icon = LogoResampler.ToBytes(LogoResampler.Resize(mask, m.Size, m.Size, c.Cx - side / 2, c.Cy - side / 2, side, side, px, px));
        for (var i = 0; i < icon.Length; i += 4)
        {
            if (icon[i + 3] <= 6)
                icon[i] = icon[i + 1] = icon[i + 2] = icon[i + 3] = 0; // the filter's faint ringing beyond the mark
        }

        return icon;
    }

    /// <summary>The picture at <paramref name="size"/> pixels (the whole master canvas).</summary>
    public static byte[] Logo(LogoVariant variant, int size) => Master(variant).Resized(size);

    /// <summary>The adaptive-icon tile colours (top, bottom): a very dark navy so the mark stands out; the curtain's are its own art's edges.</summary>
    public static (string Top, string Bottom) TileColors(LogoVariant variant) => variant switch
    {
        LogoVariant.Curtain => ("#272D66", "#120E3B"),
        _ => ("#04101F", "#020914"),
    };

    /// <summary>The Windows 11 widget package's pictures (file name, side in pixels).</summary>
    public static readonly (string Name, int Px)[] WidgetImages = [("Square150x150Logo.png", 150), ("Square44x44Logo.png", 44), ("StoreLogo.png", 50)];

    public static string TileXml(LogoVariant variant)
    {
        var (top, bottom) = TileColors(variant);
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <!-- Adaptive launcher icon, background: a very dark navy tile (design: {variant}). Generated by scripts/export-logo-image.ps1: edit LogoExport.TileColors, not this file. -->
            <vector xmlns:android="http://schemas.android.com/apk/res/android" xmlns:aapt="http://schemas.android.com/aapt"
                android:width="108dp"
                android:height="108dp"
                android:viewportWidth="108"
                android:viewportHeight="108">
              <path android:pathData="M0,0h108v108h-108z">
                <aapt:attr name="android:fillColor">
                  <gradient android:type="linear" android:startX="54" android:startY="0" android:endX="54" android:endY="108">
                    <item android:offset="0" android:color="{top}"/>
                    <item android:offset="1" android:color="{bottom}"/>
                  </gradient>
                </aapt:attr>
              </path>
            </vector>

            """;
    }

    /// <summary>The Android files of a variant: path under <c>src/Pardeh.Android/Resources</c> to content.</summary>
    public static IReadOnlyDictionary<string, byte[]> AndroidFiles(LogoVariant variant)
    {
        var files = new Dictionary<string, byte[]>
        {
            ["drawable-nodpi/pardeh_logo.png"] = Png(Logo(variant, 512), 512),
            ["drawable-nodpi/ic_launcher_foreground.png"] = Png(Foreground(variant), LayerPx),
            ["drawable-nodpi/ic_launcher_monochrome.png"] = Png(Monochrome(variant), LayerPx),
            ["drawable/ic_launcher_pardeh_background.xml"] = new UTF8Encoding(false).GetBytes(TileXml(variant).ReplaceLineEndings("\n")),
        };
        foreach (var (folder, px) in StatusSizes)
            files[$"{folder}/ic_stat_pardeh.png"] = Png(Status(variant, px), px);
        return files;
    }

    /// <summary>Resource files that earlier versions generated and that must not exist any more (a same-named .png and .xml would clash).</summary>
    public static readonly string[] ObsoleteAndroidFiles = ["drawable/pardeh_logo.xml", "drawable/ic_launcher_foreground.xml", "drawable/ic_launcher_monochrome.xml", "drawable/ic_stat_pardeh.xml"];

    public static byte[] Png(byte[] bgra, int size) => LunaArt.Png(bgra, size * 4, 0, 0, size, size);

    /// <summary>A multi-size Windows icon (PNG-compressed entries) of a picture variant.</summary>
    public static byte[] Ico(LogoVariant variant) => IcoFrom(IcoSizes.Select(size => (size, Png(Logo(variant, size), size))).ToList());

    /// <summary>Decodes a PNG file's pixels (premultiplied BGRA); the size is read from the picture.</summary>
    public static (int Width, int Height, byte[] Bgra) Decode(byte[] png)
    {
        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);
        var w = bitmap.PixelSize.Width;
        var h = bitmap.PixelSize.Height;
        var pixels = new byte[w * h * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), pixels.Length, w * 4);
        }
        finally
        {
            handle.Free();
        }

        Pardeh.UI.Rendering.BitmapBridge.ToBgra(pixels, bitmap.Format);
        return (w, h, pixels);
    }

    /// <summary>An Avalonia bitmap of premultiplied BGRA pixels (for the preview sheets).</summary>
    public static Bitmap ToBitmap(byte[] bgra, int size)
    {
        var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        for (var y = 0; y < size; y++)
            Marshal.Copy(bgra, y * size * 4, buffer.Address + y * buffer.RowBytes, size * 4);
        return bitmap;
    }
}

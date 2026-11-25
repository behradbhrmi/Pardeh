using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Pardeh.UI.Rendering;

namespace Pardeh.UI.FullMode;

/// <summary>
/// The logo as a picture: the embedded master PNGs (src/Pardeh.UI/Assets, see Assets/README.md) decoded once, and a few
/// pre-filtered smaller copies (Lanczos, so the artwork's fine detail does not alias at small sizes) from which
/// <see cref="Draw"/> picks the one just larger than what the target needs. Pixels are premultiplied BGRA, like everywhere else.
/// </summary>
internal sealed class LogoImage
{
    /// <summary>The pre-filtered widths kept besides the master.</summary>
    private static readonly int[] Sizes = [16, 24, 32, 48, 64, 96, 128, 192, 256, 384, 512];

    private static readonly ConcurrentDictionary<LogoVariant, LogoImage?> Cache = new();

    private const double ScreenDensity = 3;

    private readonly ConcurrentDictionary<int, Bitmap> _levels = new();

    /// <summary>Pixels per unit while a skin picture is rendered offscreen (set by <see cref="LunaArt.Render"/>), 0 when drawing on screen.</summary>
    [ThreadStatic]
    public static int BakeDensity;

    private LogoImage(int size, byte[] bgra)
    {
        Size = size;
        Pixels = bgra;
    }

    /// <summary>The master's side in pixels (the master is square).</summary>
    public int Size { get; }

    /// <summary>The master: premultiplied BGRA.</summary>
    public byte[] Pixels { get; }

    /// <summary>The embedded resource name of an image variant, or null for a vector design.</summary>
    public static string? ResourceName(LogoVariant variant) => variant switch
    {
        LogoVariant.Curtain => "pardeh-logo-curtain.png",
        _ => null,
    };

    /// <summary>The picture of an image variant (decoded on first use), or null when it is a vector design or cannot be decoded.</summary>
    public static LogoImage? Get(LogoVariant variant) => Cache.GetOrAdd(variant, Load);

    private static LogoImage? Load(LogoVariant variant)
    {
        if (ResourceName(variant) is not { } name)
            return null;
        try
        {
            using var stream = typeof(LogoImage).Assembly.GetManifestResourceStream(name);
            return stream is null ? null : Decode(stream);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Decodes a PNG (the master) to premultiplied BGRA.</summary>
    public static LogoImage Decode(Stream stream)
    {
        using var decoded = new Bitmap(stream);
        var w = decoded.PixelSize.Width;
        var h = decoded.PixelSize.Height;
        if (w != h)
            throw new InvalidOperationException("the logo master is square");
        var pixels = new byte[w * h * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            decoded.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), pixels.Length, w * 4);
        }
        finally
        {
            handle.Free();
        }

        BitmapBridge.ToBgra(pixels, decoded.Format);
        return new LogoImage(w, pixels);
    }

    private readonly ConcurrentDictionary<(int Hue, int Saturation, int Brightness), LogoImage> _tinted = new();

    /// <summary>
    /// The logo in the skin colour: the cool (teal / blue) parts of the picture take the hue <paramref name="hue"/> (and the saturation and
    /// brightness scales), any warm parts keep their colour. The shift is measured from the picture's own dominant cool
    /// hue, so it works for every image variant.
    /// </summary>
    public LogoImage WithHue(double hue, double saturation, double brightness) =>
        _tinted.GetOrAdd(((int)Math.Round(((hue % 360) + 360) % 360), (int)Math.Round(saturation * 100), (int)Math.Round(brightness * 100)), key => Tint(key.Hue, key.Saturation / 100.0, key.Brightness / 100.0));

    private static bool IsCool(double h, double s) => s > 0.12 && h is >= 120 and <= 290;

    private LogoImage Tint(double hue, double saturation, double brightness)
    {
        // The dominant cool hue: a circular mean weighted by saturation and opacity.
        double sx = 0, sy = 0;
        for (var i = 0; i < Pixels.Length; i += 4)
        {
            var (h, s, _, a) = ToHsv(Pixels, i);
            if (a < 0.5 || !IsCool(h, s))
                continue;
            sx += s * Math.Cos(h * Math.PI / 180);
            sy += s * Math.Sin(h * Math.PI / 180);
        }

        var baseHue = sx == 0 && sy == 0 ? 180 : (Math.Atan2(sy, sx) * 180 / Math.PI + 360) % 360;
        var delta = hue - baseHue;
        var result = (byte[])Pixels.Clone();
        for (var i = 0; i < result.Length; i += 4)
        {
            var (h, s, v, a) = ToHsv(Pixels, i);
            if (a <= 0 || !IsCool(h, s))
                continue;
            var (r, g, b) = FromHsv((h + delta + 360) % 360, Math.Clamp(s * saturation, 0, 1), Math.Clamp(v * brightness, 0, 1));
            result[i + 2] = (byte)Math.Round(r * a);
            result[i + 1] = (byte)Math.Round(g * a);
            result[i] = (byte)Math.Round(b * a);
        }

        return new LogoImage(Size, result);
    }

    /// <summary>Hue (degrees), saturation, value and alpha (all 0..1) of a premultiplied BGRA pixel.</summary>
    private static (double H, double S, double V, double A) ToHsv(byte[] bgra, int i)
    {
        var a = bgra[i + 3] / 255.0;
        if (a <= 0)
            return (0, 0, 0, 0);
        double b = bgra[i] / 255.0 / a, g = bgra[i + 1] / 255.0 / a, r = bgra[i + 2] / 255.0 / a;
        r = Math.Min(r, 1);
        g = Math.Min(g, 1);
        b = Math.Min(b, 1);
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        if (d < 1e-6)
            return (0, 0, max, a);
        var h = max == r ? 60 * (((g - b) / d + 6) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return (h, d / max, max, a);
    }

    private static (double R, double G, double B) FromHsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return (r + m, g + m, b + m);
    }

    /// <summary>The picture at <paramref name="size"/> x <paramref name="size"/> pixels, Lanczos-filtered (premultiplied BGRA).</summary>
    public byte[] Resized(int size) => size == Size ? Pixels : LogoResampler.ResizeBytes(Pixels, Size, Size, size, size);

    /// <summary>The smallest pre-filtered copy that is at least <paramref name="pixels"/> wide (the master when none is).</summary>
    public Bitmap Level(double pixels)
    {
        var size = Size;
        foreach (var s in Sizes)
        {
            if (s >= pixels - 0.5 && s < Size)
            {
                size = s;
                break;
            }
        }

        return _levels.GetOrAdd(size, s =>
        {
            var bgra = Resized(s);
            var bitmap = new WriteableBitmap(new PixelSize(s, s), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using var buffer = bitmap.Lock();
            for (var y = 0; y < s; y++)
                Marshal.Copy(bgra, y * s * 4, buffer.Address + y * buffer.RowBytes, s * 4);
            return bitmap;
        });
    }

    /// <summary>Draws the picture so its square fits (centred) in <paramref name="r"/>, from the copy that suits the pixels it will cover.</summary>
    public void Draw(DrawingContext g, Rect r)
    {
        var side = Math.Min(r.Width, r.Height);
        if (side <= 0)
            return;
        // Pixels per unit: the density of an art bake (LunaArt.Render sets it), else a typical screen (phones are 2.5 to 3.5x; the copy is
        // then drawn down a little, which is smooth, where drawing a too small copy up would be soft).
        var level = Level(side * (BakeDensity > 0 ? BakeDensity : ScreenDensity));
        var dest = new Rect(r.X + (r.Width - side) / 2, r.Y + (r.Height - side) / 2, side, side);
        using (g.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            g.DrawImage(level, new Rect(0, 0, level.PixelSize.Width, level.PixelSize.Height), dest);
    }
}

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Pardeh.Skinning.Imaging;

namespace Pardeh.UI.FullMode;

/// <summary>
/// Pardeh's own drawing of the 2003 "Luna" full-mode chrome: original vector art, colours measured from
/// reference captures. Everything is rendered to bitmaps so the skin engine treats it like any classic skin,
/// and an asset pack with the original bitmaps can replace any file by name.
/// </summary>
internal static class LunaArt
{
    // Measured palette.
    public static readonly Color Desktop = Color.Parse("#004E98");
    public static readonly Color BandLight = Color.Parse("#B7C8F0");
    public static readonly Color BandMid = Color.Parse("#8CA6E3");
    public static readonly Color BandDark = Color.Parse("#748ACA");
    public static readonly Color TabLight = Color.Parse("#E4EAF8");
    public static readonly Color TabFace = Color.Parse("#C7D2EC");
    public static readonly Color Frame = Color.Parse("#8DA3DE");
    public static readonly Color FrameDark = Color.Parse("#5666AB");
    public static readonly Color RailLight = Color.Parse("#FBFCFE");
    public static readonly Color RailMid = Color.Parse("#E3EBF8");
    public static readonly Color RailDark = Color.Parse("#BFD2EA");
    public static readonly Color Navy = Color.Parse("#212741");
    public static readonly Color Slate = Color.Parse("#394472");
    public static readonly Color SlateHeader = Color.Parse("#6071BF");
    public static readonly Color Toolbar = Color.Parse("#282F4A");
    public static readonly Color SeekBand = Color.Parse("#D3DBED");
    public static readonly Color Track = Color.Parse("#AEB5CE");
    public static readonly Color TrackFill = Color.Parse("#94C692");
    public static readonly Color Thumb = Color.Parse("#1AB919");
    public static readonly Color TransportTop = Color.Parse("#F4F7FD");
    public static readonly Color TransportBottom = Color.Parse("#C3CDEA");
    public static readonly Color Ink = Color.Parse("#1F2F6E");

    /// <summary>Pixels per unit for art that does not ask for one (the desktop skins drawn in code): 1, or higher while the skin is shown enlarged (View > Skin Size) so it stays sharp.</summary>
    public static int DefaultScale { get; set; } = 1;

    /// <summary>
    /// Renders <paramref name="draw"/> into a w×h bitmap and returns its premultiplied BGRA pixels. With <paramref name="scale"/>
    /// &gt; 1 the bitmap is w·scale × h·scale pixels and the drawing context is scaled to match, so <paramref name="draw"/>
    /// still works in the w×h logical units (the result is the same picture, drawn sharper).
    /// </summary>
    public static byte[] Render(int w, int h, Action<DrawingContext> draw, int scale = 0)
    {
        scale = scale <= 0 ? Math.Max(1, DefaultScale) : Math.Max(1, scale);
        w *= scale;
        h *= scale;
        using var target = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        var previousDensity = LogoImage.BakeDensity;
        LogoImage.BakeDensity = scale; // the logo picks the pre-filtered copy that fits this many pixels per unit
        try
        {
            using var context = target.CreateDrawingContext();
            if (scale == 1)
            {
                draw(context);
            }
            else
            {
                using (context.PushTransform(Matrix.CreateScale(scale, scale)))
                    draw(context);
            }
        }
        finally
        {
            LogoImage.BakeDensity = previousDensity;
        }

        var pixels = new byte[w * h * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            target.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), pixels.Length, w * 4);
        }
        finally
        {
            handle.Free();
        }

        Pardeh.UI.Rendering.BitmapBridge.ToBgra(pixels, target.Format);
        return pixels;
    }

    /// <summary>
    /// Encodes a region (in image pixels) of premultiplied BGRA pixels as PNG; a <paramref name="scale"/> &gt; 1 marks it as
    /// high-resolution art (<see cref="PngDensity"/>), which the skin engine lays out in w/scale × h/scale skin units.
    /// </summary>
    public static byte[] Png(byte[] pixels, int stride, int x, int y, int w, int h, int scale = 1)
    {
        return PngDensity.Mark(EncodePng(pixels, stride, x, y, w, h), scale);
    }

    private static byte[] EncodePng(byte[] pixels, int stride, int x, int y, int w, int h)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            for (var row = 0; row < h; row++)
                Marshal.Copy(pixels, (y + row) * stride + x * 4, buffer.Address + row * buffer.RowBytes, w * 4);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    /// <summary>A w×h (skin units) picture as PNG; <paramref name="scale"/> pixels per unit when &gt; 1 (see <see cref="Render"/>).</summary>
    public static byte[] Png(int w, int h, Action<DrawingContext> draw, int scale = 0)
    {
        scale = scale <= 0 ? Math.Max(1, DefaultScale) : Math.Max(1, scale);
        return Png(Render(w, h, draw, scale), w * scale * 4, 0, 0, w * scale, h * scale, scale);
    }

    public static IBrush Vertical(params (Color Color, double Offset)[] stops) => Gradient(new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative), stops);

    public static IBrush Horizontal(params (Color Color, double Offset)[] stops) => Gradient(new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 0, RelativeUnit.Relative), stops);

    private static IBrush Gradient(RelativePoint start, RelativePoint end, (Color Color, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        foreach (var (color, offset) in stops)
            brush.GradientStops.Add(new GradientStop(color, offset));
        return brush;
    }

    public static IBrush Radial(Color center, Color edge) => new RadialGradientBrush
    {
        Center = new RelativePoint(0.4, 0.3, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.4, 0.3, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
        GradientStops = { new GradientStop(center, 0), new GradientStop(edge, 1) },
    };

    public static IPen Pen(Color color, double thickness = 1) => new Pen(new SolidColorBrush(color), thickness);

    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    public static Geometry Geo(string data) => StreamGeometry.Parse(data);
}

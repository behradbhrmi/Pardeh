namespace Pardeh.Skinning.Imaging;

/// <summary>
/// A decoded skin image: 32-bit BGRA, premultiplied alpha, top-down rows.
/// <see cref="Width"/> / <see cref="Height"/> / <see cref="Pixels"/> are always in image pixels. A high-resolution image
/// (<see cref="Scale"/> &gt; 1, see <see cref="PngDensity"/>) holds <see cref="Scale"/> pixels per skin unit: the skin's
/// layout, hit testing, tiling and borders stay in <see cref="LogicalWidth"/> × <see cref="LogicalHeight"/> units, and
/// the renderer draws it at that size, so the art is simply sharper. Scale is 1 for every ordinary skin image.
/// </summary>
public sealed class SkinBitmap
{
    public SkinBitmap(int width, int height, byte[] pixels, int scale = 1)
    {
        if (pixels.Length != width * height * 4)
            throw new ArgumentException("Pixel buffer size does not match dimensions.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
        Scale = Math.Max(1, scale);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Image pixels per skin unit (1 for ordinary skin art).</summary>
    public int Scale { get; }

    /// <summary>Width in skin units (<see cref="Width"/> / <see cref="Scale"/>).</summary>
    public double LogicalWidth => Scale == 1 ? Width : (double)Width / Scale;

    /// <summary>Height in skin units (<see cref="Height"/> / <see cref="Scale"/>).</summary>
    public double LogicalHeight => Scale == 1 ? Height : (double)Height / Scale;

    /// <summary>The same pixels reinterpreted as <paramref name="scale"/> pixels per skin unit (no copy).</summary>
    public SkinBitmap WithScale(int scale) => scale == Scale ? this : new SkinBitmap(Width, Height, Pixels, scale);

    /// <summary>The 0xRRGGBB colour at a position given in skin units (-1 outside the image).</summary>
    public int GetRgbAt(double x, double y) => Scale == 1 ? GetRgb((int)x, (int)y) : GetRgb((int)(x * Scale), (int)(y * Scale));

    /// <summary>The alpha at a position given in skin units (0 outside the image).</summary>
    public byte GetAlphaAt(double x, double y) => Scale == 1 ? GetAlpha((int)x, (int)y) : GetAlpha((int)(x * Scale), (int)(y * Scale));

    /// <summary>BGRA bytes, premultiplied, stride = Width * 4.</summary>
    public byte[] Pixels { get; }

    public int Stride => Width * 4;

    /// <summary>Returns the pixel colour as 0xRRGGBB (ignores alpha).</summary>
    public int GetRgb(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return -1;
        var i = (y * Width + x) * 4;
        return (Pixels[i + 2] << 16) | (Pixels[i + 1] << 8) | Pixels[i];
    }

    public byte GetAlpha(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return 0;
        return Pixels[(y * Width + x) * 4 + 3];
    }

    /// <summary>Copy where every pixel of colour <paramref name="rgb"/> becomes fully transparent.</summary>
    public SkinBitmap WithColorKey(int rgb)
    {
        var copy = (byte[])Pixels.Clone();
        byte r = (byte)(rgb >> 16), g = (byte)(rgb >> 8), b = (byte)rgb;
        for (var i = 0; i < copy.Length; i += 4)
        {
            if (copy[i] == b && copy[i + 1] == g && copy[i + 2] == r)
            {
                copy[i] = copy[i + 1] = copy[i + 2] = copy[i + 3] = 0;
            }
        }

        return new SkinBitmap(Width, Height, copy, Scale);
    }

    /// <summary>A copy scaled to <paramref name="width"/> × <paramref name="height"/> (area-averaged when shrinking).</summary>
    public SkinBitmap Resized(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height)
            return this;

        var result = new byte[width * height * 4];
        double sx = (double)Width / width, sy = (double)Height / height;
        for (var y = 0; y < height; y++)
        {
            int y0 = (int)(y * sy), y1 = Math.Max(y0 + 1, Math.Min(Height, (int)Math.Ceiling((y + 1) * sy)));
            for (var x = 0; x < width; x++)
            {
                int x0 = (int)(x * sx), x1 = Math.Max(x0 + 1, Math.Min(Width, (int)Math.Ceiling((x + 1) * sx)));
                int b = 0, g = 0, r = 0, a = 0, n = 0;
                for (var yy = y0; yy < y1; yy++)
                {
                    var i = (yy * Width + x0) * 4;
                    for (var xx = x0; xx < x1; xx++, i += 4)
                    {
                        b += Pixels[i];
                        g += Pixels[i + 1];
                        r += Pixels[i + 2];
                        a += Pixels[i + 3];
                        n++;
                    }
                }

                var o = (y * width + x) * 4;
                result[o] = (byte)(b / n);
                result[o + 1] = (byte)(g / n);
                result[o + 2] = (byte)(r / n);
                result[o + 3] = (byte)(a / n);
            }
        }

        return new SkinBitmap(width, height, result, Scale);
    }

    /// <summary>A copy scaled to cover <paramref name="logicalWidth"/> × <paramref name="logicalHeight"/> skin units (keeps this image's density).</summary>
    public SkinBitmap ResizedToUnits(int logicalWidth, int logicalHeight) => Resized(Math.Max(1, logicalWidth) * Scale, Math.Max(1, logicalHeight) * Scale);

    /// <summary>
    /// A copy with the hue rotated by <paramref name="hueShift"/> degrees and saturation multiplied by
    /// <paramref name="saturation"/> (the classic player's colour chooser: backHueShift, backSaturation …).
    /// </summary>
    public SkinBitmap WithHueSaturation(double hueShift, double saturation)
    {
        hueShift %= 360;
        if (hueShift < 0)
            hueShift += 360;
        if (hueShift == 0 && Math.Abs(saturation - 1) < 1e-6)
            return this;

        var copy = (byte[])Pixels.Clone();
        for (var i = 0; i < copy.Length; i += 4)
        {
            var alpha = copy[i + 3];
            if (alpha == 0)
                continue;

            // Un-premultiply, shift in HSL, premultiply again.
            double scale = alpha / 255.0;
            double b = copy[i] / 255.0 / scale, g = copy[i + 1] / 255.0 / scale, r = copy[i + 2] / 255.0 / scale;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2, h = 0, s = 0;
            var d = max - min;
            if (d > 1e-9)
            {
                s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
                h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
                h *= 60;
            }

            h = (h + hueShift) % 360;
            s = Math.Clamp(s * saturation, 0, 1);
            (r, g, b) = HslToRgb(h / 360, s, l);
            copy[i] = (byte)Math.Round(Math.Clamp(b, 0, 1) * scale * 255);
            copy[i + 1] = (byte)Math.Round(Math.Clamp(g, 0, 1) * scale * 255);
            copy[i + 2] = (byte)Math.Round(Math.Clamp(r, 0, 1) * scale * 255);
        }

        return new SkinBitmap(Width, Height, copy, Scale);
    }

    /// <summary>One 0xRRGGBB colour through the same hue / saturation shift as <see cref="WithHueSaturation"/>.</summary>
    public static int ShiftColor(int rgb, double hueShift, double saturation)
    {
        var pixel = new SkinBitmap(1, 1, [(byte)rgb, (byte)(rgb >> 8), (byte)(rgb >> 16), 255]).WithHueSaturation(hueShift, saturation);
        return pixel.GetRgb(0, 0);
    }

    private static (double R, double G, double B) HslToRgb(double h, double s, double l)
    {
        if (s <= 0)
            return (l, l, l);
        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        return (Channel(p, q, h + 1.0 / 3), Channel(p, q, h), Channel(p, q, h - 1.0 / 3));

        static double Channel(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
    }

    /// <summary>
    /// Copy that keeps only the pixels whose position in <paramref name="map"/> has colour <paramref name="mappingRgb"/>.
    /// Used for button-group states.
    /// </summary>
    public SkinBitmap MaskedBy(SkinBitmap map, int mappingRgb)
    {
        var result = new byte[Pixels.Length];
        var w = Math.Min(Width, map.Width);
        var h = Math.Min(Height, map.Height);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (map.GetRgb(x, y) != mappingRgb)
                    continue;
                var i = (y * Width + x) * 4;
                result[i] = Pixels[i];
                result[i + 1] = Pixels[i + 1];
                result[i + 2] = Pixels[i + 2];
                result[i + 3] = Pixels[i + 3];
            }
        }

        return new SkinBitmap(Width, Height, result, Scale);
    }
}

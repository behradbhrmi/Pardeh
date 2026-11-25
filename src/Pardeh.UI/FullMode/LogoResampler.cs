namespace Pardeh.UI.FullMode;

/// <summary>
/// A small, dependency-free image resampler for the logo picture: separable Lanczos (a = 3) on premultiplied 4-channel float
/// pixels. When shrinking, the kernel is stretched by the shrink factor, so it is a proper low-pass filter: the fine scan-line
/// texture of the artwork averages out at small sizes instead of aliasing (moire). Used by <see cref="LogoImage"/> (the
/// pre-filtered sizes the app draws), by the export (Android layers, .ico, PNGs) and by tools/logo-master (builds the master).
/// </summary>
internal static class LogoResampler
{
    private const int Support = 3;

    private static double Sinc(double x)
    {
        if (Math.Abs(x) < 1e-9)
            return 1;
        var px = Math.PI * x;
        return Math.Sin(px) / px;
    }

    private static double Lanczos(double x) => Math.Abs(x) >= Support ? 0 : Sinc(x) * Sinc(x / Support);

    /// <summary>The taps of one output sample: first source index, normalised weights.</summary>
    private static (int First, float[] Weights)[] Taps(int srcSize, int dstSize, double srcStart, double srcLength)
    {
        var scale = srcLength / dstSize;
        var stretch = Math.Max(1, scale);
        var taps = new (int, float[])[dstSize];
        for (var i = 0; i < dstSize; i++)
        {
            var centre = srcStart + (i + 0.5) * scale;
            var first = (int)Math.Floor(centre - Support * stretch + 0.5);
            var last = (int)Math.Floor(centre + Support * stretch + 0.5);
            var weights = new float[last - first + 1];
            double sum = 0;
            for (var j = first; j <= last; j++)
            {
                var w = Lanczos((j + 0.5 - centre) / stretch);
                // Beyond the edge the nearest source pixel is repeated (clamped), so a border does not darken or fade.
                weights[j - first] = (float)w;
                sum += w;
            }

            for (var k = 0; k < weights.Length; k++)
                weights[k] = (float)(weights[k] / sum);
            // Fold the out-of-range taps onto the edge pixels.
            if (first < 0 || last >= srcSize)
            {
                var from = Math.Clamp(first, 0, srcSize - 1);
                var to = Math.Clamp(last, 0, srcSize - 1); // a window wholly outside the image folds onto one edge pixel
                var folded = new float[to - from + 1];
                for (var j = first; j <= last; j++)
                    folded[Math.Clamp(j, 0, srcSize - 1) - from] += weights[j - first];
                taps[i] = (from, folded);
            }
            else
            {
                taps[i] = (first, weights);
            }
        }

        return taps;
    }

    /// <summary>
    /// Resamples the source rectangle (<paramref name="sx"/>, <paramref name="sy"/>, <paramref name="sw"/> x <paramref name="sh"/>, in source
    /// pixels, may be fractional) of a premultiplied 4-channel float image to <paramref name="dw"/> x <paramref name="dh"/>. The result is not clamped.
    /// </summary>
    public static float[] Resize(float[] src, int srcW, int srcH, double sx, double sy, double sw, double sh, int dw, int dh)
    {
        var xTaps = Taps(srcW, dw, sx, sw);
        var yTaps = Taps(srcH, dh, sy, sh);

        // Horizontal pass over the rows the vertical pass needs.
        var rowFirst = yTaps.Min(t => t.First);
        var rowLast = yTaps.Max(t => t.First + t.Weights.Length - 1);
        var mid = new float[(rowLast - rowFirst + 1) * dw * 4];
        Parallel.For(rowFirst, rowLast + 1, y =>
        {
            var rowIn = y * srcW * 4;
            var rowOut = (y - rowFirst) * dw * 4;
            for (var x = 0; x < dw; x++)
            {
                var (first, weights) = xTaps[x];
                float r = 0, g = 0, b = 0, a = 0;
                for (var k = 0; k < weights.Length; k++)
                {
                    var i = rowIn + (first + k) * 4;
                    var w = weights[k];
                    r += src[i] * w;
                    g += src[i + 1] * w;
                    b += src[i + 2] * w;
                    a += src[i + 3] * w;
                }

                var o = rowOut + x * 4;
                mid[o] = r;
                mid[o + 1] = g;
                mid[o + 2] = b;
                mid[o + 3] = a;
            }
        });

        var dst = new float[dw * dh * 4];
        Parallel.For(0, dh, y =>
        {
            var (first, weights) = yTaps[y];
            for (var x = 0; x < dw; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (var k = 0; k < weights.Length; k++)
                {
                    var i = ((first + k - rowFirst) * dw + x) * 4;
                    var w = weights[k];
                    r += mid[i] * w;
                    g += mid[i + 1] * w;
                    b += mid[i + 2] * w;
                    a += mid[i + 3] * w;
                }

                var o = (y * dw + x) * 4;
                dst[o] = r;
                dst[o + 1] = g;
                dst[o + 2] = b;
                dst[o + 3] = a;
            }
        });
        return dst;
    }

    /// <summary>Whole-image resize.</summary>
    public static float[] Resize(float[] src, int srcW, int srcH, int dw, int dh) => Resize(src, srcW, srcH, 0, 0, srcW, srcH, dw, dh);

    /// <summary>Clamps to 0..1 and keeps every colour channel at or below alpha (a valid premultiplied pixel); the ringing of the filter cannot leave a halo.</summary>
    public static void Clamp(float[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var a = Math.Clamp(pixels[i + 3], 0f, 1f);
            pixels[i + 3] = a;
            for (var c = 0; c < 3; c++)
                pixels[i + c] = Math.Clamp(pixels[i + c], 0f, a);
        }
    }

    /// <summary>8-bit premultiplied (any channel order) to float 0..1.</summary>
    public static float[] ToFloat(byte[] pixels)
    {
        var f = new float[pixels.Length];
        for (var i = 0; i < f.Length; i++)
            f[i] = pixels[i] / 255f;
        return f;
    }

    /// <summary>Float 0..1 (clamped first) to 8-bit.</summary>
    public static byte[] ToBytes(float[] pixels)
    {
        Clamp(pixels);
        var b = new byte[pixels.Length];
        for (var i = 0; i < b.Length; i++)
            b[i] = (byte)Math.Clamp((int)MathF.Round(pixels[i] * 255f), 0, 255);
        return b;
    }

    /// <summary>A resized 8-bit premultiplied image (e.g. BGRA pixels, the channel order is kept).</summary>
    public static byte[] ResizeBytes(byte[] src, int srcW, int srcH, int dw, int dh) => ToBytes(Resize(ToFloat(src), srcW, srcH, dw, dh));
}

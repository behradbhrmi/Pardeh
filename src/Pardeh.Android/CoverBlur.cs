using Android.Graphics;

namespace Pardeh.Android;

/// <summary>
/// A real blur for cover pictures (widget and notification backgrounds): the cover is cropped to the target shape, reduced with
/// filtering to a working size, blurred with three box passes (a close match to a Gaussian, no blocky "stretched thumbnail" look),
/// then brought to the target size with smooth filtering. Colours get a little extra saturation so the blur does not turn grey.
/// </summary>
internal static class CoverBlur
{
    /// <param name="cover">The cover picture.</param>
    /// <param name="width">Target width in pixels.</param>
    /// <param name="height">Target height in pixels.</param>
    /// <param name="radius">Blur radius in working-size pixels (about 6 is soft, 12 is very soft).</param>
    public static Bitmap Render(Bitmap cover, int width, int height, int radius = 9)
    {
        width = Math.Max(8, width);
        height = Math.Max(8, height);

        // Position pushes repaint the widget / card every few seconds with the same cover: reuse the result.
        lock (Gate)
        {
            if (_cached is { IsRecycled: false } hit && ReferenceEquals(_cachedFor, cover) && _cachedKey == (width, height, radius))
                return hit;
        }

        var result = Compute(cover, width, height, radius);
        lock (Gate)
        {
            _cached = result;
            _cachedFor = cover;
            _cachedKey = (width, height, radius);
        }

        return result;
    }

    private static readonly object Gate = new();
    private static Bitmap? _cached;
    private static Bitmap? _cachedFor;
    private static (int, int, int) _cachedKey;

    private static Bitmap Compute(Bitmap cover, int width, int height, int radius)
    {
        // A hardware bitmap (what some decoders return) cannot be read pixel by pixel: work on a normal copy of it.
        if (cover.GetConfig() == Bitmap.Config.Hardware && cover.Copy(Bitmap.Config.Argb8888!, false) is { } soft)
        {
            using (soft)
                return Compute(soft, width, height, radius);
        }

        // Working size: small enough to blur fast, large enough not to band.
        var work = 96;
        var ww = width >= height ? work : Math.Max(16, work * width / height);
        var wh = width >= height ? Math.Max(16, work * height / width) : work;

        // Crop the cover to the target aspect ratio (centre), then reduce in two halving-friendly steps for clean filtering.
        var targetRatio = (double)width / height;
        var srcRatio = (double)cover.Width / cover.Height;
        int cw = cover.Width, ch = cover.Height, cx = 0, cy = 0;
        if (srcRatio > targetRatio)
        {
            cw = (int)(cover.Height * targetRatio);
            cx = (cover.Width - cw) / 2;
        }
        else
        {
            ch = (int)(cover.Width / targetRatio);
            cy = (cover.Height - ch) / 2;
        }

        using var cropped = Bitmap.CreateBitmap(cover, cx, cy, Math.Max(1, cw), Math.Max(1, ch))!;
        var current = cropped;
        var ownsCurrent = false;
        while (current.Width > ww * 2 && current.Height > wh * 2)
        {
            var half = Bitmap.CreateScaledBitmap(current, current.Width / 2, current.Height / 2, true)!;
            if (ownsCurrent)
                current.Dispose();
            current = half;
            ownsCurrent = true;
        }

        using var small = Bitmap.CreateScaledBitmap(current, ww, wh, true)!;
        if (ownsCurrent)
            current.Dispose();

        var pixels = new int[ww * wh];
        small.GetPixels(pixels, 0, ww, 0, 0, ww, wh);
        var r = new int[pixels.Length];
        var g = new int[pixels.Length];
        var b = new int[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            r[i] = (p >> 16) & 0xFF;
            g[i] = (p >> 8) & 0xFF;
            b[i] = p & 0xFF;
        }

        foreach (var channel in new[] { r, g, b })
            for (var pass = 0; pass < 3; pass++)
            {
                BoxBlur(channel, ww, wh, radius, horizontal: true);
                BoxBlur(channel, ww, wh, radius, horizontal: false);
            }

        for (var i = 0; i < pixels.Length; i++)
        {
            // A touch more saturation and a gentle lift so the blur keeps the cover's character.
            float rr = r[i], gg = g[i], bb = b[i];
            var luma = 0.299f * rr + 0.587f * gg + 0.114f * bb;
            rr = Math.Clamp(luma + (rr - luma) * 1.25f, 0, 255);
            gg = Math.Clamp(luma + (gg - luma) * 1.25f, 0, 255);
            bb = Math.Clamp(luma + (bb - luma) * 1.25f, 0, 255);
            pixels[i] = unchecked((int)0xFF000000) | ((int)rr << 16) | ((int)gg << 8) | (int)bb;
        }

        using var blurred = Bitmap.CreateBitmap(ww, wh, Bitmap.Config.Argb8888!)!;
        blurred.SetPixels(pixels, 0, ww, 0, 0, ww, wh);
        return Bitmap.CreateScaledBitmap(blurred, width, height, true)!;
    }

    private static void BoxBlur(int[] data, int w, int h, int radius, bool horizontal)
    {
        var length = horizontal ? w : h;
        var lines = horizontal ? h : w;
        var stride = horizontal ? 1 : w;
        var lineStep = horizontal ? w : 1;
        var window = radius * 2 + 1;
        var buffer = new int[length];
        for (var line = 0; line < lines; line++)
        {
            var start = line * lineStep;
            var sum = 0;
            // Edge pixels repeat, so the picture keeps its brightness at the border.
            for (var k = -radius; k <= radius; k++)
                sum += data[start + Math.Clamp(k, 0, length - 1) * stride];
            for (var i = 0; i < length; i++)
            {
                buffer[i] = sum / window;
                sum += data[start + Math.Min(i + radius + 1, length - 1) * stride];
                sum -= data[start + Math.Max(i - radius, 0) * stride];
            }

            for (var i = 0; i < length; i++)
                data[start + i * stride] = buffer[i];
        }
    }
}

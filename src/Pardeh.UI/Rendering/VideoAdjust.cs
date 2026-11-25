using Pardeh.Core.Playback;

namespace Pardeh.UI.Rendering;

/// <summary>
/// Video Settings applied while copying a frame: brightness, contrast, saturation and hue as one colour matrix
/// (Pardeh's own; -127 – 127 like the classic sliders). Rows run in parallel.
/// </summary>
internal static class VideoAdjust
{
    public static unsafe void Apply(byte[] source, int width, int height, IntPtr destination, int destinationStride, VideoSettings settings)
    {
        // Saturation and hue in luma/chroma space, then contrast about mid grey, then brightness.
        var brightness = settings.Brightness / 127.0 * 0.35 * 255;
        var contrast = 1 + settings.Contrast / 127.0;
        var saturation = 1 + settings.Saturation / 127.0;
        var angle = settings.Hue / 127.0 * Math.PI;
        double cos = Math.Cos(angle) * saturation, sin = Math.Sin(angle) * saturation;
        const double lr = 0.299, lg = 0.587, lb = 0.114;

        // The standard hue-rotation matrix scaled by saturation (rows: R, G, B; columns: R, G, B).
        double[,] m =
        {
            { lr + cos * (1 - lr) + sin * -lr, lg + cos * -lg + sin * -lg, lb + cos * -lb + sin * (1 - lb) },
            { lr + cos * -lr + sin * 0.143, lg + cos * (1 - lg) + sin * 0.140, lb + cos * -lb + sin * -0.283 },
            { lr + cos * -lr + sin * -(1 - lr), lg + cos * -lg + sin * lg, lb + cos * (1 - lb) + sin * lb },
        };

        // Fixed point (x 1024) with contrast folded in; offset includes brightness and the contrast pivot.
        var k = new int[9];
        for (var r = 0; r < 3; r++)
            for (var c = 0; c < 3; c++)
                k[r * 3 + c] = (int)Math.Round(m[r, c] * contrast * 1024);
        var offset = (int)Math.Round((128 * (1 - contrast) + brightness) * 1024);

        var destinationBase = (byte*)destination;
        Parallel.For(0, height, y =>
        {
            var row = destinationBase + (long)y * destinationStride;
            var i = y * width * 4;
            for (var x = 0; x < width; x++, i += 4)
            {
                int b = source[i], g = source[i + 1], rr = source[i + 2];
                var nr = (k[0] * rr + k[1] * g + k[2] * b + offset) >> 10;
                var ng = (k[3] * rr + k[4] * g + k[5] * b + offset) >> 10;
                var nb = (k[6] * rr + k[7] * g + k[8] * b + offset) >> 10;
                row[x * 4] = (byte)Math.Clamp(nb, 0, 255);
                row[x * 4 + 1] = (byte)Math.Clamp(ng, 0, 255);
                row[x * 4 + 2] = (byte)Math.Clamp(nr, 0, 255);
                row[x * 4 + 3] = 255;
            }
        });
    }
}

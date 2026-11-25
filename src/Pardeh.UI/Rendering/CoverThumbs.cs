using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Pardeh.Core.Playback;

namespace Pardeh.UI.Rendering;

/// <summary>
/// Small square covers for lists drawn on the skin canvas (the Groove queue's photo tiles): the track's own picture (embedded or a
/// folder image), read once on a background thread, else a generated "photo" tile (a sky gradient, a sun and two hills) whose colours
/// follow the album's name. Used on the UI thread only; the canvas redraws every frame, so a picture appears as soon as it is read.
/// </summary>
internal static class CoverThumbs
{
    private const int Limit = 300;
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Gate = new(2);

    /// <summary>The track's cover, or null while it is read or when it has none.</summary>
    public static Bitmap? Get(MediaPlayer player, MediaItem item)
    {
        if (Cache.TryGetValue(item.Source, out var cached))
            return cached;
        if (Cache.Count >= Limit)
            Cache.Clear();
        var source = item.Source;
        Cache[source] = null;
        _ = Task.Run(async () =>
        {
            await Gate.WaitAsync();
            Bitmap? bitmap = null;
            try
            {
                if (player.GetAlbumArt(item) is { Length: > 0 } data)
                {
                    using var stream = new MemoryStream(data);
                    bitmap = Bitmap.DecodeToWidth(stream, 128);
                }
            }
            catch (Exception)
            {
                // An unreadable picture: the generated tile stays.
            }
            finally
            {
                Gate.Release();
            }

            if (bitmap is not null)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => Cache[source] = bitmap);
        });
        return null;
    }

    /// <summary>Draws the cover (cropped to fill) or the generated tile into <paramref name="rect"/> with rounded corners.</summary>
    public static void Draw(DrawingContext context, Rect rect, double radius, Bitmap? picture, string seed)
    {
        using var clip = context.PushClip(new RoundedRect(rect, radius));
        if (picture is not null)
        {
            var size = picture.Size;
            var side = Math.Min(size.Width, size.Height);
            context.DrawImage(picture, new Rect((size.Width - side) / 2, (size.Height - side) / 2, side, side), rect);
            return;
        }

        DrawGenerated(context, rect, seed);
    }

    // Sky top, sky bottom, sun, far hill, near hill.
    private static readonly string[][] Scenes =
    [
        ["#1B3C8F", "#6FB4FF", "#FFF2C4", "#2A63B8", "#123C80"],
        ["#3A1E63", "#FF8A5B", "#FFE08A", "#8A3B6B", "#3E1B45"],
        ["#0E4A6B", "#8BE0D2", "#FFFFFF", "#2C8C78", "#12513F"],
        ["#2A2370", "#C98BFF", "#FFD9F0", "#6C47B5", "#33206A"],
        ["#7A2E3A", "#FFC46B", "#FFF3D0", "#C2603E", "#5E2420"],
        ["#14315F", "#5AA7E8", "#E8F6FF", "#3F6E9E", "#1B3550"],
    ];

    /// <summary>A hash that is the same on every run (string.GetHashCode is not).</summary>
    public static int StableHash(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
            hash = (hash ^ char.ToUpperInvariant(c)) * 16777619u;
        return (int)(hash & 0x7FFFFFFF);
    }

    private static void DrawGenerated(DrawingContext context, Rect rect, string seed)
    {
        var hash = StableHash(seed);
        var scene = Scenes[hash % Scenes.Length];
        context.FillRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse(scene[0]), 0), new GradientStop(Color.Parse(scene[1]), 0.85) },
        }, rect);

        Point At(double x, double y) => new(rect.X + rect.Width * x, rect.Y + rect.Height * y);
        var sunX = 0.25 + (hash / 7 % 50) / 100.0;
        context.DrawEllipse(new SolidColorBrush(Color.Parse(scene[2])), null, At(sunX, 0.46), rect.Width * 0.14, rect.Height * 0.14);
        void Hill(string color, double left, double peakX, double peakY, double right)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(At(0, left), true);
                g.QuadraticBezierTo(At(peakX, peakY), At(1, right));
                g.LineTo(At(1, 1));
                g.LineTo(At(0, 1));
                g.EndFigure(true);
            }

            context.DrawGeometry(new SolidColorBrush(Color.Parse(color)), null, geometry);
        }

        var shift = (hash / 11 % 40) / 100.0;
        Hill(scene[3], 0.78, 0.2 + shift, 0.38, 0.72);
        Hill(scene[4], 0.74, 0.7 - shift, 0.98, 0.62);
    }
}

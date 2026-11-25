using Avalonia;
using Avalonia.Media.Imaging;
using Pardeh.Core.Playback;
using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Runtime;
using Pardeh.Skinning.Wmp;

namespace Pardeh.UI.Rendering;

/// <summary>
/// Pictures of skins for the Skin Chooser: each skin is loaded into a throwaway session (silent player,
/// no-op host) and drawn offscreen once. Results are cached per path.
/// </summary>
internal sealed class SkinPreview(IImageDecoder decoder, Func<WmpResources?> resources, Action<string> log, Func<string, Pardeh.Skinning.Model.SkinPackage?>? builtIn = null)
{
    private readonly Dictionary<string, SkinBitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The preview if ready; otherwise schedules it (on the UI thread, after this frame) and returns null.</summary>
    public SkinBitmap? Get(string path)
    {
        if (_cache.TryGetValue(path, out var preview))
            return preview;
        if (_pending.Add(path))
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _cache[path] = Render(path);
                _pending.Remove(path);
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        return null;
    }

    private SkinBitmap? Render(string path)
    {
        try
        {
            // "fullmode:<look>": one of Pardeh's built-in full modes, generated rather than loaded from a file.
            var package = builtIn?.Invoke(path) ?? Pardeh.Skinning.Model.SkinLoaders.Load(path);
            using var player = new MediaPlayer(new SilentEngine(), new NameOnlyReader());
            using var session = new SkinSession(package, player, new PreviewHost(), decoder, new MemoryPreferences()) { Resources = resources() };
            var view = session.Start();
            for (var i = 0; i < 3; i++)
                session.Tick(TimeSpan.FromMilliseconds(30));

            var size = new PixelSize(Math.Clamp((int)view.Root.Width, 1, 2000), Math.Clamp((int)view.Root.Height, 1, 2000));
            using var target = new RenderTargetBitmap(size, new Vector(96, 96));
            using (var context = target.CreateDrawingContext())
                new SkinRenderer().Render(context, view);
            using var png = new MemoryStream();
            target.Save(png, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            return decoder.Decode(png.ToArray()) is { } picture ? Trim(picture) : null;
        }
        catch (Exception ex)
        {
            log($"Skin preview failed for {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Crops away fully transparent margins (shaped skins rarely fill their view).</summary>
    private static SkinBitmap Trim(SkinBitmap bitmap)
    {
        int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetAlpha(x, y) == 0)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < 0 || (left == 0 && top == 0 && right == bitmap.Width - 1 && bottom == bitmap.Height - 1))
            return bitmap;
        int w = right - left + 1, h = bottom - top + 1;
        var pixels = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            Buffer.BlockCopy(bitmap.Pixels, ((top + y) * bitmap.Width + left) * 4, pixels, y * w * 4, w * 4);
        return new SkinBitmap(w, h, pixels);
    }

    private sealed class SilentEngine : IPlaybackEngine
    {
        public bool IsOpen => false;
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.Zero;
        public float Volume { get; set; } = 0.5f;
        public float Balance { get; set; }
        public bool Muted { get; set; }
        public EqualizerSettings Equalizer { get; } = new();
#pragma warning disable CS0067 // never raised: nothing plays
        public event EventHandler? PlaybackEnded;
#pragma warning restore CS0067
        public void Open(string source) => throw new InvalidOperationException("Previews don't play media.");
        public void Close() { }
        public void Play() { }
        public void Pause() { }
        public void Stop() { }
        public bool GetSpectrum(Span<float> bins) => false;
        public (int Width, int Height) VideoSize => (0, 0);
        public VideoFrame? CurrentVideoFrame => null;
        public void Dispose() { }
    }

    private sealed class NameOnlyReader : IMediaInfoReader
    {
        public MediaItem Read(string source) => new(source);
    }

    private sealed class MemoryPreferences : IPreferenceStore
    {
        public string? Load(string themeId, string key) => null;
        public void Save(string themeId, string key, string value) { }
    }

    private sealed class PreviewHost : ISkinHost
    {
        public void ViewChanged(SkinView view) { }
        public void ResizeView(SkinView view, double width, double height) { }
        public (double Width, double Height) MaxViewSize => (2000, 2000);
        public void MinimizeView(SkinView view) { }
        public void BeginResize(SkinView view, string edge) { }
        public void CloseSkin() { }
        public void ReturnToFullMode() { }
        public string? ShowOpenFileDialog(string dialogType, string filter) => null;
        public void LaunchUrl(string url) { }
        public void ShowPopup(SkinView view, IReadOnlyList<string> items, int selectedIndex, Action<int> onSelected) { }
        public int ShowPopupModal(SkinView view, IReadOnlyList<string> items) => -1;
        public void ShowMessage(string text) { }
        public double MeasureText(string text, string fontFamily, double sizePoints, bool bold) => text.Length * sizePoints * 0.6;
        public object? RunCommand(string name, IReadOnlyList<object?> args) => null;
        public void Log(string message) { }
    }
}

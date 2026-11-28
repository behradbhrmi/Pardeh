using Pardeh.Core.Playback;
using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Model;
using Pardeh.Skinning.Runtime;
using Pardeh.Skinning.Wmp;

namespace Pardeh.Skinning.Tests;

internal sealed class FakeEngine : IPlaybackEngine
{
    public bool IsOpen { get; private set; }
    public bool Playing { get; private set; }
    public TimeSpan Position { get; set; }
    public TimeSpan Duration { get; private set; }
    public float Volume { get; set; }
    public float Balance { get; set; }
    public bool Muted { get; set; }
    public EqualizerSettings Equalizer { get; } = new();
    public event EventHandler? PlaybackEnded;

    /// <summary>Audio tracks of whatever is opened (none unless a test lists some).</summary>
    public List<AudioTrack> Tracks { get; } = [];
    public IReadOnlyList<AudioTrack> AudioTracks => Tracks;
    public int CurrentAudioTrack { get; private set; } = -1;
    public void SelectAudioTrack(int index) => CurrentAudioTrack = index;

    public void Open(string source)
    {
        IsOpen = true;
        Duration = TimeSpan.FromMinutes(3);
        Position = TimeSpan.Zero;
        CurrentAudioTrack = Tracks.Count > 0 ? 0 : -1;
    }

    public void Close() => IsOpen = false;
    public void Play() => Playing = true;
    public void Pause() => Playing = false;
    public void Stop() => Playing = false;
    public bool GetSpectrum(Span<float> bins) => false;
    public (int Width, int Height) VideoSize { get; set; }
    public VideoFrame? CurrentVideoFrame => null;
    public void Dispose() { }
    public void End() => PlaybackEnded?.Invoke(this, EventArgs.Empty);
}

internal sealed class FakeInfoReader : IMediaInfoReader
{
    public MediaItem Read(string source)
    {
        var item = new MediaItem(source) { Duration = TimeSpan.FromMinutes(3) };
        item.Attributes["Album"] = "Test Album";
        item.Attributes["Author"] = "Test Artist";
        return item;
    }
}

internal sealed class FakeHost : ISkinHost
{
    public List<string> Logs { get; } = [];
    public List<(double W, double H)> Resizes { get; } = [];
    public string? NextOpenFile { get; set; }
    public SkinView? View { get; private set; }

    public void ViewChanged(SkinView view) => View = view;
    public void ResizeView(SkinView view, double width, double height) => Resizes.Add((width, height));
    public (double Width, double Height) MaxViewSize { get; set; } = (1920, 1040);
    public void MinimizeView(SkinView view) { }
    public void BeginResize(SkinView view, string edge) { }
    public void CloseSkin() { }
    public void ReturnToFullMode() { }
    public TimeSpan DialogDelay { get; set; }
    public string? ShowOpenFileDialog(string dialogType, string filter)
    {
        Thread.Sleep(DialogDelay);
        return NextOpenFile;
    }
    public void LaunchUrl(string url) { }
    public void ShowPopup(SkinView view, IReadOnlyList<string> items, int selectedIndex, Action<int> onSelected) => onSelected(2);
    public int ShowPopupModal(SkinView view, IReadOnlyList<string> items) { LastPopup = items; return PopupChoice; }
    public IReadOnlyList<string>? LastPopup { get; private set; }
    public int PopupChoice { get; set; } = -1;
    public void ShowMessage(string text) { }
    public List<string> Commands { get; } = [];
    public object? RunCommand(string name, IReadOnlyList<object?> args) { Commands.Add(name); return null; }
    public double MeasureText(string text, string fontFamily, double sizePoints, bool bold) => text.Length * sizePoints * 0.75;
    public void Log(string message) => Logs.Add(message);
}

internal sealed class MemoryPreferences : IPreferenceStore
{
    private readonly Dictionary<string, string> _values = [];
    public string? Load(string themeId, string key) => _values.GetValueOrDefault(themeId + "/" + key);
    public void Save(string themeId, string key, string value) => _values[themeId + "/" + key] = value;
}

internal static class TestSkins
{
    /// <summary>Path of the private reference skin, or null when it isn't available (it's never committed).</summary>
    /// <summary>A path under the repo's private/ folder, or null when it doesn't exist.</summary>
    public static string? FindPrivate(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine([dir.FullName, "private", .. parts]);
            if (File.Exists(candidate) || Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public static string? FindPrivateSkin(string fileName)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "private", "skins", fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public static (SkinSession Session, MediaPlayer Player, FakeEngine Engine, FakeHost Host) Start(SkinPackage package)
    {
        var engine = new FakeEngine();
        var player = new MediaPlayer(engine, new FakeInfoReader());
        var host = new FakeHost();
        var session = new SkinSession(package, player, host, new BmpDecoder(), new MemoryPreferences());
        session.Start();
        return (session, player, engine, host);
    }

    public static SkinPackage LoadPrivate(string fileName)
    {
        var path = FindPrivateSkin(fileName);
        Assert.SkipWhen(path is null, $"{fileName} not found in private/skins (reference skins are not committed).");
        return WmzLoader.Load(path!);
    }
}

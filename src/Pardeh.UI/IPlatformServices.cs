using Avalonia.Controls;
using Pardeh.Core.Changes;
using Pardeh.Core.Playback;
using Pardeh.Skinning.Runtime;

namespace Pardeh.UI;

/// <summary>File type registration with the OS (Windows: Open with / Default apps). Offered only where it exists.</summary>
public interface IFileAssociationService
{
    IReadOnlyList<(string Group, string[] Extensions)> Groups { get; }

    bool IsRegistered();

    void Register(IEnumerable<string> extensions);

    void Unregister();

    void OpenDefaultAppsSettings();
}

/// <summary>Imports Windows Media Player 9's original resources from the user's own Windows XP files. Windows only.</summary>
public interface IWmpImportService
{
    string TargetDirectory { get; }

    /// <summary>Imports from <paramref name="source"/> and returns a summary, or throws with a user-readable message.</summary>
    string Import(string source, Action<string> progress);
}

/// <summary>
/// Everything the shared UI needs from the operating system and the app head (Windows desktop, Android).
/// Members have do-nothing defaults so a head implements only what its platform has.
/// </summary>
public interface IPlatformServices
{
    /// <summary>
    /// Hooks the player to the system: media keys / transport controls, taskbar buttons, notification and
    /// media session. Called once the main window or view is showing. Dispose to detach.
    /// </summary>
    IDisposable? StartSystemIntegration(TopLevel top, MediaPlayer player, Action<string> log) => null;

    /// <summary>Android: asks for permission to read the device's music and videos (the media store). True when granted.</summary>
    Task<bool> RequestMediaAccess() => Task.FromResult(true);

    /// <summary>
    /// The screen's density in physical pixels per device-independent pixel when the head knows it before the first layout
    /// (Android: 2.625 on a 420 dpi phone), else 0. The phone looks draw their art at its whole-number rounding.
    /// </summary>
    double DisplayDensity => 0;

    /// <summary>A preview picture (JPEG bytes, about <paramref name="size"/> px wide) of a video's early frame, or null when the platform cannot make one. Called off the UI thread.</summary>
    byte[]? VideoThumbnail(string source, int size) => null;

    /// <summary>True on a head with a media store to list (Android): the library offers "Device music".</summary>
    bool HasDeviceMedia => false;

    /// <summary>The "Classic branding" switch changed (and at start-up): a platform with its own app name and icon (Android launcher aliases, recents) follows it. Default: nothing.</summary>
    void ApplyAppBranding(bool classic) { }

    /// <summary>Single-instance hand-over: called with the arguments of a later launch.</summary>
    void ListenForOpenRequests(Action<string[]> received) { }

    /// <summary>Synchronous "Open" dialog (skin scripts expect the answer immediately). Null: none / cancelled.</summary>
    string? OpenMediaFile(TopLevel? owner) => null;

    bool Confirm(TopLevel? owner, string text, string caption) => false;

    void Inform(TopLevel? owner, string text, string caption) { }

    /// <summary>Rounded window corners (Windows 11). No-op elsewhere.</summary>
    void SetRoundedCorners(TopLevel? window, bool rounded) { }

    /// <summary>Shapes the window to the skin's opaque pixels (desktop only).</summary>
    void ApplyWindowShape(TopLevel? window, SkinMask mask, double pixelScale) { }

    void ClearWindowShape(TopLevel? window) { }

    /// <summary>Android: light (white) icons on the status and navigation bars for a dark skin, dark icons for a light one.</summary>
    void SetSystemBarIcons(bool lightStatusIcons, bool lightNavigationIcons) { }

    /// <summary>Android Back at the root: send the app to the background (not kill it). False when not supported.</summary>
    bool MoveToBackground() => false;

    /// <summary>Android: finish the activity and drop the task (a skin's close button). False when not supported.</summary>
    bool ExitApp() => false;

    void RevealInFileManager(string path) { }

    void OpenFolder(string path) { }

    void OpenUrl(string url) { }

    /// <summary>The installed app's version when the platform records it apart from the assemblies (Android: the package's version name); else null.</summary>
    string? AppVersion => null;

    /// <summary>The platform's build number of the installed app (Android: the version code), as text; else null.</summary>
    string? AppBuild => null;

    /// <summary>
    /// How this platform changes the user's files (tags, names, removal). The default is plain paths with a holding folder
    /// in the user's home folder. PARDEH_HELD_DIR=&lt;folder&gt; is a development aid that moves the holding folder.
    /// </summary>
    IReadOnlyList<IFileBackend> CreateFileBackends() => [new LocalFileBackend(DesktopHoldingFolder())];

    static HoldingFolder DesktopHoldingFolder() =>
        Environment.GetEnvironmentVariable("PARDEH_HELD_DIR") is { Length: > 0 } custom
            ? HoldingFolder.At(custom)
            : HoldingFolder.ForDesktop(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    IFileAssociationService? FileAssociations => null;

    IWmpImportService? WmpImport => null;
}

/// <summary>The platform services of the running head; set once at startup.</summary>
public static class PlatformServices
{
    private sealed class None : IPlatformServices;

    public static IPlatformServices Current { get; set; } = new None();
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Pardeh.App.Platform;

/// <summary>
/// Tools › Options › File Types: registers Pardeh for media and playlist files for the current user (no admin
/// rights), so it appears in "Open with" and in Windows' Default apps, where the user picks it.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class FileAssociations
{
    private const string ProgId = "Pardeh.Media";
    private const string CapabilitiesKey = @"Software\Pardeh\Capabilities";

    public static readonly (string Group, string[] Extensions)[] Groups =
    [
        ("Audio", [".mp3", ".wma", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".mid", ".midi", ".aiff", ".ape"]),
        ("Video", [".mp4", ".m4v", ".mkv", ".wmv", ".avi", ".mov", ".mpg", ".mpeg", ".webm", ".ts", ".asf", ".3gp"]),
        ("Playlists", [".m3u", ".m3u8", ".pls", ".wpl", ".asx", ".xspf"]),
        ("Skins", [".wmz"]),
    ];

    /// <summary>
    /// What Explorer shows for the files Pardeh opens: the video / audio picture of the file with a small Pardeh logo in its corner
    /// (<c>TypeOverlay</c>), and the app icon (all its sizes) only where a file has no picture. Per user, no administrator rights;
    /// done for the programmatic ids Pardeh is known by: its own <c>Pardeh.Media</c> and the <c>Applications\Pardeh.exe</c> id Windows
    /// creates when a file is opened with Pardeh (that is the one a default chosen in Windows' settings uses). Safe to call at every start.
    /// </summary>
    public static void EnsureShellAppearance()
    {
        const string thumbnailHandlerKey = "{e357fccd-a995-4576-b01f-234630154e96}";
        const string systemMediaThumbnails = "{9DBD2C50-62AD-11D0-B806-00C04FD706EC}"; // what Windows itself uses for .mp4, .mkv, .mp3 ...
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !exe.EndsWith("Pardeh.exe", StringComparison.OrdinalIgnoreCase))
            return; // a development run (dotnet run): do not register a build folder
        var icon = exe + ",0";
        var changed = false;
        foreach (var id in new[] { @"Software\Classes\Applications\Pardeh.exe", @"Software\Classes\" + ProgId })
        {
            using var key = Registry.CurrentUser.CreateSubKey(id);
            changed |= Set(key, "TypeOverlay", icon);
            using var defaultIcon = key.CreateSubKey("DefaultIcon");
            changed |= Set(defaultIcon, null, icon);
            using var shellEx = key.CreateSubKey(@"ShellEx\" + thumbnailHandlerKey);
            changed |= Set(shellEx, null, systemMediaThumbnails);
        }

        if (changed)
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED: Explorer re-reads icons and thumbnail types

        static bool Set(RegistryKey key, string? name, string value)
        {
            if (string.Equals(key.GetValue(name) as string, value, StringComparison.Ordinal))
                return false;
            key.SetValue(name, value);
            return true;
        }
    }

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + ProgId);
        return key is not null;
    }

    public static void Register(IEnumerable<string> extensions)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");
        using (var progId = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId))
        {
            progId.SetValue(null, "Pardeh media file");
            using (var icon = progId.CreateSubKey("DefaultIcon"))
                icon.SetValue(null, $"\"{exe}\",0");
            using (var command = progId.CreateSubKey(@"shell\open\command"))
                command.SetValue(null, $"\"{exe}\" \"%1\"");
        }

        using var capabilities = Registry.CurrentUser.CreateSubKey(CapabilitiesKey);
        capabilities.SetValue("ApplicationName", "Pardeh");
        capabilities.SetValue("ApplicationDescription", "Pardeh media player");
        using var fileAssociations = capabilities.CreateSubKey("FileAssociations");
        foreach (var extension in extensions)
        {
            fileAssociations.SetValue(extension, ProgId);
            using var openWith = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}\OpenWithProgids");
            openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        using (var registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            registered.SetValue("Pardeh", CapabilitiesKey);
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
    }

    public static void Unregister()
    {
        foreach (var (_, extensions) in Groups)
        {
            foreach (var extension in extensions)
            {
                using var openWith = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{extension}\OpenWithProgids", writable: true);
                openWith?.DeleteValue(ProgId, throwOnMissingValue: false);
            }
        }

        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + ProgId, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Pardeh\Capabilities", throwOnMissingSubKey: false);
        using (var registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true))
            registered?.DeleteValue("Pardeh", throwOnMissingValue: false);
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Windows decides defaults itself now: open its Default apps page at Pardeh.</summary>
    public static void OpenDefaultAppsSettings() =>
        Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=Pardeh") { UseShellExecute = true });

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}

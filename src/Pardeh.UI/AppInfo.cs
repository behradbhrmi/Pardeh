using Pardeh.Ffmpeg;
using System.Reflection;
using System.Runtime.InteropServices;
using Pardeh.Core.Updates;

namespace Pardeh.UI;

/// <summary>What the About pages of both apps say, from one place.</summary>
public static class AppInfo
{
    public const string Tagline = "Every era on screen.";
    public const string Disclaimer = "An open-source tribute to the classic media players. Not affiliated with Microsoft or any other company whose products inspired a look.";
    public const string LicenseName = "GPL-3.0, free software with no warranty";

    public const string SourcePage = UpdateCheck.Repository;
    public const string ReleasesPage = UpdateCheck.ReleasesPage;
    public const string ReportPage = UpdateCheck.Repository + "/issues/new";
    public const string LicensePage = UpdateCheck.Repository + "/blob/main/LICENSE";
    public const string CreditsPage = UpdateCheck.Repository + "#third-party-libraries";

    /// <summary>The web pages the About pages link to, in order.</summary>
    public static readonly (string Title, string Url)[] Links =
    [
        ("Project on GitHub", SourcePage),
        ("Releases and what's new", ReleasesPage),
        ("Report a problem", ReportPage),
        ("Read the license", LicensePage),
        ("Credits", CreditsPage),
    ];

    private static readonly Lazy<HttpClient> Http = new(() => new HttpClient(new SocketsHttpHandler { UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(8) })
    {
        Timeout = TimeSpan.FromSeconds(20),
        MaxResponseContentBufferSize = 2 * 1024 * 1024,
    });

    /// <summary>"1.2.0+commit", as the build stamped the assemblies.</summary>
    private static string Stamp =>
        (Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly).GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? string.Empty;

    /// <summary>
    /// The version of the installed app. The head says it where the assemblies cannot: an Android release stamps the package,
    /// not the libraries inside it (which would all say 1.0.0). On Windows the build stamps the assemblies.
    /// </summary>
    public static string Version => PlatformServices.Current.AppVersion ?? VersionOf(Stamp);

    /// <summary>The short commit the build was made from, or null.</summary>
    public static string? Commit => CommitOf(Stamp);

    internal static string VersionOf(string stamp) => UpdateCheck.Parse(stamp)?.ToString(3) ?? "unknown";

    internal static string? CommitOf(string stamp) =>
        stamp.IndexOf('+') is var plus && plus >= 0 && stamp.Length - plus > 7 ? stamp.Substring(plus + 1, 7) : null;

    /// <summary>Windows: whether this copy carries its own .NET runtime.</summary>
    public static bool IsStandalone => OperatingSystem.IsWindows() && File.Exists(Path.Combine(AppContext.BaseDirectory, "coreclr.dll"));

    /// <summary>Windows: whether the setup program installed this copy (it leaves its uninstaller next to the app).</summary>
    private static bool IsInstalledBySetup => OperatingSystem.IsWindows() && File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    private static string Decoders =>
        Pardeh.Ffmpeg.FfmpegLibrary.IsAvailable ? "FFmpeg " + Pardeh.Ffmpeg.FfmpegLibrary.Version
        : OperatingSystem.IsWindows() ? "Windows Media Foundation" : "Not available";

    /// <summary>Label and value of each fact about this copy.</summary>
    public static IReadOnlyList<(string Label, string Value)> Facts()
    {
        var facts = new List<(string, string)> { ("Version", Version) };
        var build = string.Join(", ", new[] { PlatformServices.Current.AppBuild, Commit is { } commit ? "commit " + commit : null }.Where(p => p is not null));
        if (build.Length > 0)
            facts.Add(("Build", build));
        if (OperatingSystem.IsWindows())
            facts.Add(("Package", IsStandalone ? "Standalone (includes .NET)" : "Uses the installed .NET runtime"));
        facts.Add(("Runtime", $"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}"));
        facts.Add(("System", RuntimeInformation.OSDescription));
        facts.Add(("Decoders", Decoders));
        facts.Add(("License", LicenseName));
        return facts;
    }

    /// <summary>The facts as text, to paste into a problem report. It holds no names, paths or settings.</summary>
    public static string Details() => "Pardeh\n" + string.Join("\n", Facts().Select(f => $"{f.Label}: {f.Value}"));

    /// <summary>Asks GitHub for a newer release (only when the user asks) and adds which file to take.</summary>
    public static async Task<UpdateResult> CheckForUpdate()
    {
        var result = await UpdateCheck.Run(Http.Value, Version);
        if (result.State != UpdateState.Available)
            return result;
        var file = OperatingSystem.IsAndroid() ? "the .apk file"
            : IsInstalledBySetup ? "the win-x64-setup.exe file"
            : IsStandalone ? "the win-x64-standalone.zip file" : "the win-x64.zip file";
        return result with { Message = $"{result.Message} Open the release page and take {file}." };
    }
}

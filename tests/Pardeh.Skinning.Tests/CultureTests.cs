using System.Globalization;
using Avalonia;
using Avalonia.Headless;
using Pardeh.UI;
using Pardeh.Core.Library;
using Pardeh.Skinning.Model;
using Pardeh.UI.FullMode;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// The skins are generated from code, with numbers written into path data and skin XML. Those strings are machine formats: a
/// regional format whose decimal separator is not "." (fa-IR "٫", de-DE ",") must not change them (this once crashed the
/// Android app under a Persian system language).
/// </summary>
[Collection("Avalonia rendering")]
public sealed class CultureTests : IDisposable
{
    private static readonly object Gate = new();
    private static bool _platformUp;

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"pardeh-culture-{Guid.NewGuid():N}");

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    /// <summary>The skins draw their art into bitmaps, which needs a (headless, Skia) rendering platform.</summary>
    private static void EnsurePlatform()
    {
        lock (Gate)
        {
            if (_platformUp)
                return;
            try
            {
                AppBuilder.Configure<Application>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            }
            catch (InvalidOperationException)
            {
                // Another test class (the icon tests) set the platform up first.
            }

            _platformUp = true;
        }
    }

    public static TheoryData<string> Cultures => ["fa-IR", "de-DE", "ar-SA", "en-US"];

    private static IEnumerable<(string Name, Func<SkinPackage> Build)> Skins()
    {
        foreach (var key in PhoneSkin.Keys)
            yield return (key, () => PhoneSkin.Build(key));
        yield return ("ember", EmberSkin.Build);
        yield return ("aero10", TenSkin.Build);
        yield return ("horizon", HorizonSkin.Build);
        yield return ("pardeh", () => FullModeSkin.Build(null));
        foreach (var design in FullModeDesign.All)
            yield return ("design:" + design.Key, () => FullModeDesigner.Build(design));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Every_generated_skin_loads_and_runs_its_layout_scripts(string name)
    {
        EnsurePlatform();
        CultureInfo.CurrentCulture = new CultureInfo(name);
        CultureInfo.CurrentUICulture = new CultureInfo(name);

        foreach (var (skin, build) in Skins())
        {
            try
            {
                var package = build();
                var (session, _, _, _) = TestSkins.Start(package);
                session.Tick(TimeSpan.FromMilliseconds(30));
                Assert.NotNull(session.CurrentView);
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException($"Skin '{skin}' failed under {name}: {ex}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Skin_art_is_identical_under_every_regional_format(string name)
    {
        EnsurePlatform();
        var reference = Skins().ToDictionary(s => s.Name, s => Snapshot(s.Build()));

        CultureInfo.CurrentCulture = new CultureInfo(name);
        CultureInfo.CurrentUICulture = new CultureInfo(name);
        foreach (var (skin, build) in Skins())
            Assert.Equal(reference[skin], Snapshot(build()));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Skins_load_and_draw_identically_with_the_classic_branding_off(string name)
    {
        EnsurePlatform();
        var branding = AppSettings.Current.ClassicBranding;
        try
        {
            AppSettings.Current.ClassicBranding = false; // in memory only (no Save)
            var reference = Skins().ToDictionary(s => s.Name, s => Snapshot(s.Build()));
            CultureInfo.CurrentCulture = new CultureInfo(name);
            CultureInfo.CurrentUICulture = new CultureInfo(name);
            foreach (var (skin, build) in Skins())
            {
                var package = build();
                Assert.Equal(reference[skin], Snapshot(package));
                var (session, _, _, _) = TestSkins.Start(package);
                session.Tick(TimeSpan.FromMilliseconds(30));
                Assert.NotNull(session.CurrentView);
            }
        }
        finally
        {
            AppSettings.Current.ClassicBranding = branding;
        }
    }

    private static string Snapshot(SkinPackage package)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var file in package.FileNames.OrderBy(f => f, StringComparer.Ordinal))
        {
            var bytes = package.ReadFile(file)!;
            sb.Append(file).Append(':').Append(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))).Append('\n');
        }

        return sb.ToString();
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Library_cache_round_trips_under_every_regional_format(string name)
    {
        CultureInfo.CurrentCulture = new CultureInfo(name);
        CultureInfo.CurrentUICulture = new CultureInfo(name);
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "Song.mp3");
        File.WriteAllText(path, "x");
        var cache = Path.Combine(_folder, "library.json");
        var played = new DateTime(2026, 3, 14, 15, 9, 26, DateTimeKind.Utc);
        File.WriteAllText(cache, System.Text.Json.JsonSerializer.Serialize(new List<LibraryItem>
        {
            new(path, "Song", "Artist", "Album", "Jazz", "2003", 1, 61.5, false, 1, played),
        }));

        var first = new MediaLibrary(new FakeInfoReader(), cache);
        first.LoadCache();
        first.SetRating(path, 4);
        first.RecordPlay(path);

        var second = new MediaLibrary(new FakeInfoReader(), cache);
        second.LoadCache();
        var item = Assert.Single(second.Items);
        Assert.Equal(4, item.Rating);
        Assert.Equal(1, item.PlayCount);
        Assert.Equal(61.5, item.DurationSeconds, 3);
    }
}

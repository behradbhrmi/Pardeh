using System.Globalization;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Pardeh.UI;
using Pardeh.UI.FullMode;
using Pardeh.UI.Rendering;

namespace Pardeh.Skinning.Tests;

/// <summary>The code-drawn pictorial icons and the classic flag logo: they draw something, and the same under every regional format.</summary>
[Collection("Avalonia rendering")]
public sealed class PictorialIconTests : IDisposable
{
    private static readonly object Gate = new();
    private static bool _platformUp;
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }

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
                // Another test class already set the platform up.
            }

            _platformUp = true;
        }
    }

    private static byte[] Draw(string key, int size) => LunaArt.Render(size, size, g => PictorialIcons.Draw(g, key, new Rect(0, 0, size, size)));

    [Fact]
    public void Every_icon_draws_something()
    {
        EnsurePlatform();
        foreach (var key in PictorialIcons.Keys)
            Assert.Contains(Draw(key, 96).Where((_, i) => i % 4 == 3), alpha => alpha > 0);
    }

    /// <summary>The Glass Recorded TV icons (Browse icon and Library tile) exist and carry a red record dot.</summary>
    [Theory]
    [InlineData("b_tv")]
    [InlineData("t_tv")]
    public void Recorded_tv_icon_has_a_red_record_dot(string key)
    {
        EnsurePlatform();
        Assert.Contains(key, PictorialIcons.Keys);
        var pixels = Draw(key, 96);
        var red = 0;
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            // BGRA: clearly red = high R, low G and B.
            if (pixels[i + 3] > 200 && pixels[i + 2] > 170 && pixels[i + 1] < 110 && pixels[i] < 110)
                red++;
        }

        Assert.True(red > 60, $"{key}: only {red} red pixels");
    }

    [Theory]
    [InlineData("fa-IR")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    public void Icons_and_the_flag_are_identical_under_every_regional_format(string name)
    {
        EnsurePlatform();
        var keys = PictorialIcons.Keys.ToList();
        var reference = keys.Select(k => Convert.ToHexString(Draw(k, 64))).ToList();
        var flag = Convert.ToHexString(LunaArt.Render(64, 64, g => ClassicFlag.Draw(g, new Rect(0, 0, 64, 64))));
        CultureInfo.CurrentCulture = new CultureInfo(name);
        CultureInfo.CurrentUICulture = new CultureInfo(name);
        // Geometry caches are filled already: clear nothing, but draw again and also draw keys never drawn before the culture change.
        for (var i = 0; i < keys.Count; i++)
            Assert.Equal(reference[i], Convert.ToHexString(Draw(keys[i], 64)));
        Assert.Equal(flag, Convert.ToHexString(LunaArt.Render(64, 64, g => ClassicFlag.Draw(g, new Rect(0, 0, 64, 64)))));
    }

    /// <summary>Set PARDEH_ICON_SHEET to a .png path to write a contact sheet of the icons (on dark and on pale) for a look.</summary>
    [Fact]
    public void Contact_sheet()
    {
        if (Environment.GetEnvironmentVariable("PARDEH_ICON_SHEET") is not { Length: > 0 } path)
            return;
        EnsurePlatform();
        var keys = PictorialIcons.Keys.ToList();
        if (Environment.GetEnvironmentVariable("PARDEH_ICON_PREFIX") is { Length: > 0 } prefixes) // e.g. "t_,b_,s_,g_": only those families
            keys = keys.Where(k => prefixes.Split(',').Any(k.StartsWith)).ToList();
        const int Cell = 120, Columns = 8;
        var rows = (keys.Count + Columns - 1) / Columns;
        var w = Cell * Columns;
        var h = Cell * rows * 2 + Cell;
        var pixels = LunaArt.Render(w, h, g =>
        {
            g.FillRectangle(new SolidColorBrush(Color.Parse("#0E3599")), new Rect(0, 0, w, Cell * rows));
            g.FillRectangle(new SolidColorBrush(Color.Parse("#F4F8FF")), new Rect(0, Cell * rows, w, Cell * rows));
            for (var i = 0; i < keys.Count; i++)
            {
                var x = i % Columns * Cell + 12;
                var y = i / Columns * Cell + 12;
                PictorialIcons.Draw(g, keys[i], new Rect(x, y, 96, 96));
                PictorialIcons.Draw(g, keys[i], new Rect(x, y + Cell * rows, 96, 96));
            }

            ClassicFlag.Draw(g, new Rect(12, Cell * rows * 2 + 6, 96, 96));
        });
        File.WriteAllBytes(path, LunaArt.Png(pixels, w * 4, 0, 0, w, h));
    }
}

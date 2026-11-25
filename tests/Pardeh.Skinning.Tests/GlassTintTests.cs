using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Model;
using Pardeh.Skinning.Runtime;
using Pardeh.UI;
using Pardeh.UI.FullMode;
using Pardeh.UI.Rendering;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// The skin colour of Pardeh Glass (<see cref="GlassPalette"/>): a hue rotation of the blue chrome only. Blue maps to the target
/// hue, red / yellow / grey / white and alpha stay, the default (Royal blue) is the identity, and a tinted Glass build has the same
/// layout as the default one.
/// </summary>
[Collection("Avalonia rendering")]
public sealed class GlassTintTests : IDisposable
{

    private readonly GlassPalette.Tint _saved = GlassPalette.Current;
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
    private readonly int _scale = PhoneSkin.ArtScale;

    public void Dispose()
    {
        GlassPalette.Current = _saved;
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
        PhoneSkin.ArtScale = _scale;
    }

    private static void EnsurePlatform() => TestPlatform.Ensure();

    private static double HueOf(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var d = max - Math.Min(r, Math.Min(g, b));
        if (d == 0)
            return 0;
        var h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h *= 60;
        return h < 0 ? h + 360 : h;
    }

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    private static double HueDistance(double a, double b) => Math.Abs(((a - b + 540) % 360 + 360) % 360 - 180);

    // ---- The transform --------------------------------------------------------------------------

    [Fact]
    public void The_default_tint_is_the_identity()
    {
        var royal = GlassPalette.Tint.Royal;
        Assert.True(royal.IsDefault);
        foreach (var hex in new[] { "#0A2A82", "#5CC8FF", "#FFFFFF", "#FFB91F", "#CC2222", "#808080", "#1D56CB" })
            Assert.Equal(Color.Parse(hex), GlassPalette.Transform(Color.Parse(hex), royal));
        GlassPalette.Current = royal;
        Assert.False(GlassPalette.Active);
        Assert.Equal("#0A2A82", GlassPalette.Map("#0A2A82"));
        Assert.Equal("<A c=\"#1D56CB\"/>", GlassPalette.MapText("<A c=\"#1D56CB\"/>"));
        Assert.True(new GlassPalette.Tint(220.3, 1.001, 1).IsDefault); // within rounding of the default
        Assert.False(new GlassPalette.Tint(230, 1, 1).IsDefault);
        Assert.False(new GlassPalette.Tint(220, 0.9, 1).IsDefault);
    }

    [Theory]
    [InlineData(150)]
    [InlineData(38)]
    [InlineData(352)]
    [InlineData(268)]
    [InlineData(182)]
    public void Blue_maps_to_the_target_hue_and_keeps_its_alpha(double hue)
    {
        var tint = new GlassPalette.Tint(hue, 1, 1);
        foreach (var hex in new[] { "#1D56CB", "#0A2A82", "#2563D6", "#4F93F5", "#8FC8FF" })
        {
            var blue = Color.Parse(hex);
            var mapped = GlassPalette.Transform(blue, tint);
            // the body blues land on the target hue (the lighter / cyan blues near it, the family compressed)
            Assert.True(HueDistance(HueOf(mapped), hue) < 35, $"{hex} -> {mapped} hue {HueOf(mapped):0}, wanted about {hue}");
            Assert.NotEqual(blue, mapped);
        }

        var body = GlassPalette.Transform(Color.Parse("#1342B3"), tint);
        Assert.True(HueDistance(HueOf(body), hue) < 10, $"body hue {HueOf(body):0}, wanted {hue}");
        var translucent = Color.FromArgb(120, 0x1D, 0x56, 0xCB);
        Assert.Equal(120, GlassPalette.Transform(translucent, tint).A);
    }

    [Fact]
    public void White_grey_red_and_yellow_are_left_alone()
    {
        foreach (var hue in new[] { 150.0, 38, 352, 268 })
        {
            var tint = new GlassPalette.Tint(hue, 1, 1);
            foreach (var hex in new[] { "#FFFFFF", "#000000", "#808080", "#D0D0D0", "#FFE98A", "#FFB91F", "#C07C0C", "#FF4A2A", "#FF9A1F", "#EE4B3A", "#4FB93D", "#F5C518" })
                Assert.Equal(Color.Parse(hex), GlassPalette.Transform(Color.Parse(hex), tint));
        }
    }

    [Fact]
    public void White_text_stays_readable_on_every_preset_and_on_any_hue()
    {
        // The blue chrome is dark; a green or yellow of the same value would be much lighter. The lightness correction keeps it deep.
        // (name, colour of Glass' blue, least contrast with white: Royal blue itself measures 4.9 / 6.5 / 7.4 / 14 / 4.0 / 3.1 / 3.1.)
        var spots = new (string Name, string Hex, double Least)[]
        {
            ("title bar", "#2E6BE0", 3.6), ("title bar top", "#4F93F5", 2.2), ("body top", "#1D56CB", 5.0), ("page header", "#1B4DBE", 5.5),
            ("body bottom", "#0A2A82", 9.0), ("playlist card", "#0A2569", 10.0), ("playing row", "#2B7BF0", 3.0), ("selected row", "#3C93F6", 2.2), ("tab", "#2F7CE8", 3.0),
        };
        var tints = GlassPalette.Presets.Select(p => (p.Key, p.Tint)).Concat(Enumerable.Range(0, 36).Select(i => ($"hue {i * 10}", new GlassPalette.Tint(i * 10, 1, 1))));
        foreach (var (key, tint) in tints)
        {
            foreach (var (name, hex, least) in spots)
            {
                var mapped = GlassPalette.Transform(Color.Parse(hex), tint);
                var ratio = 1.05 / (RelativeLuminance(mapped) + 0.05);
                if (tint.IsDefault)
                    Assert.Equal(Color.Parse(hex), mapped);
                else if (key.StartsWith("hue ", StringComparison.Ordinal))
                    Assert.True(ratio >= least * 0.8, $"white on the {name} ({mapped}) of {key} has contrast {ratio:0.0}, wanted about {least:0.0}"); // a custom hue may be a little lighter
                else
                    Assert.True(ratio >= least, $"white on the {name} ({mapped}) of {key} has contrast {ratio:0.0}, wanted {least:0.0}");
            }
        }
    }

    private static double RelativeLuminance(Color c)
    {
        static double Lin(byte v) => v / 255.0 is var s && s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    [Fact]
    public void Graphite_removes_the_saturation_of_the_blues_only()
    {
        var graphite = GlassPalette.Presets.Single(p => p.Key == "graphite").Tint;
        var mapped = GlassPalette.Transform(Color.Parse("#2563D6"), graphite);
        double max = Math.Max(mapped.R, Math.Max(mapped.G, mapped.B)), min = Math.Min(mapped.R, Math.Min(mapped.G, mapped.B));
        Assert.True((max - min) / max < 0.2, $"graphite should be near grey, got {mapped}");
        Assert.Equal(Color.Parse("#FFE98A"), GlassPalette.Transform(Color.Parse("#FFE98A"), graphite)); // the gold star stays gold
    }

    [Fact]
    public void Text_and_hex_helpers_map_every_blue_and_nothing_else()
    {
        GlassPalette.Current = new GlassPalette.Tint(38, 1, 1);
        var text = GlassPalette.MapText("""<SUBVIEW backgroundColor="#0A2A82" foregroundColor="#FFFFFF" c="#FFE98A" w="#1D56CB"/> #12345 and x""");
        Assert.DoesNotContain("#0A2A82", text);
        Assert.DoesNotContain("#1D56CB", text);
        Assert.Contains("foregroundColor=\"#FFFFFF\"", text);
        Assert.Contains("c=\"#FFE98A\"", text);
        Assert.Contains("#12345 and x", text); // not a six digit colour
        Assert.Matches("backgroundColor=\"#[0-9A-F]{6}\"", text);
        Assert.NotEqual(Color.Parse("#0A2A82"), GlassPalette.Parse("#0A2A82"));
        Assert.NotEqual(Color.Parse("#1D56CB"), GlassPalette.Map(Color.Parse("#1D56CB")));
    }

    [Fact]
    public void Settings_round_trip_into_a_tint_and_the_default_is_royal_blue()
    {
        var settings = new AppSettings();
        Assert.True(GlassPalette.From(settings).IsDefault);
        settings.GlassTintHue = 725; // out of range: wrapped
        settings.GlassTintSaturation = 9;
        settings.GlassTintBrightness = 0;
        var tint = GlassPalette.From(settings);
        Assert.Equal(5, tint.Hue, 6);
        Assert.Equal(1.6, tint.Saturation);
        Assert.Equal(0.5, tint.Brightness);
        Assert.Equal("royal", GlassPalette.PresetOf(GlassPalette.Tint.Royal)!.Key);
        Assert.Null(GlassPalette.PresetOf(new GlassPalette.Tint(100, 1, 1)));
        foreach (var preset in GlassPalette.Presets)
            Assert.Equal(preset.Key, GlassPalette.PresetOf(preset.Tint)!.Key);
    }

    // ---- The Glass build ------------------------------------------------------------------------

    private static SkinPackage BuildGlass(GlassPalette.Tint tint, int scale = 1)
    {
        GlassPalette.Current = tint;
        PhoneSkin.ArtScale = scale;
        return PhoneSkin.Build("glass-phone");
    }

    private static SkinBitmap Decode(SkinPackage package, string name) => new AvaloniaImageDecoder().Decode(package.ReadFile(name)!)!;

    private static Color PixelAt(SkinBitmap bitmap, int x, int y)
    {
        var i = (y * bitmap.Width + x) * 4;
        var a = bitmap.Pixels[i + 3];
        // premultiplied BGRA from the decoder: undo it for the colour
        double f = a == 0 ? 1 : 255.0 / a;
        return Color.FromArgb(a, (byte)Math.Min(255, bitmap.Pixels[i + 2] * f), (byte)Math.Min(255, bitmap.Pixels[i + 1] * f), (byte)Math.Min(255, bitmap.Pixels[i] * f));
    }

    [Fact]
    public void The_default_build_is_deterministic_and_tinting_changes_the_blue_art_but_not_the_logo_or_the_gold_star()
    {
        EnsurePlatform();
        var royal = BuildGlass(GlassPalette.Tint.Royal);
        var again = BuildGlass(GlassPalette.Tint.Royal);
        foreach (var name in new[] { "bar.png", "body.png", "play.png", "pill_l.png", "phone.wms", "phone.js" })
            Assert.Equal(royal.ReadFile(name), again.ReadFile(name));

        var amber = BuildGlass(new GlassPalette.Tint(38, 1, 1.05));
        foreach (var name in new[] { "bar.png", "body.png", "tabs.png", "play.png", "pill_l.png", "seek_fill.png", "phone.wms" })
            Assert.NotEqual(royal.ReadFile(name), amber.ReadFile(name));
        // untouched pictures: the flag logo, the gold star (hue outside the blue band), the transparent hit areas
        foreach (var name in new[] { "logo.png", "star_on.png", "brand_hit.png" })
            Assert.Equal(royal.ReadFile(name), amber.ReadFile(name));

        var bar = Decode(amber, "bar.png");
        var middle = PixelAt(bar, 0, 25);
        Assert.True(middle.R > middle.B + 40, $"the title bar of the amber build should be warm, got {middle}");
        var royalBar = PixelAt(Decode(royal, "bar.png"), 0, 25);
        Assert.True(royalBar.B > royalBar.R + 40, $"the royal title bar is blue, got {royalBar}");

        // no blue left in the layout
        var layout = Encoding.UTF8.GetString(amber.ReadFile("phone.wms")!);
        Assert.DoesNotContain("#0A2A82", layout);
        Assert.DoesNotContain("#1648B8", layout);
        Assert.Contains("foregroundColor=\"#FFFFFF\"", layout);
    }

    [Fact]
    public void A_tinted_build_has_the_same_element_rectangles_as_the_default_one()
    {
        EnsurePlatform();
        var royal = BuildGlass(GlassPalette.Tint.Royal);
        foreach (var tint in new[] { new GlassPalette.Tint(150, 1, 1), new GlassPalette.Tint(38, 1, 1.05), new GlassPalette.Tint(352, 1, 0.95), new GlassPalette.Tint(218, 0.1, 1.05) })
        {
            var tinted = BuildGlass(tint);
            var view1 = StartPackage(royal);
            var view2 = StartPackage(tinted);
            foreach (var id in new[] { "bPlay", "bPrev", "bNext", "bClose", "tabNow", "svPill", "svCard", "ipl", "seek", "svFoot", "svBar", "svTabs" })
            {
                var a = view1.FindById(id)!;
                var b = view2.FindById(id)!;
                Assert.Equal((a.Left, a.Top, a.Width, a.Height), (b.Left, b.Top, b.Width, b.Height));
            }
        }
    }

    [Fact]
    public void Hi_res_art_stays_dense_when_tinted()
    {
        EnsurePlatform();
        var tinted = BuildGlass(new GlassPalette.Tint(150, 1, 1), 3);
        Assert.Equal(3, PngDensity.Read(tinted.ReadFile("tb_close.png")!));
        var plain = BuildGlass(GlassPalette.Tint.Royal, 3);
        var a = Decode(plain, "play.png");
        var b = Decode(tinted, "play.png");
        Assert.Equal((a.Width, a.Height), (b.Width, b.Height));
    }

    [Fact]
    public void A_regional_number_format_does_not_change_a_tinted_build()
    {
        EnsurePlatform();
        var tint = new GlassPalette.Tint(38.5, 0.85, 1.05);
        var invariant = BuildGlass(tint);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("fa-IR");
        var persian = BuildGlass(tint);
        Assert.Equal(invariant.ReadFile("phone.wms"), persian.ReadFile("phone.wms"));
        Assert.Equal(invariant.ReadFile("bar.png"), persian.ReadFile("bar.png"));
        Assert.Equal("38.5/0.85/1.05", tint.Key);
        Assert.Matches("^#[0-9A-F]{6}$", GlassPalette.Map("#0A2A82"));
    }

    [Fact]
    public void The_glass_theme_follows_the_tint_and_is_unchanged_by_default()
    {
        GlassPalette.Current = GlassPalette.Tint.Royal;
        var royal = MobileTheme.ForGlass();
        Assert.Equal(Color.Parse("#071C58"), royal.Back);
        Assert.Equal(Color.Parse("#6FC3FF"), royal.Accent);
        Assert.True(royal.Glass);
        GlassPalette.Current = new GlassPalette.Tint(150, 1, 1);
        var green = MobileTheme.ForGlass();
        Assert.NotEqual(royal.Back, green.Back);
        Assert.NotEqual(royal.Accent, green.Accent);
        Assert.Equal(Colors.White, green.HeadInk);
        Assert.Equal(royal.G("#FFFFFF"), green.G("#FFFFFF"));
    }

    private static SkinView StartPackage(SkinPackage package)
    {
        var session = new SkinSession(package, new Pardeh.Core.Playback.MediaPlayer(new FakeEngine(), new FakeInfoReader()), new FakeHost(), new AvaloniaImageDecoder(), new MemoryPreferences());
        session.Start();
        session.Tick(TimeSpan.FromMilliseconds(30));
        return session.CurrentView!;
    }
}

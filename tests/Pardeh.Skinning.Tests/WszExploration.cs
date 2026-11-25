using Pardeh.Skinning.Model;
using Pardeh.Skinning.Wmp;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// Decodes the compiled skins in the private resource folder (never committed) into readable .wms files,
/// and checks every one parses into a THEME with a VIEW.
/// </summary>
public class WszExploration
{
    [Fact]
    public void Decodes_every_compiled_skin()
    {
        var folder = TestSkins.FindPrivate("wmp9", "res");
        Assert.SkipWhen(folder is null, "private wmp9 resources not present");
        var resources = new WmpResources(folder!);
        var output = Path.Combine(folder!, "..", "decoded");
        Directory.CreateDirectory(output);

        var failures = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(folder!, "text"), "*.WSZ"))
        {
            var theme = new WszDecoder(File.ReadAllBytes(file), resources.GetString).Decode();
            File.WriteAllText(Path.Combine(output, Path.GetFileNameWithoutExtension(file) + ".wms"), SkinNodeWriter.ToXml(theme));
            if (theme.Tag != "THEME" || !theme.Children.Any(c => c.Tag == "VIEW") || theme.Descendants().Any(n => n.Tag.StartsWith("UNKNOWN")))
                failures.Add(Path.GetFileName(file));
        }

        Assert.Empty(failures);
    }
}

public class OriginalFullModeTests
{
    [Fact]
    public void Runs_the_original_full_mode()
    {
        var folder = TestSkins.FindPrivate("wmp9", "res");
        Assert.SkipWhen(folder is null, "private wmp9 resources not present");
        var resources = new WmpResources(folder!);
        var theme = new WszDecoder(resources.GetText("res://wmploc/RT_TEXT/MAINAPPSKIN.WSZ")!, resources.GetString).Decode();
        var package = WmzLoader.FromNode(MainAppSkin.Adapt(theme), "wmp9://mainappskin");

        var engine = new FakeEngine();
        var player = new Pardeh.Core.Playback.MediaPlayer(engine, new FakeInfoReader());
        var host = new FakeHost();
        var session = new Pardeh.Skinning.Runtime.SkinSession(package, player, host, new Pardeh.Skinning.Imaging.BmpDecoder(), new MemoryPreferences()) { Resources = resources };
        session.Start();
        var ticks = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            session.Tick(TimeSpan.FromMilliseconds(30));
            var tc = session.CurrentView!.Elements.OfType<Pardeh.Skinning.Runtime.Elements.HostAreaElement>().First(h => h.IsTaskCenter);
            var sb = tc.Embedded?.FindById("svSidebar");
            ticks.Add($"tick {i}: host {tc.Width}x{tc.Height} sidebar left={sb?.Left} width={sb?.Width} root={tc.Embedded?.Root.Width}");
        }

        player.OpenFiles(["C:/music/x.mp3"]);
        for (var i = 0; i < 3; i++) session.Tick(TimeSpan.FromMilliseconds(30));
        foreach (var id in new[] { "tracktime", "metadata", "svMarquee", "running_background_plugins", "QualityIcon" })
        {
            var e = session.CurrentView!.FindById(id);
            ticks.Add($"{id}: l={e?.Left} t={e?.Top} w={e?.Width} h={e?.Height} vis={e?.Visible} value={e?.GetString("value")} tw={(e as Pardeh.Skinning.Runtime.Elements.TextElement)?.TextWidth} halign={e?.GetString("horizontalAlignment")}");
        }

        foreach (var (px, py) in new[] { (415.0, 6.0), (675.0, 432.0), (200.0, 446.0) })
        {
            ticks.Add($"-- elements covering ({px},{py}):");
            foreach (var e in session.CurrentView!.Elements)
            {
                if (!e.IsVisual) continue;
                var (ax, ay) = e.AbsolutePosition;
                if (px >= ax && px < ax + e.Width && py >= ay && py < ay + e.Height)
                    ticks.Add($"   {e.Tag} id={e.Id} z={e.ZIndex} parent={e.Parent?.Id} abs=({ax},{ay}) {e.Width}x{e.Height} vis={e.Visible} bg={e.GetString("backgroundColor")} img={e.GetString("image") ?? e.GetString("backgroundImage")}");
            }
        }

        ticks.Add("-- top / right edge elements:");
        foreach (var e in session.CurrentView!.Elements)
        {
            var (ax, ay) = e.AbsolutePosition;
            if (e.IsVisual && (ay < 36 || ax + e.Width > 600) && e.Parent?.Id is "svEntireApp" or null or "svUpperRightCorner")
                ticks.Add($"   {e.Tag} id={e.Id} abs=({ax},{ay}) {e.Width}x{e.Height} vis={e.Visible} img={e.GetString("image") ?? e.GetString("backgroundImage")} w-expr={e.Node.Attributes.FirstOrDefault(a => a.Key == "width").Value}");
        }

        if (session.CurrentView!.FindById("seek") is Pardeh.Skinning.Runtime.Elements.SliderElement seek)
            ticks.Add($"seek: min={seek.Min} max={seek.Max} value={seek.Value} enabled={seek.Enabled} vis={seek.Visible} w={seek.Width} h={seek.Height} thumb={seek.ThumbImage?.Width} fg={seek.ForegroundImage?.Width} kind={seek.Kind} dur={player.Duration} open={player.IsAvailable("seek")}");
        var mask = Pardeh.Skinning.Runtime.SkinMask.Compute(session.CurrentView!);
        ticks.Add($"mask (415,6)={mask[415, 6]} (675,432)={mask[675, 432]} (686,440)={mask[686, 440]}");

        File.WriteAllLines(Path.Combine(folder!, "..", "ticks.txt"), ticks);

        // Regressions: numeric attributes stored as text (type 0x0D), live layout after load, the playlist combo.
        var seekSlider = (Pardeh.Skinning.Runtime.Elements.SliderElement)session.CurrentView!.FindById("seek")!;
        Assert.Equal(0, seekSlider.Min);
        Assert.Equal(180, seekSlider.Max);
        Assert.Equal(421, session.CurrentView.FindById("svAppBottomRight")!.AbsolutePosition.Y);
        Assert.IsType<Pardeh.Skinning.Runtime.Elements.DropDownPlaylistElement>(session.CurrentView.FindById("Playlist_Drop_Down"));
        Assert.Equal(155, session.CurrentView.FindById("Playlist_Drop_Down")!.Width);

        var magenta = new List<string>();
        foreach (var element in session.CurrentView!.Elements)
        {
            var images = element switch
            {
                Pardeh.Skinning.Runtime.Elements.ButtonElement b => new[] { (b.CurrentImage, element.GetString("image")) },
                Pardeh.Skinning.Runtime.Elements.ButtonGroupElement g => new[] { (g.BaseImage, element.GetString("image")) },
                Pardeh.Skinning.Runtime.Elements.SubviewElement s => new[] { (s.BackgroundImage, element.GetString("backgroundImage")) },
                _ => [],
            };
            foreach (var (image, name) in images)
            {
                if (image is null) continue;
                var hits = 0;
                for (var y = 0; y < image.Height; y++)
                    for (var x = 0; x < image.Width; x++)
                        if (image.GetAlpha(x, y) > 0 && image.GetRgb(x, y) == 0xFF00FF) hits++;
                if (hits > 0)
                    magenta.Add($"{element.Tag} id={element.Id} image={name} tc={element.GetString("transparencyColor")} hits={hits}");
            }
        }

        File.WriteAllLines(Path.Combine(folder!, "..", "magenta.txt"), magenta);
        var center = session.CurrentView!.Elements.OfType<Pardeh.Skinning.Runtime.Elements.HostAreaElement>().FirstOrDefault(h => h.IsTaskCenter);
        var geometry = new List<string> { $"host {center?.Width}x{center?.Height} at {center?.AbsolutePosition} pane={(center?.Embedded is null ? "none" : "yes")}" };
        if (center?.Embedded is { } pane)
            foreach (var e in pane.Elements)
                geometry.Add($"{new string(' ', Depth(e) * 2)}{e.Tag} {e.Id} l={e.Left} t={e.Top} w={e.Width} h={e.Height} vis={e.Visible} halign={e.GetString("horizontalAlignment")}");
        if (center?.Embedded is { } p2)
        {
            var sidebar = p2.FindById("svSidebar")!;
            geometry.Add("eval: " + p2.Script.EvaluateIn(sidebar, "vMain.width-mediacenter.playlistWidth"));
            geometry.Add("vMain.width: " + p2.Script.Evaluate("vMain.width") + " pw: " + p2.Script.Evaluate("mediacenter.playlistWidth"));
            geometry.Add("explicit left: " + sidebar.ExplicitAttributes.Contains("left") + " node left attr: " + sidebar.Node.GetAttribute("left"));
        }
        File.WriteAllLines(Path.Combine(folder!, "..", "pane.txt"), geometry);
        static int Depth(Pardeh.Skinning.Runtime.Elements.SkinElement e) { var d = 0; for (var p = e.Parent; p is not null; p = p.Parent) d++; return d; }
        File.WriteAllLines(Path.Combine(folder!, "..", "mainapp-log.txt"), host.Logs.Distinct());
        Assert.True(host.Logs.Count < 1000);
    }
}

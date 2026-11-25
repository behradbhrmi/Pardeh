using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using Pardeh.UI.FullMode;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// The vector half: writes the code-drawn <see cref="PardehLogoArt"/> designs as Android vector drawables (they are no longer what
/// the app ships, the default is a picture, see LogoExportImage.cs, but they stay exportable), the
/// .ico container, and renders vector drawables back (a small VectorDrawable reader, independent of the art model) so they can be
/// looked at on Windows.
/// </summary>
internal static partial class LogoExport
{
    private static readonly XNamespace A = "http://schemas.android.com/apk/res/android";
    private static readonly XNamespace Aapt = "http://schemas.android.com/aapt";

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The vector drawables of the vector design (<see cref="PardehLogoArt.VectorDesign"/>): file name to content (not written to the app any more, see the class comment).</summary>
    public static IReadOnlyDictionary<string, string> VectorDrawables() => new Dictionary<string, string>
    {
        ["pardeh_logo.xml"] = Vector(PardehLogoArt.Full, 288, null, $"Pardeh logo (design: {PardehLogoArt.Default}), full colour. Large notification icon, About, splash."),
        ["ic_launcher_foreground.xml"] = Vector(PardehLogoArt.Full, 108, Launcher, $"Adaptive launcher icon, foreground (design: {PardehLogoArt.Default}): the logo inside the 66 dp safe zone.", 108),
        ["ic_launcher_pardeh_background.xml"] = Vector(PardehLogoArt.LauncherBackground, 108, null, "Adaptive launcher icon, background: deep blue stage."),
        ["ic_launcher_monochrome.xml"] = Vector(PardehLogoArt.Monochrome, 108, Launcher, "Themed (monochrome) launcher layer: ring, glass cap tone and play triangle, inside the safe zone.", 108),
        ["ic_stat_pardeh.xml"] = Vector(PardehLogoArt.Silhouette, 24, (1, 0, 2), "Notification small icon: white ring and play triangle on transparent (the system tints it)."),
    };

    private static readonly (double Scale, double X, double Y) Launcher = (PardehLogoArt.LauncherScale, PardehLogoArt.LauncherOffsetX, PardehLogoArt.LauncherOffsetY);

    /// <summary>
    /// One VectorDrawable of <paramref name="dp"/> x <paramref name="dp"/>; the viewport is the picture's own space unless
    /// <paramref name="viewport"/> says otherwise (the launcher layers are 108 and place the 100-unit logo inside with a group transform).
    /// </summary>
    public static string Vector(LogoPicture pic, int dp, (double Scale, double X, double Y)? place, string comment, double? viewport = null)
    {
        var vector = new XElement("vector",
            new XAttribute(XNamespace.Xmlns + "android", A.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "aapt", Aapt.NamespaceName),
            new XAttribute(A + "width", $"{dp}dp"),
            new XAttribute(A + "height", $"{dp}dp"),
            new XAttribute(A + "viewportWidth", F(viewport ?? pic.Size)),
            new XAttribute(A + "viewportHeight", F(viewport ?? pic.Size)));

        var holder = vector;
        if (place is { } p)
        {
            holder = new XElement("group",
                new XAttribute(A + "scaleX", F(p.Scale)), new XAttribute(A + "scaleY", F(p.Scale)),
                new XAttribute(A + "translateX", F(p.X)), new XAttribute(A + "translateY", F(p.Y)));
            vector.Add(holder);
        }

        foreach (var group in pic.Groups)
        {
            var g = new XElement("group");
            if (group.ScaleX != 1 || group.ScaleY != 1)
            {
                g.Add(new XAttribute(A + "pivotX", F(group.PivotX)), new XAttribute(A + "pivotY", F(group.PivotY)),
                    new XAttribute(A + "scaleX", F(group.ScaleX)), new XAttribute(A + "scaleY", F(group.ScaleY)));
            }

            if (group.Clip is { } clip)
                g.Add(new XElement("clip-path", new XAttribute(A + "pathData", clip)));
            foreach (var shape in group.Shapes)
                g.Add(Path(shape));
            holder.Add(g);
        }

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        sb.Append("<!-- ").Append(comment).Append(" Generated from PardehLogoArt.cs by the test LogoExport (vector design, no longer shipped): edit the art there, not here. -->\n");
        sb.Append(Format(vector, 0));
        return sb.ToString();
    }

    private static XElement Path(LogoShape shape)
    {
        var path = new XElement("path");
        if (shape.EvenOdd)
            path.Add(new XAttribute(A + "fillType", "evenOdd"));
        path.Add(new XAttribute(A + "pathData", shape.Path));
        if (shape.Fill is SolidLogoFill solid)
            path.Add(new XAttribute(A + "fillColor", Argb(solid.Color)));
        else if (shape.Fill is { } gradientFill)
            path.Add(Attr("fillColor", gradientFill));
        if (shape.Stroke is not null && shape.StrokeWidth > 0)
        {
            path.Add(new XAttribute(A + "strokeWidth", F(shape.StrokeWidth)));
            path.Add(new XAttribute(A + "strokeLineCap", shape.RoundCaps ? "round" : "butt"));
            path.Add(new XAttribute(A + "strokeLineJoin", "round"));
            if (shape.Stroke is SolidLogoFill s)
                path.Add(new XAttribute(A + "strokeColor", Argb(s.Color)));
            else
                path.Add(Attr("strokeColor", shape.Stroke));
        }

        return path;
    }

    private static XElement Attr(string name, LogoFill fill)
    {
        var gradient = new XElement("gradient");
        (double Offset, string Color)[] stops;
        switch (fill)
        {
            case LinearLogoFill l:
                gradient.Add(new XAttribute(A + "type", "linear"), new XAttribute(A + "startX", F(l.X1)), new XAttribute(A + "startY", F(l.Y1)),
                    new XAttribute(A + "endX", F(l.X2)), new XAttribute(A + "endY", F(l.Y2)));
                stops = l.Stops;
                break;
            case RadialLogoFill r:
                gradient.Add(new XAttribute(A + "type", "radial"), new XAttribute(A + "centerX", F(r.Cx)), new XAttribute(A + "centerY", F(r.Cy)),
                    new XAttribute(A + "gradientRadius", F(r.Radius)));
                stops = r.Stops;
                break;
            default:
                throw new NotSupportedException(fill.GetType().Name);
        }

        foreach (var (offset, color) in stops)
            gradient.Add(new XElement("item", new XAttribute(A + "offset", F(offset)), new XAttribute(A + "color", Argb(color))));
        return new XElement(Aapt + "attr", new XAttribute("name", "android:" + name), gradient);
    }

    /// <summary>Android wants #AARRGGBB (or #RRGGBB); normalised to 8 digits upper case.</summary>
    private static string Argb(string color)
    {
        var hex = color.TrimStart('#').ToUpperInvariant();
        return "#" + (hex.Length == 6 ? "FF" + hex : hex);
    }

    private static string Format(XElement e, int depth)
    {
        var pad = new string(' ', depth * 2);
        var sb = new StringBuilder();
        sb.Append(pad).Append('<').Append(Name(e));
        var attrs = e.Attributes().ToList();
        for (var i = 0; i < attrs.Count; i++)
        {
            var a = attrs[i];
            var name = a.IsNamespaceDeclaration ? "xmlns:" + a.Name.LocalName : Name(a.Name);
            // Keep it readable: namespace declarations and the first attribute stay on the tag line, the rest wrap.
            sb.Append(i == 0 || a.IsNamespaceDeclaration ? " " : "\n" + pad + "    ").Append(name).Append("=\"").Append(System.Security.SecurityElement.Escape(a.Value)).Append('"');
        }

        var kids = e.Elements().ToList();
        if (kids.Count == 0)
            return sb.Append("/>\n").ToString();
        sb.Append(">\n");
        foreach (var k in kids)
            sb.Append(Format(k, depth + 1));
        return sb.Append(pad).Append("</").Append(Name(e)).Append(">\n").ToString();
    }

    private static string Name(XElement e) => Name(e.Name);

    private static string Name(XName n) => n.Namespace == A ? "android:" + n.LocalName : n.Namespace == Aapt ? "aapt:" + n.LocalName : n.LocalName;

    // ---- .ico -----------------------------------------------------------------------------------------------------

    public static readonly int[] IcoSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 192, 256];

    /// <summary>A multi-size Windows icon (PNG-compressed entries, supported since Vista) from PNG frames (size, PNG bytes).</summary>
    public static byte[] IcoFrom(List<(int Size, byte[] Png)> images)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(png.Length);
            w.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in images)
            w.Write(png);
        return ms.ToArray();
    }

    // ---- a small VectorDrawable renderer (to look at the exported XML) -------------------------------------------------

    /// <summary>Draws a VectorDrawable (subset: group, clip-path, path, linear / radial gradients) scaled to <paramref name="r"/>.</summary>
    public static void DrawVector(DrawingContext g, string xml, Rect r, Color? tint = null)
    {
        var root = XDocument.Parse(xml).Root!;
        var vw = double.Parse(root.Attribute(A + "viewportWidth")!.Value, CultureInfo.InvariantCulture);
        var vh = double.Parse(root.Attribute(A + "viewportHeight")!.Value, CultureInfo.InvariantCulture);
        using (g.PushTransform(Matrix.CreateScale(r.Width / vw, r.Height / vh) * Matrix.CreateTranslation(r.X, r.Y)))
            DrawChildren(g, root, tint);
    }

    private static double D(XElement e, string name, double fallback) =>
        e.Attribute(A + name) is { } a ? double.Parse(a.Value, CultureInfo.InvariantCulture) : fallback;

    private static void DrawChildren(DrawingContext g, XElement parent, Color? tint)
    {
        var clipped = new List<IDisposable>();
        foreach (var e in parent.Elements())
        {
            switch (e.Name.LocalName)
            {
                case "group":
                {
                    var px = D(e, "pivotX", 0);
                    var py = D(e, "pivotY", 0);
                    var m = Matrix.CreateTranslation(-px, -py) * Matrix.CreateScale(D(e, "scaleX", 1), D(e, "scaleY", 1)) * Matrix.CreateTranslation(px + D(e, "translateX", 0), py + D(e, "translateY", 0));
                    using (g.PushTransform(m))
                        DrawChildren(g, e, tint);
                    break;
                }

                case "clip-path":
                    clipped.Add(g.PushGeometryClip(Geometry.Parse(e.Attribute(A + "pathData")!.Value)));
                    break;
                case "path":
                {
                    var data = e.Attribute(A + "pathData")!.Value;
                    if (e.Attribute(A + "fillType")?.Value == "evenOdd")
                        data = "F1 " + data;
                    var fill = Paint(e, "fillColor", tint);
                    IPen? pen = null;
                    if (Paint(e, "strokeColor", tint) is { } sb)
                    {
                        var cap = e.Attribute(A + "strokeLineCap")?.Value == "round" ? PenLineCap.Round : PenLineCap.Flat;
                        pen = new Pen(sb, D(e, "strokeWidth", 1), lineCap: cap, lineJoin: PenLineJoin.Round);
                    }

                    g.DrawGeometry(fill, pen, Geometry.Parse(data));
                    break;
                }
            }
        }

        for (var i = clipped.Count - 1; i >= 0; i--)
            clipped[i].Dispose();
    }

    private static IBrush? Paint(XElement path, string attr, Color? tint)
    {
        if (path.Attribute(A + attr) is { } solid)
            return new SolidColorBrush(Tint(Color.Parse(solid.Value), tint));
        var gradient = path.Elements(Aapt + "attr").FirstOrDefault(a => a.Attribute("name")?.Value == "android:" + attr)?.Element("gradient");
        if (gradient is null)
            return null;
        var stops = gradient.Elements("item").Select(i => new GradientStop(Tint(Color.Parse(i.Attribute(A + "color")!.Value), tint), D(i, "offset", 0))).ToList();
        if (gradient.Attribute(A + "type")!.Value == "linear")
        {
            var b = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(D(gradient, "startX", 0), D(gradient, "startY", 0), RelativeUnit.Absolute),
                EndPoint = new RelativePoint(D(gradient, "endX", 0), D(gradient, "endY", 0), RelativeUnit.Absolute),
            };
            foreach (var s in stops)
                b.GradientStops.Add(s);
            return b;
        }

        var c = new RelativePoint(D(gradient, "centerX", 0), D(gradient, "centerY", 0), RelativeUnit.Absolute);
        var radius = D(gradient, "gradientRadius", 1);
        var rb = new RadialGradientBrush
        {
            Center = c,
            GradientOrigin = c,
            RadiusX = new RelativeScalar(radius, RelativeUnit.Absolute),
            RadiusY = new RelativeScalar(radius, RelativeUnit.Absolute),
        };
        foreach (var s in stops)
            rb.GradientStops.Add(s);
        return rb;
    }

    /// <summary>Monochrome / status icons are tinted by the system: keep the alpha, replace the colour.</summary>
    private static Color Tint(Color c, Color? tint) => tint is { } t ? Color.FromArgb(c.A, t.R, t.G, t.B) : c;

    /// <summary>The Android resource folder of this source tree (compiled-in path), or null when it does not exist.</summary>
    public static string? AndroidDrawableDir([CallerFilePath] string source = "")
    {
        var dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(source)!, "..", "..", "src", "Pardeh.Android", "Resources", "drawable"));
        return Directory.Exists(dir) ? dir : null;
    }

    public static string RepoRoot([CallerFilePath] string source = "") => System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(source)!, "..", ".."));
}

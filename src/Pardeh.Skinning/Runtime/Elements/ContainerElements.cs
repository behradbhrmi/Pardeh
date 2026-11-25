using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Model;

namespace Pardeh.Skinning.Runtime.Elements;

/// <summary>&lt;VIEW&gt;: the root of a skin window.</summary>
public sealed class ViewElement(SkinView view, SkinNode node) : SkinElement(view, node, null)
{
    public double MinWidth => GetDouble("minWidth", 0);

    public double MinHeight => GetDouble("minHeight", 0);

    public double MaxWidth => GetDouble("maxWidth", double.MaxValue);

    public double MaxHeight => GetDouble("maxHeight", double.MaxValue);

    public bool Resizable => GetBool("resizable");

    public int? BackgroundColor => SkinValue.ParseColor(Get("backgroundColor"));

    public SkinBitmap? BackgroundImage => Image("backgroundImage");

    public bool BackgroundTiled => GetBool("backgroundTiled");

    protected override IEnumerable<string> KnownProperties =>
        ["minWidth", "minHeight", "maxWidth", "maxHeight", "resizable", "titleBar", "title", "timerInterval", "backgroundImage", "scriptFile"];

    protected override IEnumerable<string> KnownMethods =>
        ["minimize", "maximize", "restore", "close", "size", "returnToMediaCenter", "setTimeout", "updateWindow"];

    internal override void ApplyDefaultSize()
    {
        DefaultSizeFrom(BackgroundImage);
        // Classic full-mode views get their size from saved settings, not the skin.
        if (Width <= 0 && !ExplicitAttributes.Contains("width"))
            SetInitial("width", Math.Max(MinWidth, 690.0));
        if (Height <= 0 && !ExplicitAttributes.Contains("height"))
            SetInitial("height", Math.Max(MinHeight, 500.0));
    }

    protected override void OnPropertyChanged(string name, object? oldValue, object? newValue)
    {
        if (!View.IsInitializing && View.EmbeddedIn is null && (name.Equals("width", StringComparison.OrdinalIgnoreCase) || name.Equals("height", StringComparison.OrdinalIgnoreCase)))
        {
            // Clamp to the view's limits first; the clamped Set re-enters with the final value.
            var screen = View.Session.Host.MaxViewSize;
            var clampedW = Math.Clamp(Width, MinWidth, Math.Max(MinWidth, Math.Min(MaxWidth, screen.Width)));
            var clampedH = Math.Clamp(Height, MinHeight, Math.Max(MinHeight, Math.Min(MaxHeight, screen.Height)));
            if (Math.Abs(clampedW - Width) > 0.01 || Math.Abs(clampedH - Height) > 0.01)
            {
                Width = clampedW;
                Height = clampedH;
            }
        }

        base.OnPropertyChanged(name, oldValue, newValue);

        if (!View.IsInitializing && View.EmbeddedIn is null && (name.Equals("width", StringComparison.OrdinalIgnoreCase) || name.Equals("height", StringComparison.OrdinalIgnoreCase)))
            View.Session.Host.ResizeView(View, Width, Height);
    }

    public override object? InvokeMember(string name, object?[] args)
    {
        var host = View.Session.Host;
        switch (name.ToLowerInvariant())
        {
            case "minimize":
                host.MinimizeView(View);
                return null;
            case "maximize" or "restore":
                // The window toggles; ask only when the state really changes.
                var maximized = SkinValue.ToBool(host.RunCommand("isMaximized", []));
                if (maximized != (name.Equals("maximize", StringComparison.OrdinalIgnoreCase)))
                    host.RunCommand("maximize", []);
                return null;
            case "updatewindow":
                View.Invalidate(); // the player repainted at once; we redraw every frame anyway
                return null;
            case "close":
                View.Session.RequestClose();
                return null;
            case "size":
                host.BeginResize(View, SkinValue.ToText(Arg(args, 0)));
                return null;
            case "returntomediacenter":
                host.ReturnToFullMode();
                return null;
            default:
                return base.InvokeMember(name, args);
        }
    }

    public override bool HitTest(double x, double y) => false;
}

/// <summary>&lt;SUBVIEW&gt;: a positioned container, usually with a background image.</summary>
public sealed class SubviewElement(SkinView view, SkinNode node, SkinElement? parent) : SkinElement(view, node, parent)
{
    public int? BackgroundColor => SkinValue.ParseColor(Get("backgroundColor"));

    private (SkinBitmap Source, int W, int H, SkinBitmap Result)? _resized;

    /// <summary>The background, stretched to the subview when resizeBackgroundImage is set.</summary>
    public SkinBitmap? BackgroundImage
    {
        get
        {
            var image = Image("backgroundImage");
            if (image is null || !GetBool("resizeBackgroundImage") || Width < 1 || Height < 1)
                return image;
            int w = (int)Width, h = (int)Height;
            if (_resized is not { } r || !ReferenceEquals(r.Source, image) || r.W != w || r.H != h)
                _resized = r = (image, w, h, image.ResizedToUnits(w, h));
            return r.Result;
        }
    }

    public bool BackgroundTiled => GetBool("backgroundTiled");

    /// <summary>
    /// Opt-in (Pardeh's own skins): the background is drawn as a nine-patch over the subview, its corners (this many skin units)
    /// kept as they are and the rest stretched; 0 stretches the whole picture. Null: the classic behaviour.
    /// </summary>
    public double? SliceBorder => Get("sliceBorder") is null ? null : Math.Max(0, GetDouble("sliceBorder"));

    protected override IEnumerable<string> KnownProperties => ["backgroundImage", "backgroundTiled", "transparencyColor", "resizeBackgroundImage", "sliceBorder"];

    internal override void ApplyDefaultSize() => DefaultSizeFrom(BackgroundImage);

    /// <summary>Opaque background pixels catch the mouse (to drag the window); transparent ones don't.</summary>
    public override bool HitTest(double x, double y)
    {
        if (!base.HitTest(x, y))
            return false;
        if (BackgroundColor is not null)
            return true;

        var image = BackgroundImage;
        if (image is null)
            return false;

        // Positions are in skin units; a high-resolution image is sampled at its own density.
        if (BackgroundTiled)
        {
            var tileW = Math.Max(1, (int)image.LogicalWidth);
            var tileH = Math.Max(1, (int)image.LogicalHeight);
            var tx = (int)x % tileW;
            var ty = (int)y % tileH;
            return image.GetAlphaAt(tx + (x - (int)x), ty + (y - (int)y)) > 0;
        }

        return image.GetAlphaAt(x, y) > 0;
    }
}

/// <summary>Elements we don't render yet (ACTIVEX, unknown tags): kept in the tree so scripts can still talk to them.</summary>
public sealed class GenericElement(SkinView view, SkinNode node, SkinElement? parent) : SkinElement(view, node, parent)
{
    public int? BackgroundColor => SkinValue.ParseColor(Get("backgroundColor"));

    public override bool HitTest(double x, double y) => BackgroundColor is not null && base.HitTest(x, y);
}

using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Model;
using Pardeh.Skinning.Runtime.Scripting;

namespace Pardeh.Skinning.Runtime.Elements;

public enum Alignment
{
    Near,
    Far,
    Center,
    Stretch,
}

/// <summary>
/// Base of every skin element. All attributes live in a case-insensitive property bag that scripts read and write;
/// typed accessors and side effects (resize propagation, change events) sit on top of it.
/// </summary>
public class SkinElement : IScriptable
{
    private static readonly HashSet<string> CommonProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "left", "top", "width", "height", "visible", "enabled", "zIndex", "alpha", "tooltip", "upToolTip",
        "downToolTip", "cursor", "tabStop", "horizontalAlignment", "verticalAlignment", "clippingColor", "clippingImage",
        "passThrough", "backgroundColor",
    };

    private static readonly HashSet<string> CommonMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "moveTo", "resize", "setFocus", "fadeIn", "fadeOut", "alphaBlendTo", "setColumnResizeMode", "setColumnWidth",
        "getColumnWidth", "sort", "focus", "getProperty", "setProperty",
    };

    private readonly Dictionary<string, object?> _properties = new(StringComparer.OrdinalIgnoreCase);
    private MoveAnimation? _move;
    private AlphaAnimation? _fade;

    public SkinElement(SkinView view, SkinNode node, SkinElement? parent)
    {
        View = view;
        Node = node;
        Parent = parent;
    }

    public SkinView View { get; }

    public SkinNode Node { get; }

    public SkinElement? Parent { get; }

    public List<SkinElement> Children { get; } = [];

    /// <summary>Event handler code by lower-case event name ("onclick", "value_onchange" …).</summary>
    public Dictionary<string, string> Handlers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Attributes that the skin set explicitly (literal, script or binding).</summary>
    public HashSet<string> ExplicitAttributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string Tag => Node.Tag;

    public string? Id => Get("id") as string;

    // ---- Typed geometry & state -------------------------------------------------------------

    public double Left { get => GetDouble("left"); set => Set("left", value); }

    public double Top { get => GetDouble("top"); set => Set("top", value); }

    public double Width { get => GetDouble("width"); set => Set("width", Math.Max(0, value)); }

    public double Height { get => GetDouble("height"); set => Set("height", Math.Max(0, value)); }

    public bool Visible { get => GetBool("visible", true); set => Set("visible", value); }

    public bool Enabled { get => GetBool("enabled", true); set => Set("enabled", value); }

    public int ZIndex => (int)GetDouble("zIndex");

    /// <summary>0 – 255.</summary>
    public double Alpha => Math.Clamp(GetDouble("alpha", 255), 0, 255);

    /// <summary>Whether the element draws anything (non-visual elements like EQUALIZERSETTINGS return false).</summary>
    public virtual bool IsVisual => true;

    public bool IsHovered { get; private set; }

    public bool IsPressed { get; private set; }

    public Alignment HorizontalAlignment => ParseAlignment(GetString("horizontalAlignment"));

    public Alignment VerticalAlignment => ParseAlignment(GetString("verticalAlignment"));

    /// <summary>Object used as the <c>with</c> scope for this element's handlers.</summary>
    public virtual IScriptable ScriptTarget => this;

    // ---- Property bag ---------------------------------------------------------------------------

    public object? Get(string name) => _properties.TryGetValue(name, out var value) ? value : GetDefault(name);

    public bool HasProperty(string name) => _properties.ContainsKey(name);

    public double GetDouble(string name, double fallback = 0) => SkinValue.ToDouble(Get(name), fallback);

    public bool GetBool(string name, bool fallback = false) => SkinValue.ToBool(Get(name), fallback);

    public string? GetString(string name)
    {
        var value = Get(name);
        return value is null ? null : SkinValue.ToText(value);
    }

    /// <summary>Sets a property, applies side effects and fires <c>name_onchange</c>.</summary>
    public void Set(string name, object? value, bool fireChange = true)
    {
        value = ResolveString(value);
        var old = Get(name);
        _properties[name] = value;
        if (SkinValue.AreEqual(old, value))
            return;

        if (AffectsShape(name))
            View.LayoutVersion++;
        OnPropertyChanged(name, old, value);
        if (fireChange && !View.IsInitializing)
            FireEvent(name + "_onchange");
        View.Invalidate();
    }

    /// <summary>Properties that can change which pixels of the window are opaque.</summary>
    private static bool AffectsShape(string name) => name.ToLowerInvariant() switch
    {
        "left" or "top" or "width" or "height" or "visible" or "zindex" or "image" or "backgroundimage"
            or "backgroundcolor" or "transparencycolor" or "thumbimage" or "clippingcolor" or "clippingimage" => true,
        _ => false,
    };

    /// <summary>Sets a property during load: no events, no resize propagation.</summary>
    internal void SetInitial(string name, object? value) => _properties[name] = ResolveString(value);

    /// <summary>"res://wmploc.dll/RT_STRING/#1452" → the string itself, as the player does when such a value is set.</summary>
    private object? ResolveString(object? value) =>
        value is string text && text.StartsWith("res://", StringComparison.OrdinalIgnoreCase)
            && text.Contains("RT_STRING", StringComparison.OrdinalIgnoreCase)
            && View.Session.Resources?.GetString(text) is { } resolved
            ? resolved
            : value;

    protected virtual object? GetDefault(string name) => name.ToLowerInvariant() switch
    {
        "visible" or "enabled" => true,
        "left" or "top" or "width" or "height" or "zindex" => 0.0,
        "alpha" => 255.0,
        _ => null,
    };

    protected virtual void OnPropertyChanged(string name, object? oldValue, object? newValue)
    {
        if (View.IsInitializing)
            return;

        if (name.Equals("width", StringComparison.OrdinalIgnoreCase))
        {
            var delta = SkinValue.ToDouble(newValue) - SkinValue.ToDouble(oldValue);
            // A right-aligned element keeps its right edge when resized (Now Playing's sidebar relies on this).
            if (HorizontalAlignment == Alignment.Far)
                Left -= delta;
            foreach (var child in Children)
                child.OnParentResized(delta, 0);
            FireEvent("onresize");
        }
        else if (name.Equals("height", StringComparison.OrdinalIgnoreCase))
        {
            var delta = SkinValue.ToDouble(newValue) - SkinValue.ToDouble(oldValue);
            if (VerticalAlignment == Alignment.Far)
                Top -= delta;
            foreach (var child in Children)
                child.OnParentResized(0, delta);
            FireEvent("onresize");
        }
        else if (name.Equals("enabled", StringComparison.OrdinalIgnoreCase) && !Enabled)
        {
            IsPressed = false;
        }
    }

    /// <summary>Applies this element's alignment when its parent changes size.</summary>
    internal void OnParentResized(double dw, double dh)
    {
        if (dw != 0)
        {
            switch (HorizontalAlignment)
            {
                case Alignment.Far: Left += dw; break;
                case Alignment.Center: Left += dw / 2; break;
                case Alignment.Stretch: Width += dw; break;
            }
        }

        if (dh != 0)
        {
            switch (VerticalAlignment)
            {
                case Alignment.Far: Top += dh; break;
                case Alignment.Center: Top += dh / 2; break;
                case Alignment.Stretch: Height += dh; break;
            }
        }
    }

    private static Alignment ParseAlignment(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "right" or "bottom" => Alignment.Far,
        "center" => Alignment.Center,
        "stretch" => Alignment.Stretch,
        _ => Alignment.Near,
    };

    // ---- Geometry helpers -------------------------------------------------------------------------

    /// <summary>Position of the element's top-left corner in view coordinates.</summary>
    public (double X, double Y) AbsolutePosition
    {
        get
        {
            double x = 0, y = 0;
            for (var e = this; e is not null && e is not ViewElement; e = e.Parent)
            {
                x += e.Left;
                y += e.Top;
            }

            // Elements of an embedded pane are positioned inside their task-center host.
            if (View.EmbeddedIn is { } host)
            {
                var (hx, hy) = host.AbsolutePosition;
                x += hx;
                y += hy;
            }

            return (x, y);
        }
    }

    /// <summary>Applies default sizes once all attributes are known (e.g. from the background image).</summary>
    internal virtual void ApplyDefaultSize()
    {
    }

    protected void DefaultSizeFrom(SkinBitmap? bitmap)
    {
        if (bitmap is null)
            return;
        if (!ExplicitAttributes.Contains("width"))
            SetInitial("width", bitmap.LogicalWidth);
        if (!ExplicitAttributes.Contains("height"))
            SetInitial("height", bitmap.LogicalHeight);
    }

    /// <summary>Loads an image property (applying transparencyColor) from the skin.</summary>
    protected SkinBitmap? Image(string property)
    {
        var name = GetString(property);
        if (name is not null && name.StartsWith("WMPImage_", StringComparison.OrdinalIgnoreCase))
            return property.Equals("backgroundImage", StringComparison.OrdinalIgnoreCase)
                ? View.Session.PlayerImage(name, Width, Height, GetBool("coverFill"), GetDouble("coverRadius"), (int)GetDouble("coverScale", 1))
                : View.Session.PlayerImage(name, Parent?.Width ?? Width, Parent?.Height ?? Height, density: (int)GetDouble("coverScale", 1));

        // The colour chooser: subview backgrounds use backgroundImageHueShift / …Saturation, everything else hueShift / saturation.
        var background = property.Equals("backgroundImage", StringComparison.OrdinalIgnoreCase);
        var hue = GetDouble(background ? "backgroundImageHueShift" : "hueShift");
        var saturation = GetDouble(background ? "backgroundImageSaturation" : "saturation", 1);
        return View.Session.Images.Get(name, TransparencyKeyFor(name), hue, saturation);
    }

    /// <summary>
    /// transparencyColor, else clippingColor; the player's own images (res://) use magenta even when unstated.
    /// </summary>
    private string? TransparencyKeyFor(string? image) =>
        GetString("transparencyColor") is { Length: > 0 } key && !key.Equals("none", StringComparison.OrdinalIgnoreCase) ? key
        : GetString("clippingColor") is { Length: > 0 } clip && !clip.Equals("none", StringComparison.OrdinalIgnoreCase) ? clip
        : image is not null && image.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ? "#FF00FF"
        : null;

    // ---- Input --------------------------------------------------------------------------------------

    /// <summary>True if the element reacts to the mouse (otherwise its opaque pixels drag the window).</summary>
    public virtual bool IsInteractive =>
        Handlers.ContainsKey("onclick") || Handlers.ContainsKey("onmousedown") || Handlers.ContainsKey("ondblclick");

    /// <summary>
    /// A press here also moves the window, even though <see cref="IsInteractive"/> may be true: the view itself (its mouse
    /// handlers belong to the window, such as the original full mode's right-click menu) and purely decorative buttons.
    /// </summary>
    public virtual bool MovesWindow => Parent is null && !Handlers.ContainsKey("onclick");

    /// <summary>Hit test in local coordinates.</summary>
    public virtual bool HitTest(double x, double y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public virtual string? ToolTip => GetString("toolTip") ?? GetString("upToolTip");

    /// <summary>The tip for a place inside the element (a mark in a list row); by default the element's own.</summary>
    public virtual string? ToolTipAt(double x, double y) => ToolTip;

    public virtual string? Cursor => GetString("cursor");

    public virtual void OnPointerEnter()
    {
        IsHovered = true;
        FireEvent("onmouseover");
        View.Invalidate();
    }

    public virtual void OnPointerLeave()
    {
        IsHovered = false;
        FireEvent("onmouseout");
        View.Invalidate();
    }

    public virtual void OnPointerPressed(double x, double y)
    {
        IsPressed = true;
        FireEvent("onmousedown");
        View.Invalidate();
    }

    public virtual void OnPointerMoved(double x, double y)
    {
        // Resize bars and the like track the mouse with onmousemove (reading event.clientX / clientY).
        if (Handlers.ContainsKey("onmousemove"))
            FireEvent("onmousemove");
    }

    public virtual void OnPointerReleased(double x, double y, bool inside)
    {
        var wasPressed = IsPressed;
        IsPressed = false;
        FireEvent("onmouseup");
        if (inside && wasPressed && Enabled)
            OnClick();
        View.Invalidate();
    }

    public virtual void OnDoubleClick(double x, double y) => FireEvent("ondblclick");

    public virtual void OnWheel(double delta)
    {
    }

    /// <summary>Touch: how many skin pixels of finger travel make one scroll step.</summary>
    public virtual double TouchScrollStep => 36;

    /// <summary>Touch: one scroll step (sign = direction, like the wheel). The default is a wheel notch.</summary>
    public virtual void OnTouchScroll(double step) => OnWheel(step);

    /// <summary>A right-click on the element. Return true when it showed its own menu (the app menu is skipped).</summary>
    public virtual bool OnContextMenu(double x, double y) => false;

    protected virtual void OnClick() => FireEvent("onclick");

    // ---- Events & animation ---------------------------------------------------------------------------

    public void FireEvent(string eventName, params (string Name, object? Value)[] args)
    {
        if (Handlers.TryGetValue(eventName, out var code))
            View.RunHandler(this, code, args);
    }

    public void MoveTo(double x, double y, double milliseconds)
    {
        if (milliseconds <= 0)
        {
            Left = x;
            Top = y;
            _move = null;
            FireEvent("onendmove");
            return;
        }

        _move = new MoveAnimation(Left, Top, x, y, milliseconds);
        View.Animate(this);
    }

    /// <summary>Advances animations. Returns true while still animating.</summary>
    internal bool AdvanceAnimation(double elapsedMs)
    {
        var active = false;
        if (_move is { } move)
        {
            move.Elapsed += elapsedMs;
            var t = Math.Min(1, move.Elapsed / move.Duration);
            Left = Math.Round(move.FromX + (move.ToX - move.FromX) * t);
            Top = Math.Round(move.FromY + (move.ToY - move.FromY) * t);
            if (t >= 1)
            {
                _move = null;
                FireEvent("onendmove");
                // The handler may start the next move at once (the colour chooser polls this way).
                active |= _move is not null;
            }
            else
            {
                active = true;
            }
        }

        if (_fade is { } fade)
        {
            fade.Elapsed += elapsedMs;
            var t = Math.Min(1, fade.Elapsed / fade.Duration);
            Set("alpha", fade.From + (fade.To - fade.From) * t);
            if (t >= 1)
            {
                _fade = null;
                if (fade.To <= 0)
                    Visible = false;
                // The full mode shows / hides its window buttons here (e.g. restore vs maximize).
                FireEvent("onendalphablend");
                active |= _fade is not null;
            }
            else
            {
                active = true;
            }
        }

        return active;
    }

    private sealed class MoveAnimation(double fromX, double fromY, double toX, double toY, double duration)
    {
        public double FromX { get; } = fromX;
        public double FromY { get; } = fromY;
        public double ToX { get; } = toX;
        public double ToY { get; } = toY;
        public double Duration { get; } = duration;
        public double Elapsed { get; set; }
    }

    private sealed class AlphaAnimation(double from, double to, double duration)
    {
        public double From { get; } = from;
        public double To { get; } = to;
        public double Duration { get; } = duration;
        public double Elapsed { get; set; }
    }

    // ---- IScriptable ------------------------------------------------------------------------------------

    /// <summary>Extra property names a subclass understands even when the skin didn't set them.</summary>
    protected virtual IEnumerable<string> KnownProperties => [];

    /// <summary>Extra method names a subclass implements.</summary>
    protected virtual IEnumerable<string> KnownMethods => [];

    public virtual MemberKind GetMemberKind(string name)
    {
        if (CommonMethods.Contains(name) || KnownMethods.Contains(name, StringComparer.OrdinalIgnoreCase))
            return MemberKind.Method;
        if (_properties.ContainsKey(name) || CommonProperties.Contains(name) || KnownProperties.Contains(name, StringComparer.OrdinalIgnoreCase))
            return MemberKind.Property;
        return MemberKind.None;
    }

    public virtual object? GetMember(string name) => Get(name);

    /// <summary>Properties a script has set; their load-time expressions stop following what they depended on.</summary>
    internal HashSet<string> ScriptSet { get; } = new(StringComparer.OrdinalIgnoreCase);

    public virtual void SetMember(string name, object? value)
    {
        if (!View.IsInitializing)
            ScriptSet.Add(name);
        Set(name, value);
    }

    public virtual object? InvokeMember(string name, object?[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "setproperty":
                Set(SkinValue.ToText(Arg(args, 0)), Arg(args, 1));
                return null;
            case "getproperty":
                return Get(SkinValue.ToText(Arg(args, 0)));
            case "moveto":
                MoveTo(SkinValue.ToDouble(Arg(args, 0)), SkinValue.ToDouble(Arg(args, 1)), SkinValue.ToDouble(Arg(args, 2)));
                return null;
            case "resize":
                Width = SkinValue.ToDouble(Arg(args, 2), Width);
                Height = SkinValue.ToDouble(Arg(args, 3), Height);
                return null;
            case "fadein":
                Visible = true;
                _fade = new AlphaAnimation(0, 255, Math.Max(1, SkinValue.ToDouble(Arg(args, 0), 250)));
                View.Animate(this);
                return null;
            case "fadeout":
                _fade = new AlphaAnimation(Alpha, 0, Math.Max(1, SkinValue.ToDouble(Arg(args, 0), 250)));
                View.Animate(this);
                return null;
            case "alphablendto":
                _fade = new AlphaAnimation(Alpha, SkinValue.ToDouble(Arg(args, 0), 255), Math.Max(1, SkinValue.ToDouble(Arg(args, 1), 250)));
                View.Animate(this);
                return null;
            default:
                return null; // setFocus, column helpers… are accepted as no-ops.
        }
    }

    protected static object? Arg(object?[] args, int index) => index < args.Length ? args[index] : null;

    public override string ToString() => Node.ToString();
}

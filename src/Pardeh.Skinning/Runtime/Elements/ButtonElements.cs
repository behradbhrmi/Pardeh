using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Model;

namespace Pardeh.Skinning.Runtime.Elements;

/// <summary>&lt;BUTTON&gt; and the typed buttons (PLAYBUTTON, PAUSEBUTTON, REWBUTTON, RETURNBUTTON …).</summary>
public sealed class ButtonElement : SkinElement, IPlayerSynced
{
    public ButtonElement(SkinView view, SkinNode node, SkinElement? parent) : base(view, node, parent)
    {
        Action = Transport.FromTag(node.Tag);
    }

    public TransportAction Action { get; }

    public bool Sticky => GetBool("sticky");

    public bool Down { get => GetBool("down"); set => Set("down", value); }

    protected override IEnumerable<string> KnownProperties =>
        ["down", "sticky", "image", "hoverImage", "downImage", "disabledImage", "hoverDownImage", "transparencyColor"];

    /// <summary>The image for the current state.</summary>
    public SkinBitmap? CurrentImage
    {
        get
        {
            var normal = Image("image");
            if (!Enabled)
                return Image("disabledImage") ?? normal;
            if (Down || (IsPressed && IsHovered))
                return (IsHovered ? Image("hoverDownImage") : null) ?? Image("downImage") ?? normal;
            if (IsHovered)
                return Image("hoverImage") ?? normal;
            return normal;
        }
    }

    public override bool IsInteractive => true;

    /// <summary>
    /// A &lt;BUTTON&gt; that does nothing when pressed is a piece of the window's frame (the original full mode draws its
    /// top and bottom bars this way).
    /// </summary>
    public override bool MovesWindow =>
        Action == TransportAction.None && !Sticky && GetString("cursor") is null && !Handlers.Keys.Any(IsPressHandler);

    private static bool IsPressHandler(string name) =>
        name.Equals("onclick", StringComparison.OrdinalIgnoreCase) || name.Equals("ondblclick", StringComparison.OrdinalIgnoreCase)
        || name.Equals("onmousedown", StringComparison.OrdinalIgnoreCase) || name.Equals("onmouseup", StringComparison.OrdinalIgnoreCase)
        || name.Equals("onlongpress", StringComparison.OrdinalIgnoreCase) || name.Equals("down_onchange", StringComparison.OrdinalIgnoreCase);

    public override string? ToolTip =>
        Down ?GetString("downToolTip") ?? GetString("upToolTip") ?? GetString("toolTip") : base.ToolTip;

    internal override void ApplyDefaultSize() => DefaultSizeFrom(Image("image"));

    public override bool HitTest(double x, double y)
    {
        if (!base.HitTest(x, y))
            return false;
        var image = Image("image");
        if (image is null || x >= image.LogicalWidth || y >= image.LogicalHeight)
            return true;
        if (image.GetAlphaAt(x, y) > 0)
            return true;

        // A glyph-only button (a thin icon on transparency, with a plate drawn behind it on hover / press) must be
        // clickable over its whole face, not only on the strokes.
        if ((Image("hoverImage") is { } hover && hover.GetAlphaAt(x, y) > 0)
            || (Image("downImage") is { } pressed && pressed.GetAlphaAt(x, y) > 0))
            return true;
        return IsSparse(image) && HasInkNear(image, x, y, 6);
    }

    private static bool HasInkNear(SkinBitmap image, double x, double y, double radius)
    {
        for (var dy = -radius; dy <= radius; dy += radius / 2)
            for (var dx = -radius; dx <= radius; dx += radius / 2)
                if (image.GetAlphaAt(x + dx, y + dy) > 0)
                    return true;
        return false;
    }

    /// <summary>True when less than a third of the image is drawn (an icon, as opposed to a filled button face).</summary>
    private static bool IsSparse(SkinBitmap image)
    {
        const int Steps = 12;
        var inked = 0;
        for (var j = 0; j < Steps; j++)
            for (var i = 0; i < Steps; i++)
                if (image.GetAlphaAt((i + 0.5) * image.LogicalWidth / Steps, (j + 0.5) * image.LogicalHeight / Steps) > 0)
                    inked++;
        return inked * 3 < Steps * Steps;
    }

    /// <summary>A long press (touch) or right click runs the optional <c>onLongPress</c> handler instead of opening the app menu.</summary>
    public override bool OnContextMenu(double x, double y)
    {
        if (!Handlers.ContainsKey("onlongpress"))
            return false;
        FireEvent("onlongpress");
        return true;
    }

    protected override void OnClick()
    {
        if (Sticky)
            Down = !Down;

        if (Handlers.ContainsKey("onclick"))
            FireEvent("onclick");
        else
            Transport.Execute(Action, View.Session);
    }

    public void SyncWithPlayer()
    {
        // Player-provided images (album art) follow the media and their container's size.
        if (GetString("image") is { } image && image.StartsWith("WMPImage_", StringComparison.OrdinalIgnoreCase))
        {
            var art = Image("image");
            Set("width", art?.LogicalWidth ?? 0, fireChange: false);
            Set("height", art?.LogicalHeight ?? 0, fireChange: false);
            Set("left", 0.0, fireChange: false);
            Set("top", 0.0, fireChange: false);
        }

        if (Action == TransportAction.None)
            return;
        var player = View.Session.Player;
        if (!ExplicitAttributes.Contains("enabled"))
            Enabled = Transport.IsAvailable(Action, player);
        if (!ExplicitAttributes.Contains("down") && Transport.DownState(Action, player) is { } down)
            Down = down;
    }
}

/// <summary>
/// &lt;BUTTONGROUP&gt;: one set of images for several buttons; a colour-coded mapping image says which pixel belongs to which button.
/// </summary>
public sealed class ButtonGroupElement(SkinView view, SkinNode node, SkinElement? parent) : SkinElement(view, node, parent)
{
    // Classic button-group art uses magenta as its transparency key even when the skin doesn't say so.
    protected override object? GetDefault(string name) =>
        name.Equals("transparencyColor", StringComparison.OrdinalIgnoreCase) ? "#FF00FF" : base.GetDefault(name);

    private readonly Dictionary<(ButtonGroupItem, string), SkinBitmap?> _overlays = [];
    private ButtonGroupItem? _hovered;
    private ButtonGroupItem? _pressed;

    public IEnumerable<ButtonGroupItem> Items => Children.OfType<ButtonGroupItem>();

    public SkinBitmap? BaseImage => Image("image");

    /// <summary>showBackground="false": the base image only sizes the group; just the button states are drawn.</summary>
    public bool ShowBackground => GetBool("showBackground", true);

    protected override IEnumerable<string> KnownProperties =>
        ["image", "hoverImage", "downImage", "disabledImage", "hoverDownImage", "mappingImage", "transparencyColor", "showBackground"];

    internal override void ApplyDefaultSize() => DefaultSizeFrom(BaseImage);

    public override bool IsInteractive => true;

    public override string? ToolTip => _hovered?.ToolTip;

    public ButtonGroupItem? ItemAt(double x, double y)
    {
        var map = View.Session.Images.Get(GetString("mappingImage"), null);
        if (map is null)
            return null;
        var rgb = map.GetRgbAt(x, y);
        if (rgb < 0)
            return null;
        return Items.FirstOrDefault(i => i.Visible && i.MappingColor == rgb);
    }

    public override bool HitTest(double x, double y) => base.HitTest(x, y) && ItemAt(x, y) is not null;

    /// <summary>The overlay to draw for an item in its current state, or null for the base image.</summary>
    public SkinBitmap? GetOverlay(ButtonGroupItem item)
    {
        if (!item.Visible || item.MappingColor is not { } color)
            return null;

        string? property;
        if (!item.Enabled)
            property = "disabledImage";
        else if (item.Down || (item == _pressed && item == _hovered))
            property = item == _hovered && HasProperty("hoverDownImage") ? "hoverDownImage" : "downImage";
        else if (item == _hovered)
            property = "hoverImage";
        else
            property = null;

        // Without a background the normal image still shows, but only inside each button's mapped area.
        if (property is not null && !ShowBackground && !HasProperty(property))
            property = null;
        if (property is null)
        {
            if (ShowBackground)
                return null;
            property = "image";
        }

        if (_overlays.TryGetValue((item, property), out var cached))
            return cached;

        var source = Image(property);
        var map = View.Session.Images.Get(GetString("mappingImage"), null);
        var overlay = source is null || map is null ? null : source.MaskedBy(map, color);
        _overlays[(item, property)] = overlay;
        return overlay;
    }

    public override void OnPointerMoved(double x, double y) => SetHovered(ItemAt(x, y));

    public override void OnPointerEnter()
    {
        base.OnPointerEnter();
    }

    public override void OnPointerLeave()
    {
        SetHovered(null);
        base.OnPointerLeave();
    }

    public override void OnPointerPressed(double x, double y)
    {
        base.OnPointerPressed(x, y);
        _pressed = ItemAt(x, y);
        if (_pressed is { Enabled: false })
            _pressed = null;
        _pressed?.FireEvent("onmousedown");
        View.Invalidate();
    }

    public override void OnPointerReleased(double x, double y, bool inside)
    {
        var pressed = _pressed;
        _pressed = null;
        base.OnPointerReleased(x, y, inside: false);
        if (pressed is not null && inside && ItemAt(x, y) == pressed && pressed.Enabled)
            pressed.Click();
        View.Invalidate();
    }

    private void SetHovered(ButtonGroupItem? item)
    {
        if (item == _hovered)
            return;
        _hovered?.FireEvent("onmouseout");
        _hovered = item;
        _hovered?.FireEvent("onmouseover");
        View.Invalidate();
    }
}

/// <summary>A button inside a BUTTONGROUP (BUTTONELEMENT, PLAYELEMENT, STOPELEMENT …). Drawn by its group.</summary>
public sealed class ButtonGroupItem : SkinElement, IPlayerSynced
{
    public ButtonGroupItem(SkinView view, SkinNode node, SkinElement? parent) : base(view, node, parent)
    {
        Action = Transport.FromTag(node.Tag);
    }

    public TransportAction Action { get; }

    public int? MappingColor => SkinValue.ParseColor(Get("mappingColor"));

    public bool Sticky => GetBool("sticky");

    public bool Down { get => GetBool("down"); set => Set("down", value); }

    public override bool IsVisual => false;

    protected override IEnumerable<string> KnownProperties => ["down", "sticky", "mappingColor"];

    public override string? ToolTip =>
        Down ? GetString("downToolTip") ?? GetString("upToolTip") ?? GetString("toolTip") : base.ToolTip;

    internal void Click()
    {
        if (Sticky)
            Down = !Down;

        if (Handlers.ContainsKey("onclick"))
            FireEvent("onclick");
        else
            Transport.Execute(Action, View.Session);
    }

    public void SyncWithPlayer()
    {
        if (Action == TransportAction.None)
            return;
        var player = View.Session.Player;
        if (!ExplicitAttributes.Contains("enabled"))
            Enabled = Transport.IsAvailable(Action, player);
        if (!ExplicitAttributes.Contains("down") && Transport.DownState(Action, player) is { } down)
            Down = down;
    }
}

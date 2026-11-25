using Pardeh.Core.Playback;
using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Model;

namespace Pardeh.Skinning.Runtime.Elements;

public enum SliderKind
{
    Custom,
    Seek,
    Volume,
    Balance,
}

/// <summary>SLIDER, CUSTOMSLIDER, SEEKSLIDER, VOLUMESLIDER and BALANCESLIDER.</summary>
public sealed class SliderElement : SkinElement, IPlayerSynced
{
    private bool _dragging;
    private double _dragValue;

    public SliderElement(SkinView view, SkinNode node, SkinElement? parent) : base(view, node, parent)
    {
        Kind = node.Tag switch
        {
            "SEEKSLIDER" => SliderKind.Seek,
            "VOLUMESLIDER" => SliderKind.Volume,
            "BALANCESLIDER" => SliderKind.Balance,
            _ => SliderKind.Custom,
        };
    }

    public SliderKind Kind { get; }

    public double Min => GetDouble("min", Kind == SliderKind.Balance ? -100 : 0);

    public double Max => GetDouble("max", 100);

    public double Value { get => GetDouble("value"); set => Set("value", value); }

    /// <summary>The value to draw: follows the pointer while dragging.</summary>
    public double DisplayValue => _dragging ? _dragValue : Value;

    public bool Vertical => string.Equals(GetString("direction"), "vertical", StringComparison.OrdinalIgnoreCase);

    /// <summary>Fixed end caps (px) when the track images are stretched to the slider; the thumb uses the whole length.</summary>
    public double BorderSize => GetDouble("borderSize");

    /// <summary>When false the value is only committed on release (seek bars).</summary>
    public bool Slide => GetBool("slide", true);

    public bool Tiled => GetBool("tiled");

    public SkinBitmap? TrackImage => (!Enabled ? Image("disabledImage") : null) ?? Image("backgroundImage");

    public SkinBitmap? ForegroundImage => Image("foregroundImage");

    public SkinBitmap? ThumbImage
    {
        get
        {
            var normal = Image("thumbImage");
            if (!Enabled)
                return Image("thumbDisabledImage") ?? normal;
            if (_dragging)
                return Image("thumbDownImage") ?? Image("thumbHoverImage") ?? normal;
            if (IsHovered)
                return Image("thumbHoverImage") ?? normal;
            return normal;
        }
    }

    protected override IEnumerable<string> KnownProperties =>
        ["min", "max", "value", "direction", "borderSize", "slide", "tiled", "backgroundImage", "foregroundImage",
         "thumbImage", "thumbHoverImage", "thumbDownImage", "thumbDisabledImage", "disabledImage", "foregroundProgress",
         "useForegroundProgress", "transparencyColor"];

    internal override void ApplyDefaultSize() => DefaultSizeFrom(Image("backgroundImage"));

    public override bool IsInteractive => true;

    /// <summary>Thumb rectangle in local coordinates.</summary>
    public (double X, double Y, double W, double H) ThumbRect
    {
        get
        {
            var thumb = Image("thumbImage");
            double tw = thumb?.LogicalWidth ?? 0, th = thumb?.LogicalHeight ?? 0;
            var fraction = Fraction(DisplayValue);
            if (Vertical)
            {
                var travel = Math.Max(0, Height - th);
                var y = (1 - fraction) * travel;
                return ((Width - tw) / 2, Math.Round(y), tw, th);
            }
            else
            {
                var travel = Math.Max(0, Width - tw);
                var x = fraction * travel;
                return (Math.Round(x), (Height - th) / 2, tw, th);
            }
        }
    }

    public double Fraction(double value)
    {
        var range = Max - Min;
        return range <= 0 ? 0 : Math.Clamp((value - Min) / range, 0, 1);
    }

    private double ValueAt(double x, double y)
    {
        var thumb = Image("thumbImage");
        double fraction;
        if (Vertical)
        {
            var th = thumb?.LogicalHeight ?? 0;
            var travel = Math.Max(1, Height - th);
            fraction = 1 - (y - th / 2) / travel;
        }
        else
        {
            var tw = thumb?.LogicalWidth ?? 0;
            var travel = Math.Max(1, Width - tw);
            fraction = (x - tw / 2) / travel;
        }

        return Min + Math.Clamp(fraction, 0, 1) * (Max - Min);
    }

    public override void OnPointerPressed(double x, double y)
    {
        base.OnPointerPressed(x, y);
        if (!Enabled)
            return;
        _dragging = true;
        _dragValue = ValueAt(x, y);
        FireEvent("ondragbegin");
        if (Slide)
            Commit(_dragValue);
    }

    public override void OnPointerMoved(double x, double y)
    {
        if (!_dragging)
            return;
        _dragValue = ValueAt(x, y);
        if (Slide)
            Commit(_dragValue);
        View.Invalidate();
    }

    public override void OnPointerReleased(double x, double y, bool inside)
    {
        if (_dragging)
        {
            _dragging = false;
            Commit(_dragValue);
            FireEvent("ondragend");
        }

        base.OnPointerReleased(x, y, inside: false);
    }

    public override void OnWheel(double delta)
    {
        if (!Enabled || Kind == SliderKind.Seek)
            return;
        var step = (Max - Min) / 20;
        Commit(Math.Clamp(Value + Math.Sign(delta) * step, Min, Max));
    }

    private void Commit(double value)
    {
        var player = View.Session.Player;
        switch (Kind)
        {
            case SliderKind.Seek:
                player.CurrentPosition = value;
                break;
            case SliderKind.Volume:
                player.Volume = value;
                if (player.Mute)
                    player.Mute = false;
                break;
            case SliderKind.Balance:
                player.Balance = value;
                break;
        }

        Value = value; // fires value_onchange
        View.Invalidate();
    }

    public void SyncWithPlayer()
    {
        if (_dragging)
            return;
        var player = View.Session.Player;
        switch (Kind)
        {
            case SliderKind.Seek:
                SetInitialOrSet("max", Math.Max(0, player.Duration));
                SetInitialOrSet("value", player.CurrentPosition);
                if (!ExplicitAttributes.Contains("enabled"))
                    Enabled = player.OpenState == OpenState.MediaOpen && player.Duration > 0;
                break;
            case SliderKind.Volume:
                SetInitialOrSet("value", player.Volume);
                break;
            case SliderKind.Balance:
                SetInitialOrSet("value", player.Balance);
                break;
        }
    }

    // Player-driven updates shouldn't fire value_onchange (that's for user changes).
    private void SetInitialOrSet(string name, double value)
    {
        if (!SkinValue.AreEqual(Get(name), value))
            Set(name, value, fireChange: false);
    }
}

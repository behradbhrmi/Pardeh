using Pardeh.Skinning.Model;

namespace Pardeh.Skinning.Runtime.Elements;

/// <summary>
/// &lt;EQBARS&gt; (alias &lt;LEVELBARS&gt;): a small live equaliser glyph, drawn as vectors by the host from the player's real spectrum.
/// An opt-in extension element: WMP skins never use it. The bars move while the player plays and settle to a low, flat resting
/// position when it is paused or stopped. It never takes a click (the button under it does).
/// Attributes: <c>bars</c> (3 by default; 3 – 16), <c>barStyle</c> (<c>solid</c> or <c>segments</c>), <c>segments</c> (rows of a segmented
/// bar, 5 by default), <c>segmentHeight</c> / <c>segmentGap</c>, <c>gap</c> (between bars), <c>colors</c> ("#low;#high;#tip", the bar
/// colour from its foot to its top and the top segment), <c>rest</c> (the resting level, 0 – 1, 0.18 by default),
/// <c>active</c> ("auto" follows the player, "false" holds the bars at rest).
/// </summary>
public sealed class EqBarsElement(SkinView view, SkinNode node, SkinElement? parent) : SkinElement(view, node, parent)
{
    public const int DefaultBars = 3;

    public override bool HitTest(double x, double y) => false;

    protected override IEnumerable<string> KnownProperties =>
        ["bars", "barStyle", "segments", "segmentHeight", "segmentGap", "gap", "colors", "rest", "active"];

    /// <summary>Number of bars (3 – 16).</summary>
    public int Bars => Math.Clamp((int)GetDouble("bars", DefaultBars), 1, 16);

    public bool Segmented => string.Equals(GetString("barStyle"), "segments", StringComparison.OrdinalIgnoreCase);

    public int Segments => Math.Clamp((int)GetDouble("segments", 5), 1, 24);

    /// <summary>The resting level (0 – 1) the bars sink to when nothing plays.</summary>
    public double Rest => Math.Clamp(GetDouble("rest", 0.18), 0, 1);

    /// <summary>False when the skin holds the bars at rest whatever the player does.</summary>
    public bool Active
    {
        get
        {
            var value = GetString("active");
            return string.IsNullOrWhiteSpace(value) || value.Equals("auto", StringComparison.OrdinalIgnoreCase) || SkinValue.ToBool(Get("active"), true);
        }
    }
}

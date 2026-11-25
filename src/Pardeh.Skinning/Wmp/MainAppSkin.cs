using Pardeh.Skinning.Model;

namespace Pardeh.Skinning.Wmp;

/// <summary>
/// What the classic player did natively around its full-mode skin (MAINAPPSKIN.WSZ), expressed as skin changes.
/// </summary>
public static class MainAppSkin
{
    /// <summary>
    /// The window-frame strips along the top edge (the menus were drawn on them natively) and the bottom edge; they
    /// make the rectangular frame of the "menu bar shown" window and are see-through otherwise.
    /// </summary>
    private static readonly string[] MenuBarStrips =
    [
        "res://wmploc/#1993", "res://wmploc/#1997", "res://wmploc/#1992", // top
        "res://wmploc/#1988", "res://wmploc/#2060", "res://wmploc/#1998", "res://wmploc/#1987", // bottom
    ];

    /// <summary>
    /// The menu bar strips only show while the menu bar does (app.titleBarVisible); with it hidden the window's top
    /// edge is the curved frame alone, as in the player's "hide menu bar" mode.
    /// </summary>
    public static SkinNode Adapt(SkinNode theme)
    {
        foreach (var node in theme.Descendants())
        {
            if (node.Tag != "BUTTON" || !MenuBarStrips.Contains(node.GetAttribute("image"), StringComparer.OrdinalIgnoreCase))
                continue;
            // The long strip ends 46 px short of the top-right corner piece ("width - left - 298"), yet the 2003
            // screenshots show one continuous band up to the curve: run it to the corner (at width - 252).
            if (node.GetAttribute("image") == "res://wmploc/#1997")
                node.SetAttribute("width", "JScriptLive:svEntireApp.width - left - 252");

            var visible = node.GetAttribute("visible");
            node.SetAttribute("visible", visible is not null && visible.StartsWith("wmpprop:", StringComparison.OrdinalIgnoreCase)
                ? "wmpprop:app.titleBarVisible && " + visible["wmpprop:".Length..]
                : "wmpprop:app.titleBarVisible");
        }

        return theme;
    }
}

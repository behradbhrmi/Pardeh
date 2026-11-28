using Pardeh.Skinning.Runtime;
using Pardeh.Skinning.Runtime.Elements;

namespace Pardeh.UI.FullMode;

/// <summary>
/// The playlist column on the right of Pardeh's own PC looks, at a width the user chose. A look takes part by naming the
/// column "svList" and having a SetListWidth(width) function in its script. Skins from files are left as they are.
/// </summary>
internal static class SideList
{
    /// <summary>How much of the pane the picture beside the list keeps.</summary>
    private const double PictureKeeps = 340;

    /// <summary>The column of a look that takes part, or null. Ask once for a view: it runs the look's script.</summary>
    public static SkinElement? Find(SkinView view) =>
        view.FindById("svList") is { } list && view.Script.Evaluate("typeof SetListWidth")?.ToString() == "function" ? list : null;

    /// <summary>Whether the column is on screen: it and everything around it.</summary>
    public static bool IsShown(SkinElement list)
    {
        for (SkinElement? e = list; e is not null; e = e.Parent)
        {
            if (!e.Visible)
                return false;
        }

        return true;
    }

    /// <summary>The widths the column may have now. It starts out <paramref name="usual"/> wide.</summary>
    public static (double Min, double Max) Limits(SkinElement list, double usual)
    {
        var min = Math.Round(usual * 0.85);
        return (min, Math.Max(min, (list.Parent?.Width ?? usual + PictureKeeps) - PictureKeeps));
    }

    /// <summary>Gives the column a width, within its limits.</summary>
    public static void Set(SkinView view, SkinElement list, double width, double usual)
    {
        var (min, max) = Limits(list, usual);
        width = Math.Round(Math.Clamp(width, min, max));
        if (Math.Abs(list.Width - width) > 0.5)
            view.Script.Execute(FormattableString.Invariant($"SetListWidth({width});"), "list");
    }
}

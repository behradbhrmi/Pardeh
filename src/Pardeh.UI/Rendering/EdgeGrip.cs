using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Pardeh.UI.Rendering;

/// <summary>
/// The handle on a side panel's inner edge: drag it for another width, double-click it for the usual one. It reports
/// widths and leaves it to its owner to apply them and to keep them within limits.
/// </summary>
internal sealed class EdgeGrip : Border
{
    public const double Thickness = 7;

    /// <param name="leftEdge">The panel's left edge, so that dragging left makes it wider; else its right edge.</param>
    /// <param name="line">What marks the edge while the pointer is on it.</param>
    /// <param name="reference">Something that stays still while the edge moves: the drag is measured against it.</param>
    /// <param name="shownWidth">The width as it is, after the owner's limits.</param>
    /// <param name="resize">The width asked for (null: the usual one), and whether it is worth keeping.</param>
    public EdgeGrip(bool leftEdge, IBrush line, Func<Visual?> reference, Func<double> shownWidth, Action<double?, bool> resize)
    {
        var mark = new Border { Width = 2, Background = line, IsVisible = false, HorizontalAlignment = leftEdge ? HorizontalAlignment.Left : HorizontalAlignment.Right };
        Width = Thickness;
        Background = Brushes.Transparent;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        Child = mark;
        ToolTip.SetTip(this, "Drag to resize; double-click for the usual width");

        double? from = null;
        double start = 0;
        var moved = false;

        PointerEntered += (_, _) => mark.IsVisible = true;
        PointerExited += (_, _) => mark.IsVisible = from is not null;
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;
            e.Handled = true;
            if (e.ClickCount == 2)
            {
                from = null;
                resize(null, true);
                return;
            }

            from = e.GetPosition(reference()).X;
            start = shownWidth();
            moved = false;
            e.Pointer.Capture(this);
        };
        PointerMoved += (_, e) =>
        {
            if (from is not { } pressed)
                return;
            var travel = e.GetPosition(reference()).X - pressed;
            if (!moved && Math.Abs(travel) < 2)
                return;
            moved = true;
            resize(Math.Max(1, start + (leftEdge ? -travel : travel)), false);
        };
        PointerCaptureLost += (_, _) =>
        {
            // The width as shown, which the limits may have held back from where the pointer went.
            if (from is not null && moved)
                resize(shownWidth(), true);
            from = null;
            mark.IsVisible = IsPointerOver;
        };
        PointerReleased += (_, e) => e.Pointer.Capture(null);
    }
}

using Pardeh.Core.Input;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Pardeh.Skinning.Runtime;

namespace Pardeh.UI;

/// <summary>How full mode's window is dressed (desktop).</summary>
internal enum WindowStyle
{
    /// <summary>Pardeh's caption and menu bar around the skin, which shows its bars.</summary>
    TitleBar,

    /// <summary>The skin with its bars and rounded corners, without a caption or menu bar.</summary>
    Bars,

    /// <summary>The frameless shaped skin (its curved edge is the window's edge).</summary>
    Compact,
}

internal sealed partial class PlayerView
{
    /// <summary>Corner radius (DIPs) of the bars-only window, as Windows 11 rounds its own windows, a little rounder.</summary>
    private const double BarsCornerRadius = 10;

    public static readonly (WindowStyle Style, string Name)[] WindowStyles =
    [
        (WindowStyle.TitleBar, "Title Bar and Menu Bar"),
        (WindowStyle.Bars, "Skin Bars, No Title Bar"),
        (WindowStyle.Compact, "Compact (No Bars)"),
    ];

    private bool _barsOnly;
    private WindowStyle _styleBeforeBars = WindowStyle.Compact;

    /// <summary>The state the full mode window is in (the saved one while a .wmz skin shows).</summary>
    internal WindowStyle CurrentWindowStyle =>
        _framed ? WindowStyle.TitleBar : _barsOnly ? WindowStyle.Bars : WindowStyle.Compact;

    /// <summary>Dresses the window for the loaded skin: its bars (the skin's titleBarVisible) and whether the caption goes with them.</summary>
    private void ApplyWindowStyle()
    {
        var bars = IsFullMode && _session is not null && SkinValue.ToBool(_session.App.GetMember("titleBarVisible"));
        var desktop = !OperatingSystem.IsAndroid();
        var barsOnly = bars && desktop && AppSettings.Current.BarsWithoutTitleBar;
        if (barsOnly != _barsOnly)
        {
            _barsOnly = barsOnly;
            _shapedVersion = -1;
            if (barsOnly)
                Platform.ClearWindowShape(Top); // the whole rectangle belongs to the window; the corners are clipped below
        }

        SetFramed(bars && !barsOnly);
        UpdateCorners();
        UpdateBarsClip();
    }

    /// <summary>Rounds the bars-only window by clipping its content (the window itself is transparent).</summary>
    private void UpdateBarsClip()
    {
        var round = _barsOnly && !_fullScreen && _restoreBounds is null && AppSettings.Current.RoundedCorners
            && _session?.CurrentView is { };
        if (!round)
        {
            Clip = null;
            return;
        }

        var view = _session!.CurrentView!;
        var size = new Size(view.Root.Width * LayoutScale, view.Root.Height * LayoutScale);
        if (Clip is RectangleGeometry current && current.Rect.Size == size)
            return;
        Clip = new RectangleGeometry(new Rect(size)) { RadiusX = BarsCornerRadius, RadiusY = BarsCornerRadius };
    }

    internal void SetWindowStyle(WindowStyle style)
    {
        if (OperatingSystem.IsAndroid())
            return;
        if (!IsFullMode)
            LoadFullMode();
        if (_session is null)
            return;

        if (style == WindowStyle.Bars && CurrentWindowStyle != WindowStyle.Bars)
            _styleBeforeBars = CurrentWindowStyle;
        if (style != WindowStyle.Compact)
        {
            AppSettings.Current.BarsWithoutTitleBar = style == WindowStyle.Bars;
            AppSettings.Current.Save();
        }

        _session.App.SetMember("titleBarVisible", style != WindowStyle.Compact);
        Dispatcher.UIThread.Post(() =>
        {
            // Title bar and bars differ only on Pardeh's side, which the skin's property change does not report.
            ApplyWindowStyle();
            Log($"Window style: {CurrentWindowStyle}");
        });
    }

    /// <summary>Ctrl+Shift+M: the bars-only window, and back to the state before it.</summary>
    internal void ToggleBarsStyle() =>
        SetWindowStyle(CurrentWindowStyle == WindowStyle.Bars ? _styleBeforeBars : WindowStyle.Bars);

    internal void NextWindowStyle() =>
        SetWindowStyle((WindowStyle)(((int)CurrentWindowStyle + 1) % WindowStyles.Length));

    private Avalonia.Controls.MenuItem WindowStyleMenu() => new()
    {
        Header = "Window Style",
        ItemsSource = WindowStyles.Select(entry => (object)Item(
            entry.Name,
            () => SetWindowStyle(entry.Style),
            entry.Style switch { WindowStyle.TitleBar => KeyCmd.TitleBar, WindowStyle.Bars => KeyCmd.BarsStyle, _ => null },
            check: IsFullMode && CurrentWindowStyle == entry.Style)).ToArray(),
    };

    // ---- Moving the window ------------------------------------------------------------------------

    private PointerPressedEventArgs? _pendingMove;
    private Point _pendingMoveFrom;

    /// <summary>
    /// A press on the window's own surface. A normal window starts moving at once; a maximized one waits for the pointer to
    /// travel, so a plain click does not restore it.
    /// </summary>
    private void OnSurfacePressed(PointerPressedEventArgs e)
    {
        if (Shell is null)
            return;
        if (_restoreBounds is null)
        {
            BeginWindowMove(e);
            return;
        }

        MoveWindowOnDrag(e);
    }

    /// <summary>Moves the window once the pointer travels with the button held; a plain click moves nothing.</summary>
    private void MoveWindowOnDrag(PointerPressedEventArgs e)
    {
        if (Shell is null)
            return;
        _pendingMove = e;
        _pendingMoveFrom = e.GetPosition(this);
    }

    private void OnSurfaceMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingMove is not { } press)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pendingMove = null;
            return;
        }

        var travel = e.GetPosition(this) - _pendingMoveFrom;
        if (Math.Abs(travel.X) < 5 && Math.Abs(travel.Y) < 5)
            return;
        _pendingMove = null;
        BeginWindowMove(press);
    }
}

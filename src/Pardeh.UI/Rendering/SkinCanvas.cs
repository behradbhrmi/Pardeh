using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Pardeh.Skinning.Runtime;
using Pardeh.Skinning.Runtime.Elements;

namespace Pardeh.UI.Rendering;

/// <summary>Displays a skin view and routes mouse input to its elements.</summary>
internal sealed class SkinCanvas : Control
{
    private readonly SkinRenderer _renderer = new();
    private SkinElement? _hovered;
    private SkinElement? _captured;

    public SkinView? View { get; set; }

    /// <summary>Where the last frame showed a Settings page (skin pixels), for the window's settings overlay.</summary>
    public Rect? SettingsHost => FullScreen ? null : _renderer.SettingsHost;

    public Rect? LibraryHost => FullScreen ? null : _renderer.LibraryHost;

    /// <summary>The press that's currently in progress (needed to start window moves/resizes).</summary>
    public PointerPressedEventArgs? CurrentPress { get; private set; }

    /// <summary>Raised for a left press on the skin's background (drag the window).</summary>
    public event EventHandler<PointerPressedEventArgs>? BackgroundPressed;

    /// <summary>Raised for a right click anywhere on the skin.</summary>
    public event EventHandler<PointerPressedEventArgs>? ContextRequestedOnSkin;

    /// <summary>Full screen: draw only the video / visualization, filling the control.</summary>
    public bool FullScreen { get; set; }

    /// <summary>The control bar shown over full-screen video.</summary>
    public FullScreenControls Controls { get; } = new();

    /// <summary>A double-click in full screen (the window leaves full screen).</summary>
    public event EventHandler? FullScreenExitRequested;

    private static bool _firstRender;

    public override void Render(DrawingContext context)
    {
        if (!_firstRender && View is not null)
        {
            _firstRender = true;
            StartupMarks.Mark("first skin frame drawn");
        }

        if (View is not { } view)
            return;
        if (FullScreen)
        {
            _renderer.RenderFullScreen(context, view, Bounds.Size);
            Controls.Draw(context, Bounds.Size, view.Session.Player);
        }
        else
            _renderer.Render(context, view);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        View is null ? default
        : FullScreen ? availableSize
        : new Size(View.Root.Width, View.Root.Height);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (View is not null && FullScreen)
        {
            Controls.Activity();
            Controls.Drag(e.GetPosition(this), View.Session.Player);
            return;
        }

        if (View is null)
            return;
        UpdateEvent(e, 0);

        var p = e.GetPosition(this);
        if (TouchMoved(e, p))
            return;
        if (_captured is not null)
        {
            var (x, y) = Local(_captured, p);
            _captured.OnPointerMoved(x, y);
            return;
        }

        var target = Interactive(View.HitTest(p.X, p.Y));
        SetHovered(target);
        if (target is not null)
        {
            var (x, y) = Local(target, p);
            target.OnPointerMoved(x, y);
        }

        UpdateToolTip(target);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (View is null)
            return;
        if (FullScreen)
        {
            // The control bar takes its own clicks; a double-click on the picture leaves full screen.
            var at = e.GetPosition(this);
            if (Controls.Press(at, View.Session.Player))
                e.Pointer.Capture(this);
            else if (e.ClickCount == 2)
                FullScreenExitRequested?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            return;
        }

        var point = e.GetCurrentPoint(this);
        var p = point.Position;
        UpdateEvent(e, point.Properties.IsRightButtonPressed ? 2 : point.Properties.IsMiddleButtonPressed ? 4 : 1);
        var hit = View.HitTest(p.X, p.Y);

        if (point.Properties.IsRightButtonPressed)
        {
            // Elements with their own menu (library tracks) first, then the app menu.
            if (Interactive(hit) is { } menuTarget)
            {
                var (mx, my) = Local(menuTarget, p);
                if (menuTarget.OnContextMenu(mx, my))
                    return;
            }

            ContextRequestedOnSkin?.Invoke(this, e);
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
            return;

        var target = Interactive(hit);
        BeginTouch(e, p, target);
        if (target is null)
        {
            // Full mode's own bezel (drawn without a hit-testable element, the title bar hidden) must drag too, so a press that lands on
            // nothing interactive moves the window whether or not an element sits there.
            if (hit is not null || e.Pointer.Type == PointerType.Mouse)
                BackgroundPressed?.Invoke(this, e);
            return;
        }

        if (!OperatingSystem.IsAndroid() && target.MovesWindow)
        {
            PressOnWindowSurface(e, p, target);
            return;
        }

        PressLog?.Invoke($"press at ({p.X:0},{p.Y:0}) goes to {Describe(target)}");
        CurrentPress = e;
        try
        {
            SetHovered(target);
            var (x, y) = Local(target, p);
            if (e.ClickCount >= 2)
            {
                target.OnDoubleClick(x, y);
            }
            else
            {
                _captured = target;
                e.Pointer.Capture(this);
                target.OnPointerPressed(x, y);
            }
        }
        finally
        {
            CurrentPress = null;
        }

        InvalidateVisual();
    }

    /// <summary>
    /// A left press on the window itself (the view or a decorative part of its frame): the view's own handlers still run,
    /// and the press moves the window. A double-click goes to the view (the original full mode maximizes on its top bar).
    /// </summary>
    private void PressOnWindowSurface(PointerPressedEventArgs e, Point p, SkinElement target)
    {
        var root = target.View.Root;
        var doubleClick = e.ClickCount >= 2;
        CurrentPress = e;
        try
        {
            var (x, y) = Local(root, p);
            if (doubleClick)
            {
                root.OnDoubleClick(x, y);
            }
            else if (root.IsInteractive)
            {
                root.OnPointerPressed(x, y);
                root.OnPointerReleased(x, y, inside: false);
            }
        }
        finally
        {
            CurrentPress = null;
        }

        if (!doubleClick)
            BackgroundPressed?.Invoke(this, e);
        InvalidateVisual();
    }

    /// <summary>Set for diagnostics (PARDEH_DIAG=1): reports presses that a control took instead of moving the window.</summary>
    public Action<string>? PressLog { get; set; }

    public static string Describe(SkinElement element) => $"{element.Tag}#{element.Id} {element.Width:0}x{element.Height:0}";

    /// <summary>What a press at a point lands on, for the window-move log line.</summary>
    public string DescribeAt(Point p) => View?.HitTest(p.X, p.Y) is { } hit ? Describe(hit) : "nothing";

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        Controls.Release();
        FinishSwipe();
        var flung = _scrolling && e.Pointer.Type == PointerType.Touch;
        EndTouch();
        if (flung)
            StartFling();
        var captured = _captured;
        _captured = null;
        e.Pointer.Capture(null);
        if (captured is null || View is null)
            return;

        var p = e.GetPosition(this);
        var (x, y) = Local(captured, p);
        var inside = Interactive(View.HitTest(p.X, p.Y)) == captured;
        captured.OnPointerReleased(x, y, inside);

        var now = Interactive(View.HitTest(p.X, p.Y));
        SetHovered(now);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_captured is null)
            SetHovered(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (View is null)
            return;
        var p = e.GetPosition(this);
        Interactive(View.HitTest(p.X, p.Y))?.OnWheel(e.Delta.Y);
        InvalidateVisual();
    }

    /// <summary>Copies pointer state into the skin's <c>event</c> object before handlers run.</summary>
    private void UpdateEvent(PointerEventArgs e, int button)
    {
        if (View is null)
            return;
        var p = e.GetPosition(this);
        Fill(View, 0, 0);

        // Embedded panes (Now Playing …) have their own event object, in their own coordinates.
        void Fill(SkinView view, double originX, double originY)
        {
            var ev = view.Event;
            ev.X = Math.Round(p.X - originX);
            ev.Y = Math.Round(p.Y - originY);
            if (button != 0)
                ev.Button = button;
            ev.Shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            ev.Ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            ev.Alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            ev.ReturnValue = true;
            foreach (var host in view.Elements.OfType<HostAreaElement>())
            {
                if (host.Embedded is { } pane)
                {
                    var (x, y) = host.AbsolutePosition;
                    Fill(pane, x, y);
                }
            }
        }
    }

    // ---- Touch: long-press = right-click, drag over a list = scroll ---------------------------------

    private DispatcherTimer? _hold;
    private PointerPressedEventArgs? _touch;
    private Point _touchStart;
    private SkinElement? _touchTarget;
    private bool _scrolling;
    private double _scrollRemainder;

    private void BeginTouch(PointerPressedEventArgs e, Point p, SkinElement? target)
    {
        StopFling();
        EndTouch();
        if (e.Pointer.Type != PointerType.Touch)
            return;
        _touch = e;
        _touchStart = p;
        _touchTarget = target;
        _scrolling = false;
        _scrollRemainder = 0;
        _swiping = false;
        // A press on the stage (video, visualization, cover) can become a horizontal swipe that seeks.
        var hitElement = View?.HitTest(p.X, p.Y);
        // A visualization that asks for it (vizSwipe="true", Pardeh Glass) takes the swipe to change its style instead of seeking.
        _vizSwipe = hitElement is EffectsElement { SwipesStyle: true };
        _vizSwiped = false;
        _swipeable = !_vizSwipe && hitElement is { } hit && hit.Id is "svStage" or "vid" or "vis" or "cover" or "stage";
        _hold = new DispatcherTimer(TimeSpan.FromMilliseconds(550), DispatcherPriority.Input, (_, _) => OnLongPress());
        _hold.Start();
    }

    private bool _swipeable, _swiping, _vizSwipe, _vizSwiped;
    private double _swipeFrom, _swipeTo;

    /// <summary>Horizontal swipe on the stage: seeks (applied when the finger lifts). True while swiping.</summary>
    private bool SwipeSeek(Point p)
    {
        if (View?.Session.Player is not { } player || player.Duration <= 0)
            return false;
        var dx = p.X - _touchStart.X;
        if (!_swiping)
        {
            if (Math.Abs(dx) < 16 || Math.Abs(dx) < Math.Abs(p.Y - _touchStart.Y) * 1.5)
                return false;
            _swiping = true;
            _hold?.Stop();
            _swipeFrom = player.CurrentPosition;
            _captured?.OnPointerReleased(-1, -1, inside: false);
            _captured = null;
        }

        // A full-width swipe covers up to three minutes (the whole track when it is shorter).
        var width = Math.Max(1, View.Root.Width);
        _swipeTo = Math.Clamp(_swipeFrom + dx / width * Math.Min(player.Duration, 180), 0, player.Duration);
        return true;
    }

    /// <summary>
    /// True when a touch starting at <paramref name="p"/> (skin pixels) already owns horizontal drags: a slider, or
    /// the stage / cover while something is playing (a swipe there seeks). Page swipes leave those alone.
    /// </summary>
    public bool OwnsHorizontalDrag(Point p)
    {
        if (View is null)
            return false;
        var playing = View.Session.Player.Duration > 0;
        for (var element = View.HitTest(p.X, p.Y); element is not null; element = element.Parent)
        {
            if (element is SliderElement or LyricsElement or EffectsElement { SwipesStyle: true })
                return true;
            if (playing && element.Id is { Length: > 0 } id
                && (id.Contains("cover", StringComparison.OrdinalIgnoreCase) || id is "svStage" or "vid" or "vis" or "stage"))
                return true;
        }

        return false;
    }

    private void FinishSwipe()
    {
        if (_swiping && View?.Session.Player is { } player)
            player.CurrentPosition = _swipeTo;
        _swiping = false;
        _swipeable = false;
    }

    private void EndTouch()
    {
        _hold?.Stop();
        _hold = null;
        _touch = null;
        _scrolling = false;
    }

    private void OnLongPress()
    {
        var press = _touch;
        var start = _touchStart;
        var target = _touchTarget;
        EndTouch();
        if (press is null || View is null || target is SliderElement)
            return;
        CancelInteraction(press);
        var hit = View.HitTest(start.X, start.Y);
        if (Interactive(hit) is { } menuTarget)
        {
            var (mx, my) = Local(menuTarget, start);
            if (menuTarget.OnContextMenu(mx, my))
                return;
        }

        ContextRequestedOnSkin?.Invoke(this, press);
    }

    /// <summary>Touch drags: moving cancels the long-press; over a list it scrolls it. True when handled.</summary>
    private bool TouchMoved(PointerEventArgs e, Point p)
    {
        if (_touch is null || e.Pointer.Type != PointerType.Touch)
            return false;
        if (_swipeable && !_scrolling && SwipeSeek(p))
            return true;
        if (_vizSwipe)
        {
            // A horizontal swipe on the visualization: the next (left) or previous (right) style, once per touch.
            var vx = p.X - _touchStart.X;
            if (!_vizSwiped && Math.Abs(vx) > 40 && Math.Abs(vx) > Math.Abs(p.Y - _touchStart.Y) * 1.5)
            {
                _vizSwiped = true;
                _hold?.Stop();
                _captured?.OnPointerReleased(-1, -1, inside: false);
                _captured = null;
                View?.Session.Host.RunCommand("vizstep", [vx < 0 ? 1.0 : -1.0]);
            }

            if (_vizSwiped)
                return true;
        }
        var dy = p.Y - _touchStart.Y;
        if (!_scrolling && (Math.Abs(dy) > 10 || Math.Abs(p.X - _touchStart.X) > 10))
        {
            _hold?.Stop();
            if (_touchTarget is PlaylistElement or LibraryElement or LyricsElement or Pardeh.Skinning.Runtime.Panes.ListPaneElement && Math.Abs(dy) > Math.Abs(p.X - _touchStart.X))
            {
                // Hand the press back (no click) and scroll the list with the finger.
                _captured?.OnPointerReleased(-1, -1, inside: false);
                _captured = null;
                _scrolling = true;
                _touchStart = new Point(_touchStart.X, p.Y);
                _scrollRemainder = 0;
                _velocity = 0;
                _lastMoveTicks = 0;
            }
        }

        if (!_scrolling)
            return false;
        var moved = p.Y - _touchStart.Y;
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_lastMoveTicks != 0)
        {
            var seconds = (now - _lastMoveTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
            if (seconds > 0.001)
                _velocity = _velocity * 0.6 + moved / seconds * 0.4; // smoothed finger speed, skin pixels per second
        }

        _lastMoveTicks = now;
        _scrollRemainder += moved;
        _touchStart = new Point(_touchStart.X, p.Y);
        ApplyTouchScroll();
        return true;
    }

    private void ApplyTouchScroll()
    {
        if (_touchTarget is IPixelScrollable smooth)
        {
            // The lyrics scroll by whole pixels with the finger (and a fling), not row by row.
            smooth.ScrollByPixels(-_scrollRemainder);
            _scrollRemainder = 0;
            InvalidateVisual();
            return;
        }

        var pixels = _touchTarget?.TouchScrollStep ?? 36;
        while (Math.Abs(_scrollRemainder) >= pixels)
        {
            var step = Math.Sign(_scrollRemainder);
            _touchTarget?.OnTouchScroll(step);
            _scrollRemainder -= step * pixels;
        }

        InvalidateVisual();
    }

    // Kinetic scrolling: a quick flick keeps the list moving after the finger lifts, slowing down by friction.
    private DispatcherTimer? _fling;
    private double _velocity;
    private long _lastMoveTicks;

    private void StopFling()
    {
        _fling?.Stop();
        _fling = null;
    }

    private void StartFling()
    {
        var velocity = _velocity;
        var idle = (System.Diagnostics.Stopwatch.GetTimestamp() - _lastMoveTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
        if (Math.Abs(velocity) < 250 || idle > 0.08 || _touchTarget is null)
            return; // a slow drag or a pause before lifting: no fling
        _scrollRemainder = 0;
        var last = System.Diagnostics.Stopwatch.GetTimestamp();
        _fling = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
        {
            var tick = System.Diagnostics.Stopwatch.GetTimestamp();
            var dt = Math.Min(0.05, (tick - last) / (double)System.Diagnostics.Stopwatch.Frequency);
            last = tick;
            _scrollRemainder += velocity * dt;
            velocity *= Math.Exp(-2.6 * dt); // half-life around a quarter of a second
            ApplyTouchScroll();
            if (Math.Abs(velocity) < 40)
                StopFling();
        });
        _fling.Start();
    }

    /// <summary>Releases the element under the mouse before the system takes over (window move/resize).</summary>
    public void CancelInteraction(PointerEventArgs e)
    {
        var captured = _captured;
        _captured = null;
        e.Pointer.Capture(null);
        captured?.OnPointerReleased(-1, -1, inside: false);
        SetHovered(null);
    }

    private static SkinElement? Interactive(SkinElement? element)
    {
        // A non-interactive child inside an interactive parent (e.g. text on a button) still clicks the parent.
        for (var e = element; e is not null; e = e.Parent)
        {
            if (e.IsInteractive)
                return e;
        }

        return null;
    }

    private void SetHovered(SkinElement? element)
    {
        if (element == _hovered)
            return;
        _hovered?.OnPointerLeave();
        _hovered = element;
        _hovered?.OnPointerEnter();
        Cursor = element is null || !OperatingSystem.IsAndroid() && element.MovesWindow
            ? Cursor.Default
            : string.Equals(element.Cursor, "sizenwse", StringComparison.OrdinalIgnoreCase)
                ? new Cursor(StandardCursorType.BottomRightCorner)
                : string.Equals(element.Cursor, "system", StringComparison.OrdinalIgnoreCase)
                    ? Cursor.Default
                    : new Cursor(StandardCursorType.Hand);
        InvalidateVisual();
    }

    private string? _toolTip;

    private void UpdateToolTip(SkinElement? element)
    {
        var text = element?.ToolTip;
        if (string.IsNullOrEmpty(text))
            text = null;
        if (text == _toolTip)
            return;
        _toolTip = text;
        ToolTip.SetTip(this, text);
    }

    private static (double X, double Y) Local(SkinElement element, Point p)
    {
        var (ax, ay) = element.AbsolutePosition;
        return (p.X - ax, p.Y - ay);
    }
}

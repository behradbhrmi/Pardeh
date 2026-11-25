using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Pardeh.UI.Rendering;

/// <summary>
/// The window frame full mode had with its menu bar shown: an XP Luna caption with minimize / maximize / close,
/// a menu row (File View Play Tools Help) and a 4-pixel border, drawn by Pardeh around the skin.
/// Appearance options swap the caption and the menu row for flat Windows 11 ones, independently.
/// </summary>
internal sealed class LunaFrame : Decorator
{
    public static readonly string[] Menus = ["File", "View", "Play", "Tools", "Help"];

    private static readonly IBrush CaptionBrush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#0997FF"), 0), new GradientStop(Color.Parse("#0053EE"), 0.12),
            new GradientStop(Color.Parse("#0050EE"), 0.5), new GradientStop(Color.Parse("#0066FF"), 0.88),
            new GradientStop(Color.Parse("#0047D8"), 1),
        },
    };

    private static readonly IBrush FrameBrush = new SolidColorBrush(Color.Parse("#0855DD"));
    private static readonly IBrush MenuBrush = new SolidColorBrush(Color.Parse("#ECE9D8"));
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.Parse("#316AC5"));

    // Windows 11 (light): a Mica-like flat surface, a thin grey outline, Segoe Fluent Icons caption glyphs.
    private static readonly IBrush ModernSurface = new SolidColorBrush(Color.Parse("#F3F3F3"));
    private static readonly IBrush ModernOutline = new SolidColorBrush(Color.Parse("#B4B4B4"));
    private static readonly IBrush ModernOutlineInactive = new SolidColorBrush(Color.Parse("#D2D2D2"));
    private static readonly IBrush ModernHairline = new SolidColorBrush(Color.FromArgb(0x14, 0, 0, 0));
    private static readonly IBrush ModernText = new SolidColorBrush(Color.Parse("#1A1A1A"));
    private static readonly IBrush ModernTextInactive = new SolidColorBrush(Color.Parse("#8A8A8A"));
    private static readonly IBrush ModernTextPressed = new SolidColorBrush(Color.Parse("#5F5F5F"));
    private static readonly IBrush ModernButtonHover = new SolidColorBrush(Color.FromArgb(0x0F, 0, 0, 0));
    private static readonly IBrush ModernButtonPressed = new SolidColorBrush(Color.FromArgb(0x0A, 0, 0, 0));
    private static readonly IBrush ModernCloseHover = new SolidColorBrush(Color.Parse("#C42B1C"));
    private static readonly IBrush ModernClosePressed = new SolidColorBrush(Color.Parse("#C83C31"));
    private static readonly Typeface GlyphFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly List<(string Menu, Rect Bounds)> _menuBounds = [];
    private int _hoverButton = -1;
    private int _hoverMenu = -1;
    private int _pressedButton = -1;
    private int _openMenu = -1;

    public string Title { get; set; } = "Pardeh";

    /// <summary>Shows the restore glyph on the modern maximize button.</summary>
    public bool Maximized { get; set; }

    private static bool ModernCaption => AppSettings.Current.ModernTitleBar;
    private static bool ModernMenu => AppSettings.Current.ModernMenuBar;

    public double Border => ModernCaption ? 1 : 4;
    public double CaptionHeight => ModernCaption ? 32 : 30;
    public double MenuHeight => ModernMenu ? 32 : 20;

    /// <summary>"minimize", "maximize", "close", "drag", "toggleMaximize" (double-click on the caption).</summary>
    public event Action<string, PointerPressedEventArgs?>? Command;

    /// <summary>A menu title was clicked: open that menu at the given point (in this control's coordinates).</summary>
    public event Action<string, Rect>? MenuRequested;

    public Thickness Chrome => new(Border, CaptionHeight + MenuHeight, Border, Border);

    /// <summary>The appearance options changed: new metrics and look.</summary>
    public void Refresh()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var chrome = Chrome;
        Child?.Measure(availableSize.Deflate(chrome));
        var child = Child?.DesiredSize ?? default;
        return new Size(child.Width + chrome.Left + chrome.Right, child.Height + chrome.Top + chrome.Bottom);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize).Deflate(Chrome));
        return finalSize;
    }

    private Rect ButtonRect(int index) // 0 minimize, 1 maximize, 2 close
    {
        if (ModernCaption)
        {
            const double width = 46;
            return new Rect(Bounds.Width - Border - (3 - index) * width, Border, width, CaptionHeight - Border);
        }

        const double size = 21;
        return new Rect(Bounds.Width - Border - 2 - (3 - index) * (size + 2), 5, size, size);
    }

    public override void Render(DrawingContext context)
    {
        if (ModernCaption)
            RenderModernCaption(context);
        else
            RenderClassicCaption(context);

        var menuRow = new Rect(Border, CaptionHeight, Bounds.Width - 2 * Border, MenuHeight);
        if (ModernMenu)
            RenderModernMenu(context, menuRow);
        else
            RenderClassicMenu(context, menuRow);
    }

    private static FormattedText Text(string text, Typeface face, double size, IBrush brush, double maxWidth = 0)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush);
        if (maxWidth > 0)
        {
            formatted.MaxTextWidth = Math.Max(1, maxWidth);
            formatted.MaxLineCount = 1;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }

        return formatted;
    }

    private void RenderClassicCaption(DrawingContext context)
    {
        var w = Bounds.Width;
        context.FillRectangle(FrameBrush, new Rect(0, 0, w, Bounds.Height));
        context.FillRectangle(CaptionBrush, new Rect(0, 0, w, CaptionHeight));

        // Title with the Luna drop shadow: Trebuchet MS bold, or Segoe UI with the modern font.
        Typeface face = UiTheme.ModernFont
            ? new Typeface(UiTheme.ModernDisplayFamily, FontStyle.Normal, FontWeight.SemiBold)
            : new Typeface(UiTheme.ClassicTitleFamily, FontStyle.Normal, FontWeight.Bold);
        var title = Text(Title, face, 13, Brushes.White, w - 120);
        var shadow = Text(Title, face, 13, new SolidColorBrush(Color.Parse("#0A1883")), w - 120);
        DrawIcon(context, new Rect(6, 7, 16, 16));
        context.DrawText(shadow, new Point(29, 8));
        context.DrawText(title, new Point(28, 7));

        for (var i = 0; i < 3; i++)
            DrawClassicButton(context, i, ButtonRect(i), i == _hoverButton);
    }

    private bool IsActive => (TopLevel.GetTopLevel(this) as Window)?.IsActive ?? true;

    private void RenderModernCaption(DrawingContext context)
    {
        var w = Bounds.Width;
        var active = IsActive;
        context.FillRectangle(active ? ModernOutline : ModernOutlineInactive, new Rect(0, 0, w, Bounds.Height));
        context.FillRectangle(ModernSurface, new Rect(Border, Border, w - 2 * Border, CaptionHeight - Border));

        // Windows 11 caption: 16-px icon at 12, 12-px Segoe UI title after a 12-px gap, greyed when inactive.
        var ink = active ? ModernText : ModernTextInactive;
        var captionMid = Border + (CaptionHeight - Border) / 2;
        using (context.PushOpacity(active ? 1 : 0.6))
            DrawIcon(context, new Rect(Border + 12, captionMid - 8, 16, 16));
        var face = UiTheme.ModernFont ? new Typeface(UiTheme.ModernFamily) : new Typeface(UiTheme.ClassicFamily);
        var title = Text(Title, face, 12, ink, w - 3 * 46 - 60);
        using (context.PushTextOptions(new TextOptions { TextRenderingMode = TextRenderingMode.Antialias }))
        {
            context.DrawText(title, new Point(Border + 40, captionMid - title.Height / 2));

            for (var i = 0; i < 3; i++)
            {
                var r = ButtonRect(i);
                var hover = i == _hoverButton;
                var pressed = hover && i == _pressedButton;
                if (hover)
                {
                    var back = i == 2 ? (pressed ? ModernClosePressed : ModernCloseHover) : (pressed ? ModernButtonPressed : ModernButtonHover);
                    context.FillRectangle(back, r);
                }

                var glyph = i switch { 0 => "\uE921", 1 => Maximized ? "\uE923" : "\uE922", _ => "\uE8BB" };
                var brush = hover && i == 2 ? Brushes.White : pressed ? ModernTextPressed : ink;
                var text = Text(glyph, GlyphFont, 10, brush);
                context.DrawText(text, new Point(Math.Round(r.X + (r.Width - text.Width) / 2), Math.Round(r.Y + (r.Height - text.Height) / 2)));
            }
        }
    }

    private void RenderClassicMenu(DrawingContext context, Rect menuRow)
    {
        context.FillRectangle(MenuBrush, menuRow);
        _menuBounds.Clear();
        var x = menuRow.X + 2;
        for (var i = 0; i < Menus.Length; i++)
        {
            var text = Text(Menus[i], UiTheme.Typeface(), 11, i == _hoverMenu ? Brushes.White : Brushes.Black);
            var item = new Rect(x, menuRow.Y + 1, text.Width + 12, MenuHeight - 2);
            if (i == _hoverMenu)
                context.FillRectangle(HighlightBrush, item);
            context.DrawText(text, new Point(item.X + 6, item.Y + (item.Height - text.Height) / 2));
            _menuBounds.Add((Menus[i], item));
            x = item.Right;
        }
    }

    private void RenderModernMenu(DrawingContext context, Rect menuRow)
    {
        // Windows 11 menu bar (as in Notepad): the caption's surface, 13-px text, rounded hover pills that stay
        // lit while their menu is open, and a hairline between the bar and the player.
        context.FillRectangle(ModernSurface, menuRow);
        context.FillRectangle(ModernHairline, new Rect(menuRow.X, menuRow.Bottom - 1, menuRow.Width, 1));
        _menuBounds.Clear();
        var x = menuRow.X + 4;
        var ink = IsActive ? ModernText : ModernTextInactive;
        using var antialias = context.PushTextOptions(new TextOptions { TextRenderingMode = TextRenderingMode.Antialias });
        for (var i = 0; i < Menus.Length; i++)
        {
            var text = Text(Menus[i], UiTheme.Typeface(), UiTheme.ModernFont ? 13 : 12, ink);
            var item = new Rect(x, menuRow.Y + 3, Math.Max(text.Width + 22, 40), MenuHeight - 7);
            if (i == _openMenu)
                context.DrawRectangle(ModernButtonPressed, null, item, 4, 4);
            else if (i == _hoverMenu)
                context.DrawRectangle(ModernButtonHover, null, item, 4, 4);
            context.DrawText(text, new Point(item.X + (item.Width - text.Width) / 2, item.Y + (item.Height - text.Height) / 2));
            _menuBounds.Add((Menus[i], item));
            x = item.Right + 2;
        }
    }

    private static void DrawIcon(DrawingContext context, Rect r)
    {
        // Pardeh's logo (or the personal picture in the data folder).
        FullMode.PardehLogo.Draw(context, r);
    }

    private static void DrawClassicButton(DrawingContext context, int index, Rect r, bool hover)
    {
        var close = index == 2;
        var (light, dark) = close
            ? (hover ? "#F59A85" : "#E8805F", hover ? "#D4421F" : "#C2381A")
            : (hover ? "#7EB4FF" : "#5C9BFF", hover ? "#2D74FF" : "#1C5DE7");
        var face = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse(light), 0), new GradientStop(Color.Parse(dark), 1) },
        };
        context.DrawRectangle(face, new Pen(Brushes.White, 1), r.Deflate(0.5), 3, 3);
        var glyph = new Pen(Brushes.White, 2);
        var c = r.Center;
        switch (index)
        {
            case 0:
                context.FillRectangle(Brushes.White, new Rect(r.X + 5, r.Bottom - 8, 7, 3));
                break;
            case 1:
                context.DrawRectangle(null, new Pen(Brushes.White, 1), new Rect(r.X + 5.5, r.Y + 5.5, 10, 10));
                context.FillRectangle(Brushes.White, new Rect(r.X + 5, r.Y + 5, 11, 3));
                break;
            default:
                context.DrawLine(glyph, new Point(c.X - 4, c.Y - 4), new Point(c.X + 4, c.Y + 4));
                context.DrawLine(glyph, new Point(c.X + 4, c.Y - 4), new Point(c.X - 4, c.Y + 4));
                break;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        var button = Enumerable.Range(0, 3).FirstOrDefault(i => ButtonRect(i).Contains(p), -1);
        var menu = _menuBounds.FindIndex(m => m.Bounds.Contains(p));

        // Like a real menu bar: with a menu open, moving onto another title switches to that menu.
        if (_openMenu >= 0 && menu >= 0 && menu != _openMenu)
            OpenMenu(menu);

        if (button != _hoverButton || menu != _hoverMenu)
        {
            _hoverButton = button;
            _hoverMenu = menu;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverButton = _hoverMenu = -1;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        var p = e.GetPosition(this);
        if (Child is { } child && child.Bounds.Contains(p))
            return;

        for (var i = 0; i < 3; i++)
        {
            if (ButtonRect(i).Contains(p))
            {
                // Like Windows: the button acts when released over it; dragging off cancels.
                _pressedButton = i;
                e.Pointer.Capture(this);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
        }

        if (_menuBounds.FindIndex(m => m.Bounds.Contains(p)) is >= 0 and var menu)
        {
            // Clicking the open menu's title closes it; any other title opens its menu instead.
            if (menu == _openMenu)
                MenuDismissRequested?.Invoke();
            else
                OpenMenu(menu);
            e.Handled = true;
            return;
        }

        if (p.Y < CaptionHeight)
        {
            Command?.Invoke(e.ClickCount == 2 ? "toggleMaximize" : "drag", e);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressedButton < 0)
            return;
        var button = _pressedButton;
        _pressedButton = -1;
        e.Pointer.Capture(null);
        InvalidateVisual();
        if (ButtonRect(button).Contains(e.GetPosition(this)))
            Command?.Invoke(button switch { 0 => "minimize", 1 => "maximize", _ => "close" }, null);
        e.Handled = true;
    }

    /// <summary>Clicking the open menu's title again: close it.</summary>
    public event Action? MenuDismissRequested;

    private void OpenMenu(int menu)
    {
        MenuRequested?.Invoke(_menuBounds[menu].Menu, _menuBounds[menu].Bounds); // closes any open one first
        _openMenu = menu;
        InvalidateVisual();
    }

    /// <summary>The menu opened from the menu bar closed: its title stops looking pressed.</summary>
    public void MenuClosed()
    {
        _openMenu = -1;
        InvalidateVisual();
    }
}

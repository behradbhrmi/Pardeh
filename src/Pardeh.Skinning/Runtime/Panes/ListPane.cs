using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Model;
using Pardeh.Skinning.Runtime.Elements;

namespace Pardeh.Skinning.Runtime.Panes;

/// <summary>A column of a list pane: header text and relative width.</summary>
public sealed record ListColumn(string Header, double Weight, bool AlignRight = false);

/// <summary>
/// What a task pane lists (skins, radio stations, items to copy) and what its buttons do. Drawn by
/// <see cref="ListPaneElement"/> in the classic player's pane style: a toolbar, a list, an optional details panel.
/// </summary>
public abstract class ListPaneModel
{
    protected ListPaneModel(SkinSession session) => Session = session;

    protected SkinSession Session { get; }

    public abstract string Title { get; }

    public abstract IReadOnlyList<ListColumn> Columns { get; }

    public abstract IReadOnlyList<string[]> Rows { get; }

    /// <summary>The row drawn in the "playing / in use" colour, or -1.</summary>
    public virtual int ActiveRow => -1;

    public virtual string Status => Rows.Count == 1 ? "1 item" : $"{Rows.Count} items";

    /// <summary>Toolbar buttons, left to right.</summary>
    public virtual IReadOnlyList<string> Buttons => [];

    /// <summary>When true, a details panel shows <see cref="Details"/> and <see cref="Preview"/> for the selected row.</summary>
    public virtual bool HasDetails => false;

    public virtual string Details(int row) => string.Empty;

    public virtual SkinBitmap? Preview(int row) => null;

    /// <summary>Double-click / Enter on a row.</summary>
    public virtual void Activate(int row)
    {
    }

    public virtual void Press(int button, int selectedRow)
    {
    }

    /// <summary>Raised when rows or status change (possibly from a background thread).</summary>
    public event Action? Changed;

    protected void OnChanged() => Changed?.Invoke();

    public virtual void Close()
    {
    }
}

/// <summary>LISTPANE kind="skins|appearance|radio|device": a Pardeh task pane for the full mode.</summary>
public sealed class ListPaneElement : SkinElement
{
    public const double ToolbarHeight = 30;
    public const double HeaderHeight = 18;
    public static double RowHeight { get; set; } = 16;
    public const double FooterHeight = 18;

    private volatile bool _changed;

    public ListPaneElement(SkinView view, SkinNode node, SkinElement? parent) : base(view, node, parent)
    {
        Model = (node.GetAttribute("kind") ?? string.Empty).ToLowerInvariant() switch
        {
            "skins" => new SkinChooserModel(view.Session),
            "radio" => new RadioTunerModel(view.Session),
            "settings" or "appearance" => new SettingsModel(view.Session),
            _ => new DeviceCopyModel(view.Session),
        };
        Model.Changed += () => _changed = true;
    }

    public ListPaneModel Model { get; }

    public int Selected { get; private set; } = -1;

    public int Scroll { get; private set; }

    /// <summary>Toolbar button rectangles (x, width), laid out by the renderer.</summary>
    public List<(double X, double Width)> ButtonBounds { get; } = [];

    public int PressedButton { get; private set; } = -1;

    /// <summary>The details panel beside the list; noDetails="true" (opt-in, narrow phone looks) leaves the whole width to the list.</summary>
    public bool ShowsDetails => Model.HasDetails && !GetBool("noDetails");

    public double ListWidth => ShowsDetails ? Math.Max(160, Math.Round(Width * 0.55)) : Width;

    public int VisibleRows => Math.Max(1, (int)((Height - ToolbarHeight - HeaderHeight - FooterHeight) / RowHeight));

    public override bool IsInteractive => true;

    public override bool HitTest(double x, double y) => Visible && base.HitTest(x, y);

    /// <summary>Called every tick: redraws after background updates (radio station lists).</summary>
    internal void Update()
    {
        if (!_changed)
            return;
        _changed = false;
        Selected = Math.Min(Selected, Model.Rows.Count - 1);
        Scroll = Math.Clamp(Scroll, 0, Math.Max(0, Model.Rows.Count - VisibleRows));
        View.Invalidate();
    }

    public override void OnPointerPressed(double x, double y)
    {
        base.OnPointerPressed(x, y);
        if (y < ToolbarHeight)
        {
            PressedButton = ButtonBounds.FindIndex(b => x >= b.X && x < b.X + b.Width);
            View.Invalidate();
            return;
        }

        if (RowAt(x, y) is { } row)
        {
            Selected = row;
            View.Invalidate();
        }
    }

    public override void OnPointerReleased(double x, double y, bool inside)
    {
        var pressed = PressedButton;
        PressedButton = -1;
        base.OnPointerReleased(x, y, inside);
        if (pressed >= 0 && y < ToolbarHeight && pressed == ButtonBounds.FindIndex(b => x >= b.X && x < b.X + b.Width))
            Model.Press(pressed, Selected);
        View.Invalidate();
    }

    public override void OnDoubleClick(double x, double y)
    {
        base.OnDoubleClick(x, y);
        if (RowAt(x, y) is { } row)
            Model.Activate(row);
    }

    public override void OnWheel(double delta)
    {
        Scroll = Math.Clamp(Scroll - Math.Sign(delta) * 3, 0, Math.Max(0, Model.Rows.Count - VisibleRows));
        View.Invalidate();
    }

    private int? RowAt(double x, double y)
    {
        var top = ToolbarHeight + HeaderHeight;
        if (x >= ListWidth || y < top || y >= Height - FooterHeight)
            return null;
        var row = Scroll + (int)((y - top) / RowHeight);
        return row < Model.Rows.Count ? row : null;
    }

    internal void Close() => Model.Close();
}

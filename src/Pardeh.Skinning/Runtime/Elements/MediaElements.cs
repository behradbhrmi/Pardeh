using Pardeh.Skinning.Imaging;
using Pardeh.Core.Playback;
using Pardeh.Skinning.Model;
using Pardeh.Skinning.Runtime.Scripting;

namespace Pardeh.Skinning.Runtime.Elements;

/// <summary>&lt;TEXT&gt;: a single line of text.</summary>
public sealed class TextElement(SkinView view, SkinNode node, SkinElement? parent) : SkinElement(view, node, parent)
{
    public string Text => GetString("value") ?? string.Empty;

    public int ForegroundColor =>
        (!Enabled ? SkinValue.ParseColor(Get("disabledForegroundColor")) : null)
        ?? (IsHovered && IsInteractive ? SkinValue.ParseColor(Get("hoverForegroundColor")) : null)
        ?? SkinValue.ParseColor(Get("foregroundColor")) ?? 0x000000;

    public int? BackgroundColor => SkinValue.ParseColor(Get("backgroundColor"));

    public string FontFamily => GetString("fontType") ?? GetString("fontFace") ?? "Tahoma";

    /// <summary>Size in points.</summary>
    public double FontSize => GetDouble("fontSize", 8);

    public string FontStyle =>
        (!Enabled ? GetString("disabledFontStyle") : null)
        ?? (IsHovered && IsInteractive ? GetString("hoverFontStyle") : null)
        ?? GetString("fontStyle") ?? "normal";

    public string Justification => GetString("justification") ?? "left";

    private double _textWidth = -1;

    /// <summary>Width of the current text in pixels (textWidth), measured by the host.</summary>
    public double TextWidth
    {
        get
        {
            if (_textWidth < 0)
                _textWidth = Math.Ceiling(View.Session.Host.MeasureText(Text, FontFamily, FontSize, FontStyle.Contains("bold", StringComparison.OrdinalIgnoreCase)));
            return _textWidth;
        }
    }

    public override object? GetMember(string name) =>
        name.Equals("textWidth", StringComparison.OrdinalIgnoreCase) ? TextWidth : base.GetMember(name);

    protected override void OnPropertyChanged(string name, object? oldValue, object? newValue)
    {
        base.OnPropertyChanged(name, oldValue, newValue);
        if (name is "value" or "Value" || name.StartsWith("font", StringComparison.OrdinalIgnoreCase))
        {
            var before = _textWidth;
            _textWidth = -1;
            if (!View.IsInitializing && Math.Abs(before - TextWidth) > 0.5)
                FireEvent("textwidth_onchange");
        }
    }

    protected override IEnumerable<string> KnownProperties =>
        ["value", "foregroundColor", "fontType", "fontFace", "fontSize", "fontStyle", "justification", "hoverForegroundColor",
         "hoverFontStyle", "disabledForegroundColor", "disabledFontStyle", "wordWrap", "scrolling", "textWidth"];

    internal override void ApplyDefaultSize()
    {
        if (!ExplicitAttributes.Contains("height"))
            SetInitial("height", Math.Ceiling(FontSize * 96 / 72 * 1.25));
    }

    public override bool HitTest(double x, double y) =>
        (IsInteractive || BackgroundColor is not null) && Width > 0 && base.HitTest(x, y);
}

/// <summary>WMPVIDEO / VIDEO: the video surface (black until video playback lands).</summary>
public sealed class VideoElement(SkinView view, SkinNode node, SkinElement? parent) : SkinElement(view, node, parent)
{
    public int BackgroundColor => SkinValue.ParseColor(Get("backgroundColor")) ?? 0x000000;

    protected override IEnumerable<string> KnownProperties => ["maintainAspectRatio", "windowless", "stretchToFit", "zoom", "fullScreen"];

    // Video surfaces fill their container when it grows (e.g. when the skin resizes itself to fit a video).
    protected override object? GetDefault(string name) =>
        name.Equals("horizontalAlignment", StringComparison.OrdinalIgnoreCase) || name.Equals("verticalAlignment", StringComparison.OrdinalIgnoreCase)
            ? "stretch"
            : base.GetDefault(name);

    public override bool HitTest(double x, double y) => Visible && base.HitTest(x, y);
}

/// <summary>WMPEFFECTS / EFFECTS: the visualization area.</summary>
public class EffectsElement(SkinView view, SkinNode node, SkinElement? parent) : SkinElement(view, node, parent)
{
    /// <summary>Titles of the visualizations ("Battery : Randomization" …), from <see cref="VisualizationCatalog"/>.</summary>
    public static readonly string[] EffectNames = VisualizationCatalog.All.Select(v => v.Title).ToArray();

    private int _effectIndex = VisualizationCatalog.Default;

    public Visualization Current => VisualizationCatalog.All[VisualizationCatalog.Wrap(EffectIndex)];

    /// <summary>Which of <see cref="EffectNames"/> is showing.</summary>
    public virtual int EffectIndex
    {
        get => _effectIndex;
        protected set => _effectIndex = value;
    }

    // Visualization surfaces fill their container when it grows.
    protected override object? GetDefault(string name) =>
        name.Equals("horizontalAlignment", StringComparison.OrdinalIgnoreCase) || name.Equals("verticalAlignment", StringComparison.OrdinalIgnoreCase)
            ? "stretch"
            : base.GetDefault(name);

    public string CurrentEffectTitle => Current.Type;

    public string CurrentPresetTitle => Current.Preset;

    protected override IEnumerable<string> KnownProperties =>
        ["currentEffectTitle", "currentEffectType", "currentPresetTitle", "currentPreset", "effectCount", "fullScreen", "windowed", "allowAll", "currentEffect", "palette", "picker", "vizSwipe"];

    protected override IEnumerable<string> KnownMethods => ["next", "previous", "nextPreset", "previousPreset", "settings"];

    public override object? GetMember(string name) => name.ToLowerInvariant() switch
    {
        "currenteffecttitle" or "currenteffecttype" or "currenteffect" => CurrentEffectTitle,
        "currentpresettitle" => CurrentPresetTitle,
        "effectcount" => (double)EffectNames.Length,
        "currentpreset" => (double)EffectIndex,
        _ => base.GetMember(name),
    };

    public override object? InvokeMember(string name, object?[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "next":
            case "nextpreset":
                EffectIndex = (EffectIndex + 1) % EffectNames.Length;
                View.Invalidate();
                return null;
            case "previous":
            case "previouspreset":
                EffectIndex = (EffectIndex + EffectNames.Length - 1) % EffectNames.Length;
                View.Invalidate();
                return null;
            default:
                return base.InvokeMember(name, args);
        }
    }

    public override bool HitTest(double x, double y) => Visible && base.HitTest(x, y);

    // ---- Phone looks (opt-in): the style chip and the swipe ------------------------------------------

    /// <summary>The phone looks' visualization picker: a small chip over the visualization opens the style sheet (picker="true").</summary>
    public bool HasPicker => GetBool("picker");

    /// <summary>A horizontal swipe on the visualization changes its style (vizSwipe="true"; the stage's seek swipe stays elsewhere).</summary>
    public bool SwipesStyle => GetBool("vizSwipe");

    /// <summary>The chip's rectangle in the element's own coordinates (bottom right corner).</summary>
    public static (double X, double Y, double Size) ChipRect(double width, double height) => (width - 42, height - 42, 34);

    public override bool IsInteractive => HasPicker || base.IsInteractive;

    public override void OnPointerReleased(double x, double y, bool inside)
    {
        base.OnPointerReleased(x, y, inside);
        var (cx, cy, size) = ChipRect(Width, Height);
        if (HasPicker && inside && Visible && x >= cx - 8 && x <= cx + size + 8 && y >= cy - 8 && y <= cy + size + 8)
            View.Session.Host.RunCommand("vizpicker", []);
    }

    /// <summary>Shows the visualization <paramref name="index"/> of <see cref="VisualizationCatalog.All"/> (the phone looks' picker).</summary>
    public void SelectEffect(int index)
    {
        EffectIndex = VisualizationCatalog.Wrap(index);
        View.Invalidate();
    }
}

/// <summary>PLAYLIST: the list of items in the current playlist.</summary>
public sealed class PlaylistElement(SkinView view, SkinNode node, SkinElement? parent) : SkinElement(view, node, parent)
{
    public static double RowHeight { get; set; } = 15;

    public int ScrollOffset { get; private set; }

    public int SelectedIndex { get; private set; } = -1;

    public int BackgroundColor => SkinValue.ParseColor(Get("backgroundColor")) ?? 0x000000;

    public int ForegroundColor => SkinValue.ParseColor(Get("foregroundColor")) ?? 0xFFFFFF;

    public int PlayingColor => SkinValue.ParseColor(Get("itemPlayingColor")) ?? 0x00FF00;

    public int SelectionColor => SkinValue.ParseColor(Get("itemSelectedBackgroundColor")) ?? 0x2B3B6B;

    protected override IEnumerable<string> KnownProperties =>
        ["columns", "columnsVisible", "dropDownVisible", "itemPlayingColor", "itemSelectedColor", "itemSelectedBackgroundColor", "foregroundColor", "touch", "numbered", "itemPlayingBackgroundColor", "headerHeight", "favIcons", "rowStyle"];

    /// <summary>Opt-in (attribute <c>headerHeight</c>, skin units): a band above the rows with the queue's size, its sort and its filter.</summary>
    public double HeaderHeight => Math.Max(0, GetDouble("headerHeight"));

    /// <summary>"fluent" (the modern looks) or "pictorial": which heart marks the favourites in the rows.</summary>
    public bool FluentHearts => string.Equals(GetString("favIcons"), "fluent", StringComparison.OrdinalIgnoreCase);

    private int _shownVersion = -1;
    private string _shownKey = string.Empty;
    private List<int> _shown = [];

    /// <summary>The queue positions the list shows, in order: all of them, or those that pass the queue's filter (display only).</summary>
    public IReadOnlyList<int> Shown
    {
        get
        {
            var player = View.Session.Player;
            var list = player.CurrentPlaylist;
            var filter = list.Filter;
            var key = $"{filter.Text}|{filter.FavoritesOnly}|{player.Favorites?.Count}";
            if (_shownVersion == list.Version && key == _shownKey)
                return _shown;
            _shownVersion = list.Version;
            _shownKey = key;
            if (!filter.IsActive)
            {
                _shown = [.. Enumerable.Range(0, list.Count)];
            }
            else
            {
                var items = list.Items;
                _shown = [.. Enumerable.Range(0, items.Count).Where(i => filter.Matches(items[i], player.IsFavorite(items[i])))];
            }

            ScrollOffset = Math.Clamp(ScrollOffset, 0, Math.Max(0, _shown.Count - VisibleRows));
            return _shown;
        }
    }

    /// <summary>The header band's text: "23 songs - Title (ascending)", or "3 of 23 songs match".</summary>
    public string HeaderText
    {
        get
        {
            var list = View.Session.Player.CurrentPlaylist;
            var count = list.Count;
            var text = count == 1 ? "1 song" : $"{count} songs";
            if (list.Filter.IsActive)
                text = $"{Shown.Count} of {text} shown";
            if (list.SortedBy is { } sorted)
                text += sorted.Field == Pardeh.Core.Library.SortField.Default
                    ? (sorted.Descending ? " - order added, reversed" : " - order added")
                    : $" - {Pardeh.Core.Library.SortSpec.Label(sorted.Field)} {(sorted.Descending ? "▼" : "▲")}";
            return text;
        }
    }

    /// <summary>
    /// Opt-in touch layout (attribute <c>touch="true"</c>): two-line rows (title, artist), the playing row on a band
    /// (<c>itemPlayingBackgroundColor</c>), a tap plays the item, a long press opens its menu, a drag scrolls row by row.
    /// </summary>
    public bool Touch => GetBool("touch");

    public int PlayingBackground => SkinValue.ParseColor(Get("itemPlayingBackgroundColor")) ?? SelectionColor;

    public override bool IsInteractive => true;

    public override bool HitTest(double x, double y) => Visible && base.HitTest(x, y);

    public int VisibleRows => Math.Max(1, (int)((Height - HeaderHeight) / RowHeight));

    /// <summary>The queue index of the row at <paramref name="y"/> (the filter hides rows: a row is a position in <see cref="Shown"/>), or -1.</summary>
    private int IndexAt(double y)
    {
        if (y < HeaderHeight)
            return -1;
        var position = ScrollOffset + (int)((y - HeaderHeight) / RowHeight);
        var shown = Shown;
        return position >= 0 && position < shown.Count ? shown[position] : -1;
    }

    // ---- Jump buttons (touch lists): first song, the playing song, last song ----------------------------

    public const double JumpButton = 32, JumpGap = 0, JumpMargin = 10, JumpGutter = 46;

    /// <summary>The three jump buttons show when the touch list is longer than its window.</summary>
    public bool JumpVisible => Touch && Shown.Count > VisibleRows;

    /// <summary>The rectangle of jump button 0 (first), 1 (now playing) or 2 (last), in the element's coordinates: a column at the right edge, a little below the header.</summary>
    public (double X, double Y, double Size) JumpRect(int button) =>
        (Width - JumpButton - JumpMargin, HeaderHeight + (Height - HeaderHeight - 3 * JumpButton) / 2 + button * JumpButton, JumpButton);

    private int JumpAt(double x, double y)
    {
        if (!JumpVisible)
            return -1;
        for (var button = 0; button < 3; button++)
        {
            var (rx, ry, size) = JumpRect(button);
            if (x >= rx - 4 && x <= rx + size + 4 && y >= ry && y <= ry + size)
                return button;
        }

        return -1;
    }

    private int MaxOffset => Math.Max(0, Shown.Count - VisibleRows);

    public void ScrollToTop()
    {
        ScrollOffset = 0;
        View.Invalidate();
    }

    public void ScrollToBottom()
    {
        ScrollOffset = MaxOffset;
        View.Invalidate();
    }

    /// <summary>Scrolls so the playing song is in the middle of the list (nothing happens when it is not in the shown rows).</summary>
    public void ScrollToCurrent()
    {
        var position = Shown.ToList().IndexOf(View.Session.Player.CurrentPlaylist.CurrentIndex);
        if (position < 0)
            return;
        ScrollOffset = Math.Clamp(position - VisibleRows / 2, 0, MaxOffset);
        View.Invalidate();
    }

    private int _revealedIndex = -2;

    /// <summary>Called as the list is drawn: when the playing song changed (or on the first draw) the list scrolls to it.</summary>
    public void RevealPlayingWhenChanged()
    {
        var index = View.Session.Player.CurrentPlaylist.CurrentIndex;
        if (index == _revealedIndex)
            return;
        _revealedIndex = index;
        ScrollToCurrent();
    }

    private int _jumpPressed = -1;

    private double _pressY;

    public override void OnPointerPressed(double x, double y)
    {
        base.OnPointerPressed(x, y);
        _pressY = y;
        _jumpPressed = JumpAt(x, y);
        SelectedIndex = _jumpPressed >= 0 ? -1 : IndexAt(y);
    }

    public override void OnPointerReleased(double x, double y, bool inside)
    {
        base.OnPointerReleased(x, y, inside);
        if (_jumpPressed >= 0)
        {
            var pressed = _jumpPressed;
            _jumpPressed = -1;
            if (inside && JumpAt(x, y) == pressed)
            {
                switch (pressed)
                {
                    case 0: ScrollToTop(); break;
                    case 1: ScrollToCurrent(); break;
                    default: ScrollToBottom(); break;
                }
            }

            SelectedIndex = -1;
            View.Invalidate();
            return;
        }

        // Touch: a tap (not a drag, not a long press) plays the row.
        if (Touch && inside && SelectedIndex >= 0 && Math.Abs(y - _pressY) < 14)
            View.Session.Player.PlayItem(SelectedIndex);
        View.Invalidate();
    }

    public override double TouchScrollStep => Touch ? RowHeight : base.TouchScrollStep;

    public override void OnTouchScroll(double step)
    {
        if (!Touch)
        {
            base.OnTouchScroll(step);
            return;
        }

        ScrollOffset = Math.Clamp(ScrollOffset - Math.Sign(step), 0, Math.Max(0, Shown.Count - VisibleRows));
        View.Invalidate();
    }

    /// <summary>Touch: long press on an item = Play, Add to / Remove from favourites, Remove, Move Up, Move Down.</summary>
    public override bool OnContextMenu(double x, double y)
    {
        var player = View.Session.Player;
        var list = player.CurrentPlaylist;
        var index = IndexAt(y);
        if (!Touch || index < 0 || index >= list.Count || JumpAt(x, y) >= 0)
            return false;
        SelectedIndex = index;
        View.Invalidate();
        var item = list.Items[index];
        var favorite = player.IsFavorite(item);
        string[] items = ["Play", favorite ? "Remove from favourites" : "Add to favourites", "Remove from list", "Move up", "Move down"];
        View.Session.Host.ShowPopup(View, items, -1, choice =>
        {
            switch (choice)
            {
                case 0: player.PlayItem(index); break;
                case 1 when player.Favorites is not null: player.SetFavorite(!favorite, item); break;
                case 2: list.RemoveAt(index); SelectedIndex = -1; break;
                case 3 when index > 0: list.Move(index, index - 1); SelectedIndex = index - 1; break;
                case 4 when index < list.Count - 1: list.Move(index, index + 1); SelectedIndex = index + 1; break;
            }

            View.Invalidate();
        });
        return true;
    }

    public override void OnDoubleClick(double x, double y)
    {
        var index = IndexAt(y);
        if (!Touch && index >= 0)
            View.Session.Player.PlayItem(index);
        base.OnDoubleClick(x, y);
    }

    public override void OnWheel(double delta)
    {
        ScrollOffset = Math.Clamp(ScrollOffset - Math.Sign(delta) * 3, 0, Math.Max(0, Shown.Count - VisibleRows));
        View.Invalidate();
    }
}

/// <summary>DROPDOWNPLAYLIST: shows the current playlist's name.</summary>
public sealed class DropDownPlaylistElement(SkinView view, SkinNode node, SkinElement? parent) : SkinElement(view, node, parent)
{
    /// <summary>The full-mode combo's frame (stretched) and its chevron, when the skin supplies them.</summary>
    public SkinBitmap? BackgroundImage => Image("dropDownBackgroundImage");

    public SkinBitmap? ArrowImage => Image("dropDownImage");

    public bool IsOpen { get; private set; }

    protected override IEnumerable<string> KnownProperties =>
        ["dropDownImage", "dropDownBackgroundImage", "playlistItemsVisible", "hueShift", "saturation", "statusColor"];

    internal override void ApplyDefaultSize()
    {
        if (!ExplicitAttributes.Contains("height"))
            SetInitial("height", BackgroundImage is null ? 20.0 : 21.0);
    }

    public override bool IsInteractive => true;

    public override void OnPointerPressed(double x, double y)
    {
        base.OnPointerPressed(x, y);
        var player = View.Session.Player;
        var items = player.CurrentPlaylist.Items.Select(i => i.Name).ToList();
        var current = player.CurrentMedia is { } media ? player.CurrentPlaylist.Items.ToList().IndexOf(media) : -1;
        if (items.Count == 0)
        {
            View.Session.Host.RunCommand("openfile", []);
            return;
        }

        IsOpen = true;
        View.Invalidate();
        View.Session.Host.ShowPopup(View, items, current, index =>
        {
            IsOpen = false;
            if (index >= 0)
                player.PlayItem(index);
            View.Invalidate();
        });
    }
}

/// <summary>
/// POPUP: a menu the script fills (appendItem(id, text) or appendItem(level, id, text)) and shows. show() waits
/// for the choice and returns the chosen item's id (-1 when cancelled), like the player did.
/// </summary>
public sealed class PopupElement(SkinView view, SkinNode node, SkinElement? parent) : SkinElement(view, node, parent)
{
    private readonly List<(int Id, string Text)> _items = [];
    private readonly HashSet<int> _checked = [];

    public override bool IsVisual => false;

    protected override IEnumerable<string> KnownProperties => ["selectedItem", "itemCount"];

    protected override IEnumerable<string> KnownMethods =>
        ["appendItem", "deleteItem", "deleteAll", "deleteAllItems", "show", "showEx", "getItem", "checkItem", "checkRadioItem", "enableItem"];

    public override object? GetMember(string name) => name.ToLowerInvariant() switch
    {
        "itemcount" => (double)_items.Count,
        _ => base.GetMember(name),
    };

    public override object? InvokeMember(string name, object?[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "appenditem":
                // (text) · (id, text) · (level, id, text): the last argument is the text, the one before it the id.
                var text = SkinValue.ToText(Arg(args, args.Length - 1));
                if (text.StartsWith("res://", StringComparison.OrdinalIgnoreCase) && View.Session.Resources?.GetString(text) is { } resolved)
                    text = resolved;
                var id = args.Length >= 2 ? (int)SkinValue.ToDouble(Arg(args, args.Length - 2), _items.Count) : _items.Count;
                _items.Add((id, text));
                return null;
            case "deleteitem":
                var index = (int)SkinValue.ToDouble(Arg(args, args.Length - 1), -1);
                _items.RemoveAll(i => i.Id == index);
                return null;
            case "deleteall" or "deleteallitems":
                _items.Clear();
                _checked.Clear();
                return null;
            case "getitem":
                var wanted = (int)SkinValue.ToDouble(Arg(args, 0), -1);
                return _items.FirstOrDefault(i => i.Id == wanted).Text;
            case "checkitem" or "checkradioitem":
                var checkedId = (int)SkinValue.ToDouble(Arg(args, 0));
                if (SkinValue.ToBool(Arg(args, 1), true))
                    _checked.Add(checkedId);
                else
                    _checked.Remove(checkedId);
                return null;
            case "enableitem":
                return null;
            case "showex":
                return ShowPlayerMenu((int)SkinValue.ToDouble(Arg(args, 0)));
            case "show":
                // show("Play"): one of the player's named menus instead of the script's items.
                if (Arg(args, 0) is string menu && menu.Length > 0)
                {
                    View.Session.Host.RunCommand("menu", [menu]);
                    return -1.0;
                }

                return (double)Choose(_items);
            default:
                return base.InvokeMember(name, args);
        }
    }

    /// <summary>Shows items and waits for the choice. Returns its id, or -1.</summary>
    private int Choose(IReadOnlyList<(int Id, string Text)> items)
    {
        if (items.Count == 0)
            return -1;
        var session = View.Session;
        // Checked items get a tick (the player's menus marked the current zoom, etc.).
        var labels = items.Select(i => (_checked.Contains(i.Id) ? "✓  " : string.Empty) + i.Text).ToList();
        var choice = session.RunModal(() => session.Host.ShowPopupModal(View, labels));
        var id = choice >= 0 && choice < items.Count ? items[choice].Id : -1;
        if (id >= 0)
            Set("selectedItem", (double)id);
        return id;
    }

    /// <summary>showEx(n): the player's own menus — 2 Premium Services, 3 Now Playing options.</summary>
    private object? ShowPlayerMenu(int id)
    {
        switch (id)
        {
            case 3:
                View.Session.Host.RunCommand("menu", ["nowplayingoptions"]);
                return null;
            case 2:
                if (Choose([(0, "Premium Services")]) == 0)
                    View.Session.App.SetMember("currentTask", "Services");
                return null;
            default:
                View.Session.Host.RunCommand("menu", ["view"]);
                return null;
        }
    }
}

/// <summary>EQUALIZERSETTINGS: script access to the 10-band equalizer (usually id "eq").</summary>
public sealed class EqualizerSettingsElement(SkinView view, SkinNode node, SkinElement? parent) : SkinElement(view, node, parent)
{
    public override bool IsVisual => false;

    private EqualizerSettings Eq => View.Session.Player.Equalizer;

    private static int? BandOf(string name) =>
        name.StartsWith("gainLevel", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(9), out var band) && band is >= 1 and <= 10
            ? band - 1
            : null;

    public override MemberKind GetMemberKind(string name)
    {
        if (BandOf(name) is not null)
            return MemberKind.Property;
        return name.ToLowerInvariant() switch
        {
            "bypass" or "currentpreset" or "currentpresettitle" or "presetcount" or "enhancedaudio" or "speakersize"
                or "trubasslevel" or "wowlevel" or "enablesplinetension" or "splinetension" or "crossfade" or "crossfadewindow"
                or "normalization" or "normalizationpeak" or "normalizationaverage" or "currentspeakername" => MemberKind.Property,
            "presettitle" or "reset" or "nextpreset" or "previouspreset" => MemberKind.Method,
            _ => base.GetMemberKind(name),
        };
    }

    public override object? GetMember(string name)
    {
        if (BandOf(name) is { } band)
            return Eq.GetGain(band);
        return name.ToLowerInvariant() switch
        {
            "bypass" => Eq.Bypass,
            "currentpreset" => (double)Eq.CurrentPreset,
            "currentpresettitle" => EqualizerSettings.Presets[Eq.CurrentPreset].Name,
            "presetcount" => (double)EqualizerSettings.Presets.Count,
            "crossfade" => Eq.CrossFade,
            "crossfadewindow" => Eq.CrossFadeSeconds * 1000,
            "normalization" => Eq.AutoVolume,
            "normalizationpeak" or "normalizationaverage" => 0.0,
            "enhancedaudio" => Eq.Enhanced,
            "wowlevel" => Eq.WowLevel,
            "trubasslevel" => Eq.TruBassLevel,
            "speakersize" => (double)Eq.SpeakerSize,
            "currentspeakername" => EqualizerSettings.SpeakerNames[Eq.SpeakerSize],
            _ => base.GetMember(name),
        };
    }

    public override void SetMember(string name, object? value)
    {
        if (BandOf(name) is { } band)
        {
            Eq.SetGain(band, SkinValue.ToDouble(value));
            View.Invalidate();
            return;
        }

        switch (name.ToLowerInvariant())
        {
            case "bypass":
                Eq.Bypass = SkinValue.ToBool(value);
                break;
            case "currentpreset":
                Eq.CurrentPreset = (int)SkinValue.ToDouble(value);
                break;
            case "crossfade":
                Eq.CrossFade = SkinValue.ToBool(value);
                break;
            case "crossfadewindow":
                Eq.CrossFadeSeconds = SkinValue.ToDouble(value) / 1000; // milliseconds in the object model
                break;
            case "normalization":
                Eq.AutoVolume = SkinValue.ToBool(value);
                break;
            case "enhancedaudio":
                Eq.Enhanced = SkinValue.ToBool(value);
                break;
            case "wowlevel":
                Eq.WowLevel = SkinValue.ToDouble(value);
                break;
            case "trubasslevel":
                Eq.TruBassLevel = SkinValue.ToDouble(value);
                break;
            case "speakersize":
                Eq.SpeakerSize = (int)SkinValue.ToDouble(value);
                break;
            default:
                base.SetMember(name, value);
                return;
        }

        View.Invalidate();
    }

    public override object? InvokeMember(string name, object?[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "presettitle":
                var index = (int)SkinValue.ToDouble(Arg(args, 0));
                return index >= 0 && index < EqualizerSettings.Presets.Count ? EqualizerSettings.Presets[index].Name : string.Empty;
            case "reset":
                Eq.Reset();
                return null;
            case "nextpreset":
                Eq.CurrentPreset = (Eq.CurrentPreset + 1) % EqualizerSettings.Presets.Count;
                return null;
            case "previouspreset":
                Eq.CurrentPreset = (Eq.CurrentPreset + EqualizerSettings.Presets.Count - 1) % EqualizerSettings.Presets.Count;
                return null;
            default:
                return base.InvokeMember(name, args);
        }
    }
}

/// <summary>PLAYER / NETWORK: hold player event handlers; their script scope is the player object.</summary>
public sealed class PlayerEventsElement(SkinView view, SkinNode node, SkinElement? parent) : SkinElement(view, node, parent)
{
    public override bool IsVisual => false;

    public override IScriptable ScriptTarget => View.Session.PlayerObject;
}

/// <summary>
/// APP / MEDIACENTER / PLAYERAPPLICATION: hold <c>name_onchange</c> handlers for a global object; their script
/// scope is that object.
/// </summary>
public sealed class ObjectEventsElement(SkinView view, SkinNode node, SkinElement? parent, IScriptable target) : SkinElement(view, node, parent)
{
    public override bool IsVisual => false;

    public override IScriptable ScriptTarget => target;
}

/// <summary>
/// TASKCENTER and plug-in hosts: areas the classic player filled with native content. A TASKCENTER shows the
/// pane for <c>app.currentTask</c> as an embedded skin view; plug-in hosts get Pardeh's own drawing.
/// </summary>
public sealed class HostAreaElement(SkinView view, SkinNode node, SkinElement? parent) : SkinElement(view, node, parent)
{
    private string? _task;

    public int? BackgroundColor => SkinValue.ParseColor(Get("backgroundColor"));

    /// <summary>The pane shown inside a TASKCENTER.</summary>
    public SkinView? Embedded { get; private set; }

    public bool IsTaskCenter => Tag == "TASKCENTER";

    /// <summary>"metadata", "captions" … for plug-in hosts (from id / objectID).</summary>
    public string PluginKind => (GetString("objectID") ?? Id ?? string.Empty).ToLowerInvariant();

    protected override IEnumerable<string> KnownProperties => ["objectID", "backgroundColor"];

    public override bool HitTest(double x, double y) =>
        Visible && base.HitTest(x, y) && (Embedded is not null || BackgroundColor is not null);

    internal void Update(TimeSpan elapsed)
    {
        // Wait until the host has its real size: pane scripts lay out from the size they start with.
        if (IsTaskCenter)
        {
            var task = View.Session.App.CurrentTask;
            if (!string.Equals(task, _task, StringComparison.OrdinalIgnoreCase) && Width > 0 && Height > 0)
            {
                _task = task;
                Embedded?.Close();
                Embedded = View.Session.CreatePaneView(task, this);
                View.LayoutVersion++;
            }
        }
        else if (GetString("objectID") is { Length: > 0 } plugin && IsShowing && Width > 0 && Height > 0
                 && !string.Equals(plugin, _task, StringComparison.OrdinalIgnoreCase))
        {
            _task = plugin;
            Embedded?.Close();
            Embedded = View.Session.CreatePluginView(plugin, this);
            View.LayoutVersion++;
        }

        if (Embedded is { } pane)
        {
            if (Math.Abs(pane.Root.Width - Width) > 0.5)
                pane.Root.Width = Width;
            if (Math.Abs(pane.Root.Height - Height) > 0.5)
                pane.Root.Height = Height;
            pane.Tick(elapsed);
        }
    }

    /// <summary>Visible, and so are all its parents.</summary>
    private bool IsShowing
    {
        get
        {
            for (SkinElement? e = this; e is not null; e = e.Parent)
            {
                if (!e.Visible)
                    return false;
            }

            return true;
        }
    }

    protected override void OnPropertyChanged(string name, object? oldValue, object? newValue)
    {
        // Like the player: an objectID it has no plug-in for reads back as "" (skins test for that and fall back).
        if (!IsTaskCenter && name.Equals("objectID", StringComparison.OrdinalIgnoreCase)
            && SkinValue.ToText(newValue) is { Length: > 0 } id && !View.Session.HasPluginPane(id))
        {
            Set("objectID", string.Empty, fireChange: false);
            return;
        }

        base.OnPropertyChanged(name, oldValue, newValue);
    }

    internal void ClosePane()
    {
        Embedded?.Close();
        Embedded = null;
    }
}

/// <summary>The video / visualization plug-in host (piDisplay): video when there is a picture, otherwise visualizations.</summary>
public sealed class DisplayElement(SkinView view, SkinNode node, SkinElement? parent) : EffectsElement(view, node, parent)
{
    /// <summary>The full mode's display follows mediacenter.effectType / effectPreset (the pane's arrows change them via vizRequest).</summary>
    public override int EffectIndex
    {
        get => VisualizationCatalog.IndexOf(SkinValue.ToText(View.Session.MediaCenter.GetMember("effectType")),
            (int)SkinValue.ToDouble(View.Session.MediaCenter.GetMember("effectPreset")));
        protected set => View.Session.ShowVisualization(value);
    }

    public override bool HitTest(double x, double y) => Visible && x >= 0 && y >= 0 && x < Width && y < Height;

    public override bool IsInteractive => true;

    /// <summary>Double-click on the video or visualization: full screen (the player's own behaviour).</summary>
    public override void OnDoubleClick(double x, double y)
    {
        base.OnDoubleClick(x, y);
        var center = View.Session.MediaCenter;
        if (SkinValue.ToBool(center.GetMember("fullScreenAvailable"), true) && View.Session.Player.CurrentMedia is not null)
            center.SetMember("fullScreenRequest", true);
    }
}

/// <summary>VIDEOSETTINGS (usually id "vs"): brightness, contrast, hue and saturation of the picture (-127 – 127).</summary>
public sealed class VideoSettingsElement(SkinView view, SkinNode node, SkinElement? parent) : SkinElement(view, node, parent)
{
    public override bool IsVisual => false;

    private VideoSettings Video => View.Session.Player.Video;

    public override MemberKind GetMemberKind(string name) => name.ToLowerInvariant() switch
    {
        "brightness" or "contrast" or "hue" or "saturation" => MemberKind.Property,
        "reset" => MemberKind.Method,
        _ => base.GetMemberKind(name),
    };

    public override object? GetMember(string name) => name.ToLowerInvariant() switch
    {
        "brightness" => Video.Brightness,
        "contrast" => Video.Contrast,
        "hue" => Video.Hue,
        "saturation" => Video.Saturation,
        _ => base.GetMember(name),
    };

    public override void SetMember(string name, object? value)
    {
        var number = SkinValue.ToDouble(value);
        switch (name.ToLowerInvariant())
        {
            case "brightness": Video.Brightness = number; break;
            case "contrast": Video.Contrast = number; break;
            case "hue": Video.Hue = number; break;
            case "saturation": Video.Saturation = number; break;
            default: base.SetMember(name, value); return;
        }

        View.Invalidate();
    }

    public override object? InvokeMember(string name, object?[] args)
    {
        if (name.Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            Video.Reset();
            return null;
        }

        return base.InvokeMember(name, args);
    }
}

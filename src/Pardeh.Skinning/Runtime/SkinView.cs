using Pardeh.Core.Playback;
using Pardeh.Skinning.Model;
using Pardeh.Skinning.Runtime.Elements;
using Pardeh.Skinning.Runtime.Scripting;
using Pardeh.Skinning.Wmp;

namespace Pardeh.Skinning.Runtime;

/// <summary>
/// A live instance of one &lt;VIEW&gt;: its element tree, its own script engine, bindings, animations and timers.
/// </summary>
public sealed class SkinView
{
    private static readonly HashSet<string> NumericAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "left", "top", "width", "height", "zIndex", "alpha", "minWidth", "minHeight", "maxWidth", "maxHeight", "borderSize", "min", "max",
    };

    private readonly Dictionary<string, SkinElement> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SkinElement> _all = [];
    private readonly List<Binding> _bindings = [];
    private readonly HashSet<SkinElement> _animating = [];
    private readonly List<Action> _unsubscribe = [];
    private double _timerElapsed;
    private bool _closed;

    internal SkinView(SkinSession session, SkinNode node)
    {
        Session = session;
        Node = node;
        Script = new ScriptHost(session.Log);
        Root = new ViewElement(this, node);
    }

    public SkinSession Session { get; }

    public SkinNode Node { get; }

    public ViewElement Root { get; private set; }

    public ScriptHost Script { get; }

    /// <summary>The task-center element this view is embedded in (null for a top-level view).</summary>
    public HostAreaElement? EmbeddedIn { get; internal set; }

    /// <summary>The <c>event</c> object for the input being handled.</summary>
    public EventObject Event { get; } = new();

    public string? Id => Node.Id;

    public bool IsInitializing { get; private set; } = true;

    /// <summary>Incremented whenever something that affects the window shape changes.</summary>
    public int LayoutVersion { get; internal set; }

    public IReadOnlyList<SkinElement> Elements => _all;

    /// <summary>Raised when something visible changed.</summary>
    public event Action? Invalidated;

    public SkinElement? FindById(string id) => _byId.GetValueOrDefault(id);

    public void Invalidate() => Invalidated?.Invoke();

    internal void Animate(SkinElement element) => _animating.Add(element);

    // ---- Loading -----------------------------------------------------------------------------------

    /// <summary>Start-up profiling hook (set by the host).</summary>
    public static Action<string>? Trace { get; set; }

    internal void Load()
    {
        var pending = new List<Binding>();
        Trace?.Invoke("view: elements building");
        Root = (ViewElement)Build(Node, null, pending);
        Trace?.Invoke("view: elements built (" + _all.Count + ")");

        // A view without a size gets a sensible default before expressions like view.width are evaluated.
        Root.ApplyDefaultSize();

        DefineGlobals();
        LoadScripts();
        Trace?.Invoke("view: scripts loaded");

        // JScript: attributes are evaluated in document order once every element and script exists.
        foreach (var item in pending)
        {
            var value = Script.EvaluateIn(item.Element.ScriptTarget, item.Expression);
            if (value is not null)
                item.Element.SetInitial(item.Property, value);
        }

        foreach (var element in _all)
            element.ApplyDefaultSize();

        foreach (var binding in _bindings)
        {
            var value = Script.EvaluateIn(binding.Element.ScriptTarget, binding.Expression);
            binding.LastValue = value;
            // Live expressions too: the one-time pass may have run before what they depend on was bound.
            if (value is not null)
                binding.Element.SetInitial(binding.Property, value);
        }

        // "center" alignment without an explicit position centres the element in its parent (the rail's handle).
        foreach (var element in _all)
        {
            if (element.Parent is not { } parent)
                continue;
            if (element.Width > 0 && !element.ExplicitAttributes.Contains("left") && string.Equals(element.GetString("horizontalAlignment"), "center", StringComparison.OrdinalIgnoreCase))
                element.SetInitial("left", Math.Round((parent.Width - element.Width) / 2));
            if (element.Height > 0 && !element.ExplicitAttributes.Contains("top") && string.Equals(element.GetString("verticalAlignment"), "center", StringComparison.OrdinalIgnoreCase))
                element.SetInitial("top", Math.Round((parent.Height - element.Height) / 2));
        }

        Trace?.Invoke("view: attributes evaluated");
        IsInitializing = false;
        foreach (var hook in _all.OfType<ObjectEventsElement>())
        {
            if (hook.ScriptTarget is ScriptableObject target)
            {
                Action<string> handler = name => hook.FireEvent(name + "_onchange");
                target.MemberChanged += handler;
                _unsubscribe.Add(() => target.MemberChanged -= handler);
            }
        }

        SyncWithPlayer();
        Trace?.Invoke("view: synced with player");
        Root.FireEvent("onload");
        Trace?.Invoke("view: onload done");
        // Full-mode skins set up their task buttons when the current task is first reported.
        foreach (var hook in _all.OfType<ObjectEventsElement>())
        {
            if (ReferenceEquals(hook.ScriptTarget, Session.App))
                hook.FireEvent("currentTask_onchange");
        }

        Invalidate();
    }

    private SkinElement Build(SkinNode node, SkinElement? parent, List<Binding> pending)
    {
        var element = parent is null ? Root : Create(node, parent);
        _all.Add(element);

        foreach (var (name, value) in node.Attributes)
        {
            if (IsEventAttribute(name, element))
            {
                element.Handlers[name] = SkinValue.StripScriptPrefix(value);
                continue;
            }

            element.ExplicitAttributes.Add(name);
            switch (SkinValue.Classify(value, out var expression))
            {
                case AttributeValueKind.Script:
                    pending.Add(new Binding(element, name, expression));
                    break;
                case AttributeValueKind.Binding:
                    _bindings.Add(new Binding(element, name, expression));
                    break;
                case AttributeValueKind.LiveScript:
                    // Evaluated in document order at load like JScript:, then kept current like a binding.
                    pending.Add(new Binding(element, name, expression));
                    _bindings.Add(new Binding(element, name, expression) { Live = true });
                    break;
                default:
                    var literal = SkinValue.ParseLiteral(value);
                    // e.g. top="svMain.top": a bare expression where a number belongs.
                    if (literal is string text && NumericAttributes.Contains(name) && text.Trim().Length > 0)
                        pending.Add(new Binding(element, name, text));
                    else
                        element.SetInitial(name, literal);
                    break;
            }
        }

        if (element.Id is { } id)
            _byId.TryAdd(id, element);

        foreach (var child in node.Children)
            element.Children.Add(Build(child, element, pending));

        return element;
    }

    private SkinElement Create(SkinNode node, SkinElement parent)
    {
        if (parent is ButtonGroupElement)
            return new ButtonGroupItem(this, node, parent);

        return node.Tag switch
        {
            "SUBVIEW" => new SubviewElement(this, node, parent),
            "BUTTON" or "PLAYBUTTON" or "PAUSEBUTTON" or "STOPBUTTON" or "PREVBUTTON" or "NEXTBUTTON" or "REWBUTTON"
                or "FFWDBUTTON" or "MUTEBUTTON" or "SHUFFLEBUTTON" or "REPEATBUTTON" or "RETURNBUTTON" or "IMAGEBUTTON"
                or "FAVBUTTON" or "QUEUESORTBUTTON" or "QUEUEFILTERBUTTON"
                => new ButtonElement(this, node, parent),
            "BUTTONGROUP" => new ButtonGroupElement(this, node, parent),
            "SLIDER" or "CUSTOMSLIDER" or "SEEKSLIDER" or "VOLUMESLIDER" or "BALANCESLIDER" or "PROGRESSBAR"
                => new SliderElement(this, node, parent),
            "TEXT" => new TextElement(this, node, parent),
            "GLYPHTEXT" => new GlyphTextElement(this, node, parent), // bitmap-font text (Winamp classic skins)
            "ANALYZER" => new AnalyzerElement(this, node, parent),   // spectrum bars (Winamp classic skins)
            "EQBARS" or "LEVELBARS" => new EqBarsElement(this, node, parent), // live equaliser glyph (opt-in, phone looks)
            "WMPVIDEO" or "VIDEO" => new VideoElement(this, node, parent),
            "WMPEFFECTS" or "EFFECTS" => new EffectsElement(this, node, parent),
            "PLAYLIST" when node.Attributes.Any(a => a.Key.Equals("playlistItemsVisible", StringComparison.OrdinalIgnoreCase) && SkinValue.ToBool(a.Value, true) == false)
                => new DropDownPlaylistElement(this, node, parent),
            "PLAYLIST" => new PlaylistElement(this, node, parent),
            "DROPDOWNPLAYLIST" => new DropDownPlaylistElement(this, node, parent),
            "POPUP" => new PopupElement(this, node, parent),
            "LIBRARY" => new LibraryElement(this, node, parent),
            "LISTPANE" => new Panes.ListPaneElement(this, node, parent),
            "LYRICS" => new LyricsElement(this, node, parent), // phone looks: the lyrics view of Now Playing
            "EQUALIZERSETTINGS" => new EqualizerSettingsElement(this, node, parent),
            "VIDEOSETTINGS" => new VideoSettingsElement(this, node, parent),
            "PLAYER" or "NETWORK" or "WMPPLAYER" => new PlayerEventsElement(this, node, parent),
            "APP" or "PLAYERAPPLICATION" => new ObjectEventsElement(this, node, parent, Session.App),
            "MEDIACENTER" => new ObjectEventsElement(this, node, parent, Session.MediaCenter),
            "HELPER" => new ObjectEventsElement(this, node, parent, Session.Helper),
            "PLUGINHOST" when string.Equals(node.Id, "piDisplay", StringComparison.OrdinalIgnoreCase) => new DisplayElement(this, node, parent),
            "TASKCENTER" or "PLUGINHOST" => new HostAreaElement(this, node, parent),
            _ => new GenericElement(this, node, parent),
        };
    }

    private static bool IsEventAttribute(string name, SkinElement element)
    {
        if (name.Length > 2 && name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.EndsWith("_onchange", StringComparison.OrdinalIgnoreCase))
            return true;
        // PLAYER / NETWORK attributes are all event hooks (openstatechange, playstatechange …).
        return element is PlayerEventsElement && !name.Equals("id", StringComparison.OrdinalIgnoreCase);
    }

    private void DefineGlobals()
    {
        Script.SetGlobal("player", Session.PlayerObject);
        Script.SetGlobal("theme", Session.ThemeObject);
        Script.SetGlobal("mediacenter", Session.MediaCenter);
        Script.SetGlobal("mediaCenter", Session.MediaCenter); // COM names were case-insensitive
        Script.SetGlobal("helper", Session.Helper);
        Script.SetGlobal("pardeh", Session.Pardeh);
        Script.SetGlobal("app", Session.App);
        Script.SetGlobal("event", Event);
        Script.SetGlobal("view", Root);

        foreach (var state in Enum.GetValues<PlayState>())
            Script.SetGlobal("ps" + state, (double)(int)state);
        foreach (var state in Enum.GetValues<OpenState>())
            Script.SetGlobal("os" + (state == OpenState.OpeningUnknownUrl ? "OpeningUnknownURL" : state.ToString()), (double)(int)state);

        foreach (var (id, element) in _byId)
            Script.SetGlobal(id, element.ScriptTarget);
    }

    private void LoadScripts()
    {
        var files = Root.GetString("scriptFile") ?? string.Empty;
        foreach (var file in files.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (file.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            {
                // The player's shared scripts, when imported; otherwise the compatibility script below stands in.
                if (Session.Resources?.GetText(file) is { } original)
                    Script.Execute(SkinPackage.DecodeText(original), file);
                continue;
            }

            var code = Session.Package.ReadText(file);
            if (code is null)
                Session.Log($"Script file not found in skin: {file}");
            else
                Script.Execute(code, file);
        }

        Script.Execute(CompatScript.Source, "pardeh-compat.js");
    }

    // ---- Running ------------------------------------------------------------------------------------

    public void Tick(TimeSpan elapsed)
    {
        if (_closed)
            return;

        var ms = elapsed.TotalMilliseconds;
        if (_animating.Count > 0)
        {
            foreach (var element in _animating.ToList())
            {
                if (!element.AdvanceAnimation(ms))
                    _animating.Remove(element);
            }
        }

        foreach (var binding in _bindings)
        {
            // Apply only when the result changes, so scripts can still set the property themselves.
            // A compiled skin's expression is evaluated at load; once a script lays the property out itself
            // (Now Playing's panel layout), the expression must not pull it back.
            if (binding.Live && binding.Element.ScriptSet.Contains(binding.Property))
                continue;
            var value = Script.EvaluateIn(binding.Element.ScriptTarget, binding.Expression);
            if (value is null || SkinValue.AreEqual(value, binding.LastValue))
                continue;
            binding.LastValue = value;
            binding.Element.Set(binding.Property, value);
        }

        foreach (var host in _all.OfType<HostAreaElement>())
            host.Update(elapsed);
        foreach (var pane in _all.OfType<Panes.ListPaneElement>())
            pane.Update();

        SyncWithPlayer();

        var interval = Root.GetDouble("timerInterval");
        if (interval > 0 && Root.Handlers.ContainsKey("ontimer"))
        {
            _timerElapsed += ms;
            if (_timerElapsed >= interval)
            {
                _timerElapsed = 0;
                Root.FireEvent("ontimer");
            }
        }

        Invalidate();
    }

    private void SyncWithPlayer()
    {
        foreach (var element in _all)
        {
            if (element is IPlayerSynced synced)
                synced.SyncWithPlayer();
        }
    }

    internal void RunHandler(SkinElement element, string code, (string Name, object? Value)[] args)
    {
        if (IsInitializing || _closed)
            return;
        Script.RunHandler(element.ScriptTarget, code, args);
    }

    internal void Close()
    {
        if (_closed)
            return;
        foreach (var unsubscribe in _unsubscribe)
            unsubscribe();
        _unsubscribe.Clear();
        foreach (var host in _all.OfType<HostAreaElement>())
            host.ClosePane();
        foreach (var pane in _all.OfType<Panes.ListPaneElement>())
            pane.Close();
        Root.FireEvent("onclose");
        _closed = true;
    }

    // ---- Player events ---------------------------------------------------------------------------------

    private IEnumerable<PlayerEventsElement> PlayerHooks => _all.OfType<PlayerEventsElement>();

    private IEnumerable<SkinView> Panes => _all.OfType<HostAreaElement>().Select(h => h.Embedded).OfType<SkinView>();

    internal void OnPlayStateChanged(PlayState state)
    {
        foreach (var hook in PlayerHooks)
            hook.FireEvent("playstatechange", ("NewState", (double)(int)state));
        foreach (var pane in Panes)
            pane.OnPlayStateChanged(state);
    }

    internal void OnOpenStateChanged(OpenState state)
    {
        foreach (var hook in PlayerHooks)
            hook.FireEvent("openstatechange", ("NewState", (double)(int)state));
        foreach (var pane in Panes)
            pane.OnOpenStateChanged(state);
    }

    internal void OnStatusChanged()
    {
        foreach (var hook in PlayerHooks)
            hook.FireEvent("status_onchange");
        foreach (var pane in Panes)
            pane.OnStatusChanged();
    }

    internal void OnModeChanged(string mode, bool value)
    {
        foreach (var hook in PlayerHooks)
            hook.FireEvent("modechange", ("ModeName", mode), ("NewValue", value));
        foreach (var pane in Panes)
            pane.OnModeChanged(mode, value);
    }

    internal void OnMediaChanged()
    {
        // The classic events pass the item: MediaChange(Item), CurrentItemChange(pdispMedia).
        var player = Session.Player;
        object? item = player.CurrentMedia is { } media ? Scripting.MediaObject.For(player, media) : null;
        foreach (var hook in PlayerHooks)
        {
            hook.FireEvent("mediachange", ("Item", item));
            hook.FireEvent("currentitemchange", ("pdispMedia", item));
        }

        var hasVideo = Session.Player.HasVideo;
        foreach (var video in _all.OfType<VideoElement>())
            video.FireEvent(hasVideo ? "onvideostart" : "onvideoend");
        foreach (var pane in Panes)
            pane.OnMediaChanged();
    }

    // ---- Layout queries -----------------------------------------------------------------------------------

    /// <summary>Children in paint order (zIndex, then document order).</summary>
    public static IEnumerable<SkinElement> PaintOrder(SkinElement element) =>
        element.Children.Where(c => c.IsVisual).OrderBy(c => c.ZIndex);

    /// <summary>The top-most visible element at a point in view coordinates.</summary>
    public SkinElement? HitTest(double x, double y) => HitTest(Root, x, y);

    private static SkinElement? HitTest(SkinElement element, double x, double y)
    {
        if (!element.Visible)
            return null;
        if (x < 0 || y < 0 || x >= element.Width || y >= element.Height)
            return null; // children are clipped to their parent

        foreach (var child in PaintOrder(element).Reverse())
        {
            var hit = HitTest(child, x - child.Left, y - child.Top);
            if (hit is not null)
                return hit;
        }

        if (!element.HitTest(x, y))
            return null;
        // Embedded panes take their own input.
        if (element is HostAreaElement { Embedded: { } pane })
            return pane.HitTest(x, y) ?? element;
        return element;
    }

    private sealed record Binding(SkinElement Element, string Property, string Expression)
    {
        public bool Live { get; init; }

        public object? LastValue { get; set; }
    }
}

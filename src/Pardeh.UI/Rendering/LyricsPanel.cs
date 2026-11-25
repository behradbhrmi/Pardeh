using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Pardeh.Core.Playback;
using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Runtime;
using Pardeh.Skinning.Wmp;

namespace Pardeh.UI.Rendering;

/// <summary>The colours of the desktop lyrics panel: they follow the full mode look.</summary>
internal sealed record LyricsPanelTheme(string Back, string Bar, string Ink, string Accent, bool FluentIcons, string Font);

/// <summary>
/// The lyrics and chords view of the desktop (full modes and skin mode): the same LYRICS element the phone looks use, in a
/// small skin of its own with a header, so that it can sit over a full mode's stage or in a window, whatever skin shows.
/// </summary>
internal sealed class LyricsPanel : UserControl, IDisposable
{
    public const double HeaderHeight = 24;

    private readonly SkinCanvas _canvas = new();
    private readonly SkinSession _session;

    public LyricsPanel(MediaPlayer player, ISkinHost main, Pardeh.Skinning.Runtime.ILyricsFeed feed, LyricsPanelTheme theme, bool inWindow, double width, double height, Action<string> command)
    {
        Colors = theme;
        InWindow = inWindow;
        var package = WmzLoader.FromFiles(
            new() { ["lyrics.wms"] = Encoding.UTF8.GetBytes(Markup(theme, inWindow, width, height)) }, "pardeh://lyrics-panel");
        _session = new SkinSession(package, player, new PanelHost(this, main, command), new AvaloniaImageDecoder(), new NoPreferences()) { Lyrics = feed };
        _session.Start();
        _canvas.View = _session.CurrentView;
        Content = _canvas;
        Resize(width, height);
    }

    public LyricsPanelTheme Colors { get; }

    public bool InWindow { get; }

    internal SkinView? View => _session.CurrentView;

    /// <summary>The panel's size in skin units.</summary>
    public void Resize(double width, double height)
    {
        if (_session.CurrentView is not { } view)
            return;
        width = Math.Max(160, Math.Round(width));
        height = Math.Max(120, Math.Round(height));
        if (Math.Abs(view.Root.Width - width) < 0.5 && Math.Abs(view.Root.Height - height) < 0.5)
            return;
        view.Root.Width = width;
        view.Root.Height = height;
        _canvas.InvalidateMeasure();
    }

    public void Tick(TimeSpan elapsed)
    {
        _session.Tick(elapsed);
        _canvas.InvalidateVisual();
    }

    public void Dispose()
    {
        _canvas.View = null;
        _session.Dispose();
    }

    private static string Markup(LyricsPanelTheme theme, bool inWindow, double width, double height)
    {
        var w = ((int)Math.Max(160, width)).ToString(CultureInfo.InvariantCulture);
        var h = ((int)Math.Max(120, height)).ToString(CultureInfo.InvariantCulture);
        var right = (int)Math.Max(160, width);
        var body = (int)Math.Max(120, height) - (int)HeaderHeight;
        var buttons = inWindow
            ? string.Empty
            : $"""
               <TEXT id="tPopOut" left="{right - 130}" top="5" width="76" height="15" justification="right" horizontalAlignment="right" value="Own window" fontType="{theme.Font}" fontSize="8" foregroundColor="{theme.Ink}" hoverForegroundColor="{theme.Accent}" cursor="hand" onClick="pardeh.command('lyricsPanel', 'window');" toolTip="Show the lyrics in their own window"/>
               <TEXT id="tClose" left="{right - 46}" top="5" width="38" height="15" justification="right" horizontalAlignment="right" value="Close" fontType="{theme.Font}" fontSize="8" foregroundColor="{theme.Ink}" hoverForegroundColor="{theme.Accent}" cursor="hand" onClick="pardeh.command('lyricsPanel', 'close');" toolTip="{System.Security.SecurityElement.Escape(Keymaps.WithKey("Close the lyrics", Pardeh.Core.Input.KeyCmd.Lyrics))}"/>
               """;
        return $"""
            <THEME id="PardehLyricsPanel" title="Lyrics">
              <VIEW id="vLyrics" width="{w}" height="{h}" resizable="true" titleBar="false" backgroundColor="{theme.Back}">
                <SUBVIEW id="bar" left="0" top="0" width="{w}" height="{(int)HeaderHeight}" backgroundColor="{theme.Bar}" horizontalAlignment="stretch">
                  <TEXT left="9" top="5" width="150" height="15" value="Lyrics" fontType="{theme.Font}" fontSize="8" fontStyle="bold" foregroundColor="{theme.Ink}"/>
                  {buttons}
                </SUBVIEW>
                <LYRICS id="lyr" left="0" top="{(int)HeaderHeight}" width="{w}" height="{body}" textColor="{theme.Ink}" accentColor="{theme.Accent}" plateColor="#FFFFFF" backColor="{theme.Back}" icons="{(theme.FluentIcons ? "fluent" : "pictorial")}" fontType="{theme.Font}" lyricsSize="14" toolbarHeight="36" horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </VIEW>
            </THEME>
            """;
    }

    private sealed class NoPreferences : IPreferenceStore
    {
        public string? Load(string themeId, string key) => null;

        public void Save(string themeId, string key, string value)
        {
        }
    }

    /// <summary>The panel's skin may open menus and run lyrics commands; everything about a window stays with the player's own skin.</summary>
    private sealed class PanelHost(LyricsPanel panel, ISkinHost main, Action<string> command) : ISkinHost
    {
        public void ViewChanged(SkinView view)
        {
        }

        public void ResizeView(SkinView view, double width, double height)
        {
        }

        public (double Width, double Height) MaxViewSize => (4000, 4000);

        public void MinimizeView(SkinView view)
        {
        }

        public void BeginResize(SkinView view, string edge)
        {
        }

        public void CloseSkin()
        {
        }

        public void ReturnToFullMode()
        {
        }

        public string? ShowOpenFileDialog(string dialogType, string filter) => null;

        public void LaunchUrl(string url)
        {
        }

        public void ShowPopup(SkinView view, IReadOnlyList<string> items, int selectedIndex, Action<int> onSelected)
        {
            var chosen = false;
            var menu = new ContextMenu
            {
                ItemsSource = items.Select((text, index) =>
                {
                    var item = new MenuItem { Header = text };
                    item.Click += (_, _) => { chosen = true; onSelected(index); };
                    return item;
                }).ToList(),
            };
            menu.Closed += (_, _) => { if (!chosen) onSelected(-1); };
            menu.Open(panel._canvas);
        }

        public int ShowPopupModal(SkinView view, IReadOnlyList<string> items) => -1;

        public void ShowMessage(string text) => main.ShowMessage(text);

        public double MeasureText(string text, string fontFamily, double sizePoints, bool bold) => main.MeasureText(text, fontFamily, sizePoints, bold);

        public object? RunCommand(string name, IReadOnlyList<object?> args)
        {
            if (name.Equals("lyricsPanel", StringComparison.OrdinalIgnoreCase))
            {
                command(args.Count > 0 ? args[0]?.ToString() ?? string.Empty : string.Empty);
                return null;
            }

            return name.Equals("lyrics", StringComparison.OrdinalIgnoreCase) ? main.RunCommand(name, args) : null;
        }

        public void Log(string message) => main.Log(message);
    }
}

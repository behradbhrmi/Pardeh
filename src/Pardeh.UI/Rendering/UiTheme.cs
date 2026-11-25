using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;

namespace Pardeh.UI.Rendering;

/// <summary>
/// The Appearance options (classic XP or modern Windows 11 per item) as fonts and menu themes.
/// Skin bitmaps and skin-specified fonts are never touched: only Pardeh-drawn UI, menus and dialogs.
/// </summary>
internal static class UiTheme
{
    public const string ClassicFamily = "Tahoma";
    public const string ClassicTitleFamily = "Trebuchet MS";
    public const string ModernFamily = "Segoe UI, Tahoma";
    public const string ModernDisplayFamily = "Segoe UI Variable Display, Segoe UI, Tahoma";

    private static readonly FontFamily Classic = new(ClassicFamily);
    private static readonly FontFamily Modern = new(ModernFamily);
    private static ResourceDictionary? _menus;
    private static Style? _fontStyle;
    private static Styles? _dialogStyles;
    private static ResourceDictionary? _classicControls;

    public static bool ModernFont => AppSettings.Current.ModernFont;

    /// <summary>The UI font: Tahoma, or Segoe UI with the modern font option.</summary>
    public static FontFamily Family => ModernFont ? Modern : Classic;

    public static Typeface Typeface(bool bold = false) =>
        new(Family, FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal);

    /// <summary>Applies the menu and font options app-wide; menus and dialogs opened afterwards pick them up.</summary>
    public static void Apply(bool darkSurface = false)
    {
        if (Application.Current is not { } app)
            return;

        // Windows 11's default blue for Fluent accents (toggle switches, default buttons, selection), not the
        // system accent colour, which may be anything (the user's was purple).
        foreach (var (key, color) in new[]
        {
            ("SystemAccentColor", "#0067C0"), ("SystemAccentColorDark1", "#005A9E"), ("SystemAccentColorDark2", "#004275"),
            ("SystemAccentColorDark3", "#002642"), ("SystemAccentColorLight1", "#0078D4"), ("SystemAccentColorLight2", "#4CC2FF"),
            ("SystemAccentColorLight3", "#99EBFF"),
        })
            app.Resources[key] = Color.Parse(color);

        // Dialog windows and menus (classic or Fluent) take the UI font; skins set their own fonts on their text.
        if (_fontStyle is not null)
            app.Styles.Remove(_fontStyle);
        _fontStyle = new Style(x => Selectors.Or(x.OfType<Window>(), x.OfType<ContextMenu>()))
        {
            Setters = { new Setter(TemplatedControl.FontFamilyProperty, Family) },
        };
        app.Styles.Add(_fontStyle);

        if (_dialogStyles is not null)
            app.Styles.Remove(_dialogStyles);
        _dialogStyles = DialogStyles.Create(AppSettings.Current.ModernDialogs, Family);
        app.Styles.Add(_dialogStyles);

        var merged = app.Resources.MergedDictionaries;
        if (_menus is not null)
            merged.Remove(_menus);
        var modernMenus = AppSettings.Current.ModernMenus;
        _menus = MenuTheme.Create(modernMenus || darkSurface, Family, modernMenus || darkSurface ? (ModernFont ? 14 : 12) : (ModernFont ? 12 : 11), darkSurface);
        merged.Add(_menus);

        if (_classicControls is not null)
            merged.Remove(_classicControls);
        _classicControls = null;
        if (!AppSettings.Current.ModernControls)
        {
            _classicControls = ClassicControlsTheme.Create(Family);
            merged.Add(_classicControls);
        }
    }

    /// <summary>
    /// Inside <paramref name="root"/>, scrollbars and scroll viewers use the Fluent (Windows 11) themes even when the
    /// classic XP scrollbars are on app-wide (the modern settings pages must not mix the two).
    /// </summary>
    public static void UseFluentScrolling(Control root)
    {
        if (Application.Current?.Styles.OfType<Avalonia.Themes.Fluent.FluentTheme>().FirstOrDefault() is not { } fluent)
            return;
        foreach (var type in new[] { typeof(ScrollViewer), typeof(ScrollBar), typeof(ToolTip) })
        {
            if (fluent.TryGetResource(type, ThemeVariant.Light, out var theme) && theme is ControlTheme controlTheme)
                root.Resources[type] = controlTheme;
        }
    }

    /// <summary>Marks a Pardeh dialog so the dialog styles apply; the modern look also gives its default button the accent colour.</summary>
    public static void Dialog(Window dialog)
    {
        dialog.Classes.Add(DialogStyles.Class);
        dialog.Opened += (_, _) =>
        {
            if (!AppSettings.Current.ModernDialogs)
                return;
            foreach (var button in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(dialog).OfType<Button>())
            {
                if (button.IsDefault)
                    button.Classes.Add("accent");
            }
        };
    }
}

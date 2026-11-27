using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Pardeh.Core.Library;
using Pardeh.Core.Playback;

namespace Pardeh.UI.Rendering;

/// <summary>What the Skins and Pages pages need from the main window.</summary>
internal interface ISettingsHost
{
    /// <summary>Built-in full modes ("fullmode:&lt;look&gt;") and installed skins: key and display name.</summary>
    IReadOnlyList<(string Key, string Name)> SkinEntries();

    string? CurrentSkinEntry();

    void ApplySkinEntry(string key);

    void AddSkin();

    void OpenSkinsFolder();

    /// <summary>A picture of the skin, or null while it is being drawn (ask again later).</summary>
    Avalonia.Media.Imaging.Bitmap? SkinPicture(string key);

    WindowStyle WindowStyle { get; }

    void SetWindowStyle(WindowStyle style);

    /// <summary>The full mode showing is a dark design (the modern settings follow it).</summary>
    bool DarkSurface { get; }

    /// <summary>Phone skins: the colours of the mobile settings (dark or light, the skin's accent).</summary>
    MobileTheme PhoneTheme { get; }

    /// <summary>The lyrics of the playing track (Settings > Lyrics: clearing the stored lyrics), or null.</summary>
    Pardeh.UI.LyricsKit.LyricsController? Lyrics => null;
}

/// <summary>
/// Tools › Options, and the same pages live in full mode's Settings page: Skins, Pages, Player, Appearance, Library,
/// Audio and Video, File Types. Every change applies (and is saved) right away.
/// <para>Two looks, following Appearance › Dialogs: classic XP tabs with check boxes, or Windows 11 Settings (a
/// navigation list, a large page title and rounded cards with toggle switches), light or dark with the full mode.</para>
/// </summary>
internal static partial class OptionsDialog
{
    /// <summary>The settings pages for a window (with a host, the Skins and Pages pages come first).</summary>
    public static Control Pages(TopLevel owner, MediaPlayer player, MediaLibrary library, ISettingsHost? host) =>
        new SettingsBuilder(owner, player, library, host).Build();

    /// <summary>The Settings page's content (laid over the skin by the window).</summary>
    public static Control Panel(TopLevel owner, MediaPlayer player, MediaLibrary library, ISettingsHost host)
    {
        if (Compact)
            return new MobileSettingsView(owner, player, library, host, host.PhoneTheme); // phone: the mobile settings screen
        var builder = new SettingsBuilder(owner, player, library, host);
        Control content = builder.Build(withTitle: true);
        if (builder.Modern)
            content = new ThemeVariantScope { RequestedThemeVariant = builder.Variant, Child = content };
        var panel = new UserControl { Content = content };
        panel.Classes.Add(DialogStyles.Class);
        builder.Decorate(panel);
        return panel;
    }

    public static async Task Show(Window owner, MediaPlayer player, MediaLibrary library, Action applied, ISettingsHost? host = null)
    {
        var settings = AppSettings.Current;
        var builder = new SettingsBuilder(owner, player, library, host);
        var content = builder.Build(withTitle: false);
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Children = { ok } };
        DockPanel.SetDock(footer, Dock.Bottom);
        var dialog = new Window
        {
            Title = "Options",
            Width = builder.Modern ? 860 : 640,
            Height = builder.Modern ? 600 : 540,
            CanResize = builder.Modern,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel { Margin = new Thickness(builder.Modern ? 0 : 10), Children = { footer, content } },
        };
        if (builder.Modern)
            footer.Margin = new Thickness(0, 0, 16, 14);
        ok.Click += (_, _) => dialog.Close();
        UiTheme.Dialog(dialog);
        builder.Decorate(dialog);
        await dialog.ShowDialog(owner);
        settings.Save();
        applied();
    }

    /// <summary>Builds the pages in the classic or the Windows 11 look.</summary>
    /// <summary>Phone layouts: the Settings page uses a tab strip instead of the side navigation (set by the player view).</summary>
    public static bool Compact { get; set; }

    /// <summary>The title of the page the next settings pages open on (null: the first).</summary>
    public static string? StartPage { get; set; }

    private sealed partial class SettingsBuilder(TopLevel owner, MediaPlayer player, MediaLibrary library, ISettingsHost? host)
    {
        private readonly AppSettings _settings = AppSettings.Current;

        public bool Modern { get; } = AppSettings.Current.ModernDialogs || Compact;

        private bool Dark => Modern && host?.DarkSurface == true;

        private static FontFamily UiFont => UiTheme.Family;

        // Windows 11 Settings colours, light and dark.
        private IBrush PageBack => Brush(Dark ? "#202020" : "#F3F3F3");
        private IBrush CardBack => Brush(Dark ? "#2B2B2B" : "#FBFBFB");
        private IBrush CardBorder => Brush(Dark ? "#1D1D1D" : "#E5E5E5");
        private IBrush Ink => Brush(Dark ? "#FFFFFF" : "#1A1A1A");
        private IBrush SubInk => Brush(Dark ? "#C5C5C5" : "#5F5F5F");
        private IBrush NavSelected => Brush(Dark ? "#2D2D2D" : "#EAEAEA");
        private IBrush AccentBrush => Brush(Dark ? "#4CC2FF" : "#0067C0");

        private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));

        public ThemeVariant Variant => Dark ? ThemeVariant.Dark : ThemeVariant.Light;

        /// <summary>Theme the modern pages: Fluent scrollbars inside, and the dark variant over dark full modes.</summary>
        public void Decorate(Control root)
        {
            if (!Modern)
                return;
            UiTheme.UseFluentScrolling(root);
            if (root is Window window)
            {
                window.RequestedThemeVariant = Variant;
                window.Background = PageBack;
            }
        }

        public Control Build(bool withTitle = true)
        {
            var pages = new List<(string Title, string Glyph, Func<Control> Content)>();
            if (host is not null)
            {
                pages.Add(("Skins", "", () => SkinsPage(host)));
                pages.Add(("Pages", "", () => PagesPage(host)));
            }

            pages.Add(("Player", "", PlayerPage));
            pages.Add(("Appearance", "", AppearancePage));
            pages.Add(("Library", "", LibraryPage));
            pages.Add(("Audio and Video", "", AudioVideoPage));
            if (!OperatingSystem.IsAndroid())
            {
                pages.Add(("Playback", "", PlaybackPage));
                pages.Add(("Lyrics", "", LyricsPage));
                pages.Add(("Keyboard", "", KeyboardPage));
            }

            if (PlatformServices.Current.FileAssociations is not null)
                pages.Add(("File Types", "", FileTypesPage));
            return Modern ? ModernShell(pages, withTitle) : ClassicShell(pages, withTitle);
        }

        // ---- Shells -----------------------------------------------------------------------------

        /// <summary>The page to open on: <see cref="StartPage"/>; PARDEH_SETTINGS_PAGE is a development aid (for snapshots).</summary>
        private static int FirstPage(List<(string Title, string Glyph, Func<Control> Content)> pages)
        {
            var wanted = StartPage ?? Environment.GetEnvironmentVariable("PARDEH_SETTINGS_PAGE");
            return Math.Max(0, pages.FindIndex(p => p.Title == wanted));
        }

        private Control ClassicShell(List<(string Title, string Glyph, Func<Control> Content)> pages, bool withTitle)
        {
            var tabs = new TabControl();
            foreach (var (title, _, content) in pages)
            {
                tabs.Items.Add(new TabItem
                {
                    Header = new TextBlock { Text = title, FontFamily = UiFont, FontSize = 12 },
                    Content = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(12), Spacing = 8, Children = { content() } } },
                });
            }

            tabs.SelectedIndex = FirstPage(pages);
            if (!withTitle)
                return tabs;
            var heading = new TextBlock { Text = "Settings", FontFamily = UiFont, FontSize = 15, FontWeight = FontWeight.Bold, Margin = new Thickness(2, 0, 0, 8) };
            DockPanel.SetDock(heading, Dock.Top);
            return new DockPanel { Margin = new Thickness(8), Children = { heading, tabs } };
        }

        /// <summary>Windows 11 Settings: navigation on the left (accent bar on the selected page), the page on the right.</summary>
        private Control ModernShell(List<(string Title, string Glyph, Func<Control> Content)> pages, bool withTitle)
        {
            var built = new Control?[pages.Count];
            var host = new ContentControl();
            var title = new TextBlock { FontSize = 26, FontWeight = FontWeight.SemiBold, Foreground = Ink, Margin = new Thickness(0, 0, 0, 16) };
            var scroller = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new StackPanel { Margin = new Thickness(28, 22, 28, 24), MaxWidth = 900, Children = { title, host } },
            };

            if (Compact)
                return CompactShell(pages, built, host, title, scroller, withTitle);

            var nav = new StackPanel { Spacing = 2, Margin = new Thickness(10, 0, 10, 10) };
            var items = new List<(Border Item, Border Bar)>();
            void Select(int index)
            {
                built[index] ??= pages[index].Content();
                host.Content = built[index];
                title.Text = pages[index].Title;
                scroller.Offset = default;
                for (var i = 0; i < items.Count; i++)
                {
                    items[i].Item.Background = i == index ? NavSelected : Brushes.Transparent;
                    items[i].Bar.IsVisible = i == index;
                }
            }

            for (var i = 0; i < pages.Count; i++)
            {
                var index = i;
                var bar = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(1.5), Background = AccentBrush, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("3,Auto,*"),
                    Children =
                    {
                        bar,
                        new TextBlock { Text = pages[i].Glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 16, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 14, 0), [Grid.ColumnProperty] = 1 },
                        new TextBlock { Text = pages[i].Title, FontSize = 14, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center, [Grid.ColumnProperty] = 2 },
                    },
                };
                var item = new Border { Height = 36, CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Child = row, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
                item.PointerEntered += (_, _) => { if (!bar.IsVisible) item.Background = Brush(Dark ? "#262626" : "#EDEDED"); };
                item.PointerExited += (_, _) => { if (!bar.IsVisible) item.Background = Brushes.Transparent; };
                item.PointerPressed += (_, _) => Select(index);
                items.Add((item, bar));
                nav.Children.Add(item);
            }

            var left = new DockPanel
            {
                Width = 230,
                Children =
                {
                    new TextBlock { Text = "Settings", FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Ink, Margin = new Thickness(22, 18, 0, 14), IsVisible = withTitle, [DockPanel.DockProperty] = Dock.Top },
                    new ScrollViewer { Content = nav, Margin = new Thickness(0, withTitle ? 0 : 14, 0, 0) },
                },
            };
            Select(FirstPage(pages));
            var shell = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Background = PageBack, Children = { left, scroller } };
            Grid.SetColumn(scroller, 1);
            return shell;
        }

        /// <summary>Phone: the page list becomes a scrolling strip of touch-sized tabs above the page.</summary>
        private Control CompactShell(List<(string Title, string Glyph, Func<Control> Content)> pages, Control?[] built, ContentControl host,
            TextBlock title, ScrollViewer scroller, bool withTitle)
        {
            if (scroller.Content is StackPanel body)
                body.Margin = new Thickness(14, 10, 14, 18);
            title.FontSize = 20;
            title.Margin = new Thickness(0, 0, 0, 10);
            var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(6, 0) };
            var tabs = new List<(Border Item, Border Bar)>();
            void Select(int index)
            {
                built[index] ??= pages[index].Content();
                host.Content = built[index];
                title.Text = pages[index].Title;
                scroller.Offset = default;
                for (var i = 0; i < tabs.Count; i++)
                {
                    tabs[i].Item.Background = i == index ? NavSelected : Brushes.Transparent;
                    tabs[i].Bar.IsVisible = i == index;
                }
            }

            for (var i = 0; i < pages.Count; i++)
            {
                var index = i;
                var bar = new Border { Height = 3, Width = 28, CornerRadius = new CornerRadius(1.5), Background = AccentBrush, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2), IsVisible = false };
                var label = new TextBlock { Text = pages[i].Title, FontSize = 14, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
                var item = new Border { MinHeight = 46, CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Child = new Grid { Children = { label, bar } } };
                item.PointerPressed += (_, _) => Select(index);
                tabs.Add((item, bar));
                strip.Children.Add(item);
            }

            Select(FirstPage(pages));
            var top = new ScrollViewer { Content = strip, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
            DockPanel.SetDock(top, Dock.Top);
            return new DockPanel { Background = PageBack, Children = { top, scroller } };
        }

        // ---- Building blocks (classic: check boxes and labels; modern: cards) ------------------

        private Control Section(string text) => Modern
            ? new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Ink, Margin = new Thickness(2, 14, 0, 6) }
            : new TextBlock { Text = text, FontFamily = UiFont, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6, 0, 0) };

        private TextBlock Note(string text) => Modern
            ? new TextBlock { Text = text, FontSize = 12, Foreground = SubInk, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 6) }
            : new TextBlock { Text = text, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray };

        /// <summary>A Windows 11 settings card: title and description on the left, the control on the right.</summary>
        private Control Card(string title, string? description, Control? control, string? glyph = null)
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
            text.Children.Add(new TextBlock { Text = title, FontSize = 14, Foreground = Ink, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrEmpty(description))
                text.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = SubInk, TextWrapping = TextWrapping.Wrap });
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), MinHeight = 44 };
            if (glyph is not null)
                grid.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 18, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            if (control is not null)
            {
                control.VerticalAlignment = VerticalAlignment.Center;
                control.Margin = new Thickness(16, 0, 0, 0);
                Grid.SetColumn(control, 2);
                grid.Children.Add(control);
            }

            return new Border
            {
                Background = CardBack,
                BorderBrush = CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 10),
                Margin = new Thickness(0, 0, 0, 3),
                Child = grid,
            };
        }

        /// <summary>An on / off setting: a check box (classic) or a card with a toggle switch (modern).</summary>
        private Control Toggle(string title, string? description, bool value, Action<bool> set)
        {
            if (!Modern)
            {
                var box = new CheckBox { Content = title, IsChecked = value, FontFamily = UiFont };
                box.IsCheckedChanged += (_, _) => set(box.IsChecked == true);
                return box;
            }

            var toggle = new ToggleSwitch { IsChecked = value, OnContent = "On", OffContent = "Off", MinWidth = 0 };
            toggle.IsCheckedChanged += (_, _) => set(toggle.IsChecked == true);
            return Card(title, description, toggle);
        }

        /// <summary>A setting with its own control (combo box, numbers, buttons): label beside it, or a card.</summary>
        private Control Row(string title, string? description, Control control)
        {
            if (Modern)
                return Card(title, description, control);
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new TextBlock { Text = title + ":", VerticalAlignment = VerticalAlignment.Center, FontFamily = UiFont }, control },
            };
        }

        private ComboBox Choice(IEnumerable<string> items, int index, Action<int> changed, double width = 220)
        {
            var combo = new ComboBox { ItemsSource = items.ToList(), SelectedIndex = index, MinWidth = width };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) changed(combo.SelectedIndex); };
            return combo;
        }

        private StackPanel Stack(params Control[] children)
        {
            var panel = new StackPanel { Spacing = Modern ? 0 : 8 };
            panel.Children.AddRange(children);
            return panel;
        }

        private Button Btn(string text, System.Action click, bool accent = false)
        {
            var button = new Button { Content = text };
            if (accent && Modern)
                button.Classes.Add("accent");
            button.Click += (_, _) => click();
            return button;
        }

        // ---- Pages ------------------------------------------------------------------------------

        private Control SkinsPage(ISettingsHost host)
        {
            var entries = host.SkinEntries().ToList();
            var list = Compact
                ? new ListBox { MinHeight = 160, MaxHeight = 300, HorizontalAlignment = HorizontalAlignment.Stretch,
                    ItemsSource = entries.Select(e => e.Name).ToList(),
                    ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((name, _) =>
                        new TextBlock { Text = name, FontSize = 15, Foreground = Ink, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0), MinHeight = 28 }) }
                : new ListBox { ItemsSource = entries.Select(e => e.Name).ToList(), MinWidth = 220, MinHeight = 220, MaxHeight = 259 };
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled); // no sideways jump to the selection
            if (Compact)
            {
                // Touch-sized rows (48 dp) and no horizontal scroll.
                list.Styles.Add(new Style(x => x.OfType<ListBoxItem>()) { Setters = { new Setter(Layoutable.MinHeightProperty, 48.0), new Setter(Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Stretch) } });
            }

            var picture = new Image { Stretch = Stretch.Uniform, MaxHeight = Compact ? 200 : 230, HorizontalAlignment = HorizontalAlignment.Center };
            var caption = new TextBlock { FontFamily = Modern ? FontFamily.Default : UiFont, FontSize = Modern ? 14 : 12, FontWeight = FontWeight.SemiBold, Foreground = Modern ? Ink : Brushes.Black, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

            void ShowPicture()
            {
                if (list.SelectedIndex < 0)
                    return;
                var key = entries[list.SelectedIndex].Key;
                caption.Text = entries[list.SelectedIndex].Name;
                picture.Source = host.SkinPicture(key);
                if (picture.Source is null) // still being drawn: look again shortly
                    Avalonia.Threading.DispatcherTimer.RunOnce(() => { if (list.SelectedIndex >= 0 && entries[list.SelectedIndex].Key == key) picture.Source = host.SkinPicture(key); }, TimeSpan.FromMilliseconds(400));
            }

            void Apply()
            {
                if (list.SelectedIndex >= 0)
                    host.ApplySkinEntry(entries[list.SelectedIndex].Key);
            }

            list.SelectedIndex = Math.Max(0, entries.FindIndex(e => e.Key == host.CurrentSkinEntry()));
            list.SelectionChanged += (_, _) => ShowPicture();
            list.DoubleTapped += (_, _) => Apply();
            ShowPicture();

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0),
                Children = { Btn("Apply Skin", Apply, accent: true), Btn("Add Skin...", host.AddSkin), Btn("Skins Folder", host.OpenSkinsFolder) } };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,16,2*") };
            var right = new StackPanel { Children = { picture, caption, Note("Full modes are built into Pardeh. Skins (.wmz, .wsz) are used in skin mode.") } };
            if (Compact)
            {
                // Phone: list, then the preview, then full-width buttons; all stacked.
                foreach (var button in buttons.Children.OfType<Button>())
                {
                    button.HorizontalAlignment = HorizontalAlignment.Stretch;
                    button.HorizontalContentAlignment = HorizontalAlignment.Center;
                    button.MinHeight = 48;
                }

                buttons.Orientation = Orientation.Vertical;
                var stacked = new StackPanel { Spacing = 12, Children = { list, right } };
                var phoneCard = new Border { Background = CardBack, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12),
                    Child = new StackPanel { Children = { stacked, buttons } } };
                return Stack(Note("Choose how full mode looks, or a skin for skin mode."), phoneCard);
            }

            Grid.SetColumn(right, 2);
            grid.Children.AddRange([list, right]);

            if (!Modern)
                return Stack(Section("Full modes and skins"), grid, buttons);
            var card = new Border { Background = CardBack, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(16),
                Child = new StackPanel { Children = { grid, buttons } } };
            return Stack(Note("Choose how full mode looks, or a skin for skin mode."), card);
        }

        private Control PagesPage(ISettingsHost host)
        {
            var panel = Stack(Section("Show these pages in full mode"), Note("Turn a page off to hide it from the task rail. Now Playing and Settings are always shown."));
            var descriptions = new Dictionary<string, string>
            {
                ["library"] = "Browse and search your music and videos",
                ["cdrip"] = "Copy music from audio CDs",
                ["burn"] = "Copy the playlist to a drive, phone or folder",
                ["radio"] = "Internet radio stations",
                ["info"] = "Details and cover of what's playing",
            };
            foreach (var (key, name) in AppSettings.Pages)
            {
                panel.Children.Add(Toggle(name, descriptions.GetValueOrDefault(key), !_settings.HiddenPages.Contains(key), shown =>
                {
                    _settings.HiddenPages.Remove(key);
                    if (!shown)
                        _settings.HiddenPages.Add(key);
                    _settings.Save();
                }));
            }

            panel.Children.Add(Section("Window"));
            panel.Children.Add(Row("Window style", Keymaps.WithKey("Full mode's frame; title bar", Pardeh.Core.Input.KeyCmd.TitleBar) + Keymaps.WithKey("; skin bars only", Pardeh.Core.Input.KeyCmd.BarsStyle),
                Choice(PlayerView.WindowStyles.Select(s => s.Name), (int)host.WindowStyle, i => host.SetWindowStyle(PlayerView.WindowStyles[i].Style), 230)));
            return panel;
        }

        private Control PlayerPage()
        {
            var looks = PlayerView.FullModeLooks;
            return Stack(
                Section("Player"),
                Toggle("Keep the player on top", "Stay above other windows", _settings.AlwaysOnTop, v => { _settings.AlwaysOnTop = v; _settings.Save(); }),
                Toggle("One Pardeh at a time", "Open files in the running player instead of starting another one", _settings.SingleInstance, v => { _settings.SingleInstance = v; _settings.Save(); }),
                Row("Start in", "The mode Pardeh opens in", Choice(["The mode I used last", "Full mode", "Skin mode"],
                    _settings.StartMode switch { "full" => 1, "skin" => 2, _ => 0 },
                    i => { _settings.StartMode = i switch { 1 => "full", 2 => "skin", _ => "last" }; _settings.Save(); })),
                Row("Full mode look", "Horizon, Luna, Fluent, Ember, Pardeh Ten and Pardeh Classic work without any Windows XP files",
                    Choice(looks.Select(l => l.Name), Math.Max(0, Array.FindIndex(looks, l => l.Key == _settings.FullModeLook)),
                        i => { _settings.FullModeLook = looks[i].Key; _settings.Save(); }, 260)));
        }

        private Control AppearancePage()
        {
            Control Look(string title, string description, bool value, Action<bool> set) => Toggle(title, description, value, v => { set(v); _settings.Save(); });
            return Stack(
                Section(Modern ? "Use the Windows 11 look for" : "Use the modern Windows 11 look for"),
                Note("Each item is independent. Items left off keep the classic Windows XP look. Skins themselves are not changed."),
                Look("Fonts", "Segoe UI instead of Tahoma in Pardeh's panes, menus and dialogs", _settings.ModernFont, v => _settings.ModernFont = v),
                Look("Title bar", "Shown with the menu bar in full mode", _settings.ModernTitleBar, v => _settings.ModernTitleBar = v),
                Look("Menu bar", "File, View, Play, Tools, Help", _settings.ModernMenuBar, v => _settings.ModernMenuBar = v),
                Look("Context menus and popups", "Right-click menus and skin popups", _settings.ModernMenus, v => _settings.ModernMenus = v),
                Look("Dialogs and settings", "Options, questions, Edit Info and this page", _settings.ModernDialogs, v => _settings.ModernDialogs = v),
                Look("Scrollbars and tooltips", "Classic dialogs and panes", _settings.ModernControls, v => _settings.ModernControls = v),
                Look("Task pane toolbars", "Media Library, Radio Tuner and other Pardeh panes", _settings.ModernToolbars, v => _settings.ModernToolbars = v),
                Look("Full-screen control bar", "The bar over video in full screen", _settings.ModernFullScreenBar, v => _settings.ModernFullScreenBar = v),
                Section("Window"),
                Toggle("Rounded window corners", "With the menu bar shown (Windows 11)", _settings.RoundedCorners, v => { _settings.RoundedCorners = v; _settings.Save(); }));
        }

        private Control LibraryPage()
        {
            var list = new ListBox { Height = 170, ItemsSource = library.Folders.ToList() };
            void Refresh() => list.ItemsSource = library.Folders.ToList();
            async void Add()
            {
                var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Add a folder to watch" });
                if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
                {
                    library.AddFolder(path);
                    Refresh();
                }
            }

            void Remove()
            {
                if (list.SelectedItem is string folder)
                {
                    library.RemoveFolder(folder);
                    Refresh();
                }
            }

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0),
                Children = { Btn("Add Folder...", Add, accent: true), Btn("Remove", Remove), Btn("Search Folders Now", () => _ = library.ScanAsync()) } };
            Control content = list, more = buttons;
            if (Modern)
            {
                content = new Border { Background = CardBack, BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(16),
                    Child = new StackPanel { Children = { list, buttons } } };
                more = new Panel();
            }

            return Stack(Section("Monitored folders"), Note("Pardeh keeps the library up to date with these folders, including changes while it runs."), content, more,
                Section("Fix Info"),
                Toggle("Look up song details online (MusicBrainz)", PlayerView.OnlineInfoPrivacy, _settings.FixInfoOnline, v => { _settings.FixInfoOnline = v; _settings.Save(); }),
                Toggle("Check the playing song online", PlayerView.CheckPlayingSummary, _settings.FixInfoCheckPlaying, v => { _settings.FixInfoCheckPlaying = v; _settings.Save(); }),
                Toggle("Keep a history of look-ups", PlayerView.HistorySummary, _settings.KeepLookupHistory, v => { _settings.KeepLookupHistory = v; _settings.Save(); }),
                Toggle("Take names in the database's script", "Off: a song with Persian names keeps them in Persian, and one with Latin names in Latin",
                    _settings.FixInfoDatabaseScript, v => { _settings.FixInfoDatabaseScript = v; _settings.Save(); }));
        }

        private Control AudioVideoPage()
        {
            var eq = player.Equalizer;
            var seconds = new NumericUpDown { Minimum = 0, Maximum = 10, Increment = 1, Value = (decimal)eq.CrossFadeSeconds, Width = 130, FormatString = "0 's'" };
            seconds.ValueChanged += (_, _) => eq.CrossFadeSeconds = (double)(seconds.Value ?? 0);
            return Stack(
                Section("Audio"),
                Toggle("Crossfading", "Blend the end of one track into the next", eq.CrossFade, v => eq.CrossFade = v),
                Row("Crossfade overlap", "How long the tracks overlap", seconds),
                Toggle("Auto Volume Leveling", "Keep loud and quiet tracks at a similar volume", eq.AutoVolume, v => eq.AutoVolume = v),
                Toggle("Bass and width enhancement", "The SRS WOW effects", eq.Enhanced, v => eq.Enhanced = v),
                Row("Quiet Mode", "Narrow the difference between loud and soft sounds",
                    Choice(["Off", "Medium difference", "Little difference"], eq.QuietMode, i => eq.QuietMode = i)),
                Row("Play speed", "Slower or faster without changing pitch",
                    Choice(MediaPlayer.RatePresets.Select(r => r.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "×"), Array.IndexOf(MediaPlayer.RatePresets, player.Rate), i => player.Rate = MediaPlayer.RatePresets[i], 120)),
                Section("Video"),
                Row("Video settings", "Brightness, contrast, hue and saturation are in View › Enhancements › Video Settings", Btn("Reset", player.Video.Reset)));
        }

        private static IFileAssociationService Associations => PlatformServices.Current.FileAssociations!;

        private Control FileTypesPage()
        {
            var chosen = Associations.Groups.ToDictionary(g => g.Group, _ => true);
            var status = Note(Associations.IsRegistered() ? "Pardeh is registered for file types." : "Pardeh is not registered yet.");
            var panel = Stack(Section("Open these with Pardeh"),
                Note("Windows lets you choose default apps yourself: Pardeh adds itself to \"Open with\" and to Default apps, then opens that page."));
            foreach (var group in Associations.Groups)
            {
                var extensions = string.Join(", ", group.Extensions.Take(8)) + (group.Extensions.Length > 8 ? " …" : string.Empty);
                panel.Children.Add(Modern
                    ? Toggle(group.Group, extensions, true, v => chosen[group.Group] = v)
                    : Toggle($"{group.Group} ({extensions})", null, true, v => chosen[group.Group] = v));
            }

            void Register()
            {
                var extensions = Associations.Groups.Where(g => chosen[g.Group]).SelectMany(g => g.Extensions).ToList();
                Associations.Register(extensions);
                status.Text = "Registered. In Default apps, pick Pardeh for the types you want.";
                Associations.OpenDefaultAppsSettings();
            }

            void Unregister()
            {
                Associations.Unregister();
                status.Text = "Pardeh is not registered.";
            }

            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 6),
                Children = { Btn("Register and open Default apps", Register, accent: true), Btn("Remove registration", Unregister) } });
            panel.Children.Add(status);
            return panel;
        }
    }
}

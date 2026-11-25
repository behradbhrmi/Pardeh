using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Pardeh.Core.Input;

namespace Pardeh.UI.Rendering;

/// <summary>The Keyboard page: the keymap profiles, and every command with its keys.</summary>
internal static partial class OptionsDialog
{
    private sealed partial class SettingsBuilder
    {
        private Control KeyboardPage() => new KeyboardEditor(this, owner).Root;

        /// <summary>
        /// The page keeps its own controls and redraws the parts that changed. Anything that needs an answer (a name, a
        /// confirmation, a key that is already taken) is asked in a bar above the list.
        /// </summary>
        private sealed class KeyboardEditor
        {
            private const string AllCategories = "All categories";

            private readonly SettingsBuilder _page;
            private readonly TopLevel _owner;
            private readonly KeymapManager _keys = Keymaps.Current;

            private readonly ComboBox _profiles = new() { MinWidth = 200 };
            private readonly TextBlock _profileNote;
            private readonly Button _rename, _delete, _resetAll;
            private readonly ContentControl _bar = new();
            private readonly TextBox _search = new() { Width = 220, PlaceholderText = "Search actions or keys" };
            private readonly ComboBox _category = new() { MinWidth = 170 };
            private readonly CheckBox _showMissing = new() { Content = "Show actions Pardeh can't do yet" };
            private readonly StackPanel _list = new() { Spacing = 0 };

            private Control? _capture;
            private KeyChord? _pressed; // "find by key": the key the list is filtered to
            private bool _updating;

            public KeyboardEditor(SettingsBuilder page, TopLevel owner)
            {
                _page = page;
                _owner = owner;
                _profileNote = page.Note(string.Empty);
                _rename = page.Btn("Rename...", () => AskName("New name", Selected.Name, name => _keys.Rename(Selected, name)));
                _delete = page.Btn("Delete", () => Confirm($"Delete the keymap \"{Selected.Name}\"?", "Delete", () => _keys.Delete(Selected)));
                _resetAll = page.Btn("Reset all keys", () => Confirm($"Put every key of \"{Selected.Name}\" back as it was?", "Reset", () => _keys.ResetAll(Selected)));

                _profiles.SelectionChanged += (_, _) =>
                {
                    if (_updating || _profiles.SelectedIndex < 0)
                        return;
                    CloseBar();
                    _keys.SetActive(_keys.Profiles[_profiles.SelectedIndex]);
                };
                _category.ItemsSource = new[] { AllCategories }.Concat(KeyCommandCatalog.All.Select(c => c.Category).Distinct()).ToList();
                _category.SelectedIndex = 0;
                _category.SelectionChanged += (_, _) => FillList();
                _search.TextChanged += (_, _) => { _pressed = null; FillList(); };
                _showMissing.IsCheckedChanged += (_, _) => FillList();

                var profileButtons = Wrap(
                    page.Btn("New empty", () => AskName("Name of the new keymap (it starts with no keys)", "My keys", name => _keys.SetActive(_keys.Create(name)))),
                    page.Btn("Duplicate", () => AskName("Name of the copy", Selected.Name + " copy", name => _keys.SetActive(_keys.Duplicate(Selected, name)))),
                    _rename, _delete, _resetAll,
                    page.Btn("Import...", () => _ = Import()),
                    page.Btn("Export...", () => _ = Export()));

                var filters = Wrap(_search, page.Btn("Find by key...", FindByKey), _category, _showMissing);
                filters.Margin = new Thickness(0, 8, 0, 4);

                Root = page.Stack(
                    page.Section("Keymap"),
                    page.Row("Keymap in use", "A built-in keymap stays as it is: changing one of its keys makes your own copy", _profiles),
                    profileButtons,
                    _profileNote,
                    page.Section("Keys"),
                    filters,
                    _bar,
                    _list);
                Root.AttachedToVisualTree += (_, _) => { _keys.Changed += Refresh; Refresh(); };
                Root.DetachedFromVisualTree += (_, _) => _keys.Changed -= Refresh;
                Refresh();
            }

            public Control Root { get; }

            private KeymapProfile Selected => _keys.Active;

            private static WrapPanel Wrap(params Control[] children)
            {
                var panel = new WrapPanel { Orientation = Orientation.Horizontal };
                foreach (var child in children)
                {
                    child.Margin = new Thickness(0, 0, 8, 6);
                    child.VerticalAlignment = VerticalAlignment.Center;
                    panel.Children.Add(child);
                }

                return panel;
            }

            private void Refresh()
            {
                _updating = true;
                _profiles.ItemsSource = _keys.Profiles.Select(p => p.IsBuiltIn ? p.Name + " (built-in)" : p.Name).ToList();
                _profiles.SelectedIndex = _keys.Profiles.ToList().IndexOf(Selected);
                _updating = false;

                var profile = Selected;
                _rename.IsEnabled = _delete.IsEnabled = !profile.IsBuiltIn;
                _resetAll.IsEnabled = _keys.HasChanges(profile);
                var from = _keys.Find(profile.BaseId)?.Name;
                _profileNote.Text = _keys.Problem
                    ?? (profile.IsBuiltIn ? "Built-in." : from is null ? "Your keymap." : $"Your keymap, based on {from}. Changed actions are marked.");
                FillList();
            }

            // ---- The list -------------------------------------------------------------------------

            private void FillList()
            {
                var bindings = _keys.Resolve(Selected);
                var text = _search.Text?.Trim() ?? string.Empty;
                var category = _category.SelectedItem as string;
                var searching = text.Length > 0 || _pressed is not null;
                _list.Children.Clear();
                var heading = string.Empty;

                foreach (var command in KeyCommandCatalog.All)
                {
                    var chords = bindings.GetValueOrDefault(command.Id) ?? [];
                    if (_pressed is { } pressed)
                    {
                        if (!chords.Contains(pressed))
                            continue;
                    }
                    else
                    {
                        if (!command.Supported && _showMissing.IsChecked != true)
                            continue;
                        if (category != AllCategories && command.Category != category && !searching)
                            continue;
                        if (text.Length > 0
                            && !command.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                            && !command.Category.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                            && !chords.Any(c => c.ToDisplayString().Contains(text, StringComparison.OrdinalIgnoreCase)))
                            continue;
                    }

                    if (command.Category != heading)
                    {
                        heading = command.Category;
                        _list.Children.Add(_page.Section(heading));
                    }

                    _list.Children.Add(CommandRow(command, chords));
                }

                if (_list.Children.Count == 0)
                    _list.Children.Add(_page.Note(_pressed is { } key ? $"{key.ToDisplayString()} does nothing in this keymap." : "No action matches."));
            }

            private Control CommandRow(KeyCommand command, IReadOnlyList<KeyChord> chords)
            {
                var modified = _keys.IsModified(Selected, command.Id);
                var name = new TextBlock
                {
                    Text = command.Name + (modified ? "  • changed" : string.Empty) + (command.Supported ? string.Empty : "  (not in Pardeh yet)"),
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = !command.Supported ? Quiet : Text,
                    FontWeight = modified ? FontWeight.SemiBold : FontWeight.Normal,
                };

                var keys = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                foreach (var bound in chords)
                {
                    var chord = bound;
                    var remove = Small("✕", $"Remove {chord.ToDisplayString()}");
                    remove.Click += (_, _) => Change(profile => _keys.RemoveChord(profile, command.Id, chord));
                    keys.Children.Add(new Border
                    {
                        BorderBrush = Quiet,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(7, 0, 0, 0),
                        Margin = new Thickness(0, 2, 6, 2),
                        Child = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 4,
                            Children = { new TextBlock { Text = chord.ToDisplayString(), VerticalAlignment = VerticalAlignment.Center, Foreground = Text }, remove },
                        },
                    });
                }

                var add = Small("+ Key", $"Add a key for \"{command.Name}\"");
                add.Click += (_, _) => Capture($"Press the key for \"{command.Name}\"", chord => Assign(command, chord));
                keys.Children.Add(add);
                if (modified && Selected.BaseId is not null)
                {
                    var reset = Small("Reset", "Back to the keys of the keymap this one is based on");
                    reset.Click += (_, _) => Change(profile => _keys.ResetCommand(profile, command.Id));
                    keys.Children.Add(reset);
                }

                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*"), MinHeight = 30 };
                Grid.SetColumn(keys, 2);
                keys.VerticalAlignment = VerticalAlignment.Center;
                grid.Children.AddRange([name, keys]);
                return new Border { BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(2, 2), Child = grid };
            }

            private IBrush Text => _page.Modern ? _page.Ink : Brushes.Black;

            private IBrush Quiet => _page.Modern ? _page.SubInk : Brushes.DimGray;

            private IBrush Line => new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));

            private static Button Small(string text, string tip)
            {
                var button = new Button { Content = text, MinWidth = 0, MinHeight = 0, Padding = new Thickness(7, 2), Margin = new Thickness(0, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center };
                ToolTip.SetTip(button, tip);
                return button;
            }

            /// <summary>Changes the profile in use; a built-in one is copied first.</summary>
            private void Change(Action<KeymapProfile> change) => change(_keys.Editable(Selected));

            private void Assign(KeyCommand command, KeyChord chord)
            {
                var owner = _keys.OwnerOf(Selected, chord);
                if (owner == command.Id)
                    return;
                if (owner is null)
                {
                    Change(profile => _keys.AddChord(profile, command.Id, chord));
                    return;
                }

                var other = KeyCommandCatalog.Find(owner)?.Name ?? owner;
                Confirm($"{chord.ToDisplayString()} is used by \"{other}\". Move it to \"{command.Name}\"?", "Move",
                    () => Change(profile => _keys.AddChord(profile, command.Id, chord)));
            }

            // ---- The bar: capture a key, ask a name, confirm --------------------------------------

            private void ShowBar(Control content)
            {
                _capture = null;
                _bar.Content = new Border
                {
                    BorderBrush = _page.AccentBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 8),
                    Margin = new Thickness(0, 0, 0, 6),
                    Child = content,
                };
                _bar.BringIntoView();
            }

            private void CloseBar()
            {
                _capture = null;
                _bar.Content = null;
            }

            /// <summary>
            /// Waits for one key press. The box takes every key before the dialog or the player can act on it, so Space,
            /// Enter, Tab and the player's own keys can all be chosen.
            /// </summary>
            private void Capture(string question, Action<KeyChord> taken)
            {
                var box = new Border
                {
                    Focusable = true,
                    Background = Brushes.Transparent,
                    Child = new TextBlock { Text = question + " (Esc cancels)", Foreground = Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center },
                };
                box.AddHandler(InputElement.KeyDownEvent, (_, e) =>
                {
                    e.Handled = true;
                    if (Keymaps.ChordOf(e) is not { } chord)
                        return;
                    CloseBar();
                    if (chord != new KeyChord("Escape"))
                        taken(chord);
                }, RoutingStrategies.Tunnel);
                var cancel = _page.Btn("Cancel", CloseBar);
                var dock = new DockPanel { Children = { cancel, box } };
                DockPanel.SetDock(cancel, Dock.Right);
                ShowBar(dock);
                _capture = box;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (ReferenceEquals(_capture, box)) box.Focus(); });
            }

            private void FindByKey() => Capture("Press a key to see what it does", chord =>
            {
                _search.Text = string.Empty;
                _pressed = chord;
                FillList();
                var done = _page.Btn("Show all keys", () => { _pressed = null; CloseBar(); FillList(); });
                var label = new TextBlock { Text = "Showing: " + chord.ToDisplayString(), Foreground = Text, VerticalAlignment = VerticalAlignment.Center };
                var dock = new DockPanel { Children = { done, label } };
                DockPanel.SetDock(done, Dock.Right);
                ShowBar(dock);
            });

            private void AskName(string question, string initial, Action<string> answered)
            {
                var box = new TextBox { Text = initial, MinWidth = 200, MaxLength = 60 };
                void Done()
                {
                    var name = box.Text?.Trim();
                    CloseBar();
                    if (!string.IsNullOrEmpty(name))
                        answered(name);
                }

                box.AddHandler(InputElement.KeyDownEvent, (_, e) =>
                {
                    if (e.Key is not (Key.Enter or Key.Escape))
                        return;
                    e.Handled = true; // not the dialog's OK button
                    if (e.Key == Key.Enter)
                        Done();
                    else
                        CloseBar();
                }, RoutingStrategies.Tunnel);
                ShowBar(Wrap(new TextBlock { Text = question, Foreground = Text }, box, _page.Btn("OK", Done, accent: true), _page.Btn("Cancel", CloseBar)));
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { box.Focus(); box.SelectAll(); });
            }

            private void Confirm(string question, string action, Action confirmed) =>
                ShowBar(Wrap(
                    new TextBlock { Text = question, Foreground = Text, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 },
                    _page.Btn(action, () => { CloseBar(); confirmed(); }, accent: true),
                    _page.Btn("Cancel", CloseBar)));

            private void Say(string message)
            {
                var close = _page.Btn("OK", CloseBar);
                ShowBar(Wrap(new TextBlock { Text = message, Foreground = Text, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 }, close));
            }

            // ---- Files ----------------------------------------------------------------------------

            private static readonly FilePickerFileType KeymapFiles = new("Pardeh keymaps") { Patterns = ["*.pardeh-keys.json", "*.json"] };

            private async Task Import()
            {
                var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import a keymap", FileTypeFilter = [KeymapFiles] });
                if (files.Count == 0)
                    return;
                try
                {
                    await using var stream = await files[0].OpenReadAsync();
                    using var reader = new StreamReader(stream);
                    var profile = _keys.Import(await reader.ReadToEndAsync());
                    _keys.SetActive(profile);
                    Say($"\"{profile.Name}\" was imported and is now in use.");
                }
                catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
                {
                    Say(ex.Message);
                }
            }

            private async Task Export()
            {
                var profile = Selected;
                var name = string.Concat(profile.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export the keymap",
                    SuggestedFileName = name + ".pardeh-keys.json",
                    FileTypeChoices = [KeymapFiles],
                });
                if (file is null)
                    return;
                try
                {
                    await using var stream = await file.OpenWriteAsync();
                    stream.SetLength(0);
                    await using var writer = new StreamWriter(stream);
                    await writer.WriteAsync(_keys.Export(profile));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Say("The keymap could not be saved: " + ex.Message);
                }
            }
        }
    }
}

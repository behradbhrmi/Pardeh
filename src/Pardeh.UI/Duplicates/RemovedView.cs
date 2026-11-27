using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Pardeh.Core.Changes;
using Pardeh.UI.CleanupViews;

namespace Pardeh.UI.DuplicateViews;

/// <summary>What the "Removed files" screen asks the app to do.</summary>
internal sealed record RemovedContext
{
    /// <summary>The files waiting in the holding folders.</summary>
    public Func<Task<IReadOnlyList<HeldFile>>> List { get; init; } = () => Task.FromResult<IReadOnlyList<HeldFile>>([]);

    public Func<HeldFile, Task> Restore { get; init; } = _ => Task.CompletedTask;

    /// <summary>Asks, then deletes these for good.</summary>
    public Func<IReadOnlyList<HeldFile>, Task> Delete { get; init; } = _ => Task.CompletedTask;

    /// <summary>Where else removed files may be (the system's own trash).</summary>
    public string TrashNote { get; init; } = string.Empty;
}

/// <summary>
/// The Pardeh holding folder: what waits there and how much room it takes. Files are restored to where they were, never
/// over another file. Space is freed only by emptying it here, after a question; nothing is emptied by itself.
/// </summary>
internal sealed class RemovedView : UserControl
{
    private readonly CleanupInk _ink;
    private readonly RemovedContext _context;
    private readonly StackPanel _content = new() { Spacing = 8 };
    private readonly Button _empty;
    private IReadOnlyList<HeldFile> _held = [];
    private bool _working;

    public RemovedView(CleanupInk ink, RemovedContext context, Action close)
    {
        _ink = ink;
        _context = context;
        _empty = ink.Button("Empty...", () => _ = Do(() => context.Delete(_held)));
        var buttons = DuplicateText.Buttons(_empty, ink.Cancels(ink.Button("Close", close)));
        var page = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 8, Children = { ink.Scroller(_content), buttons } };
        Grid.SetRow(buttons, 1);
        Content = page;
        _empty.IsEnabled = false;
        _content.Children.Add(ink.Label("Looking...", colour: ink.Sub));
        _ = Fill();
    }

    private async Task Do(Func<Task> work)
    {
        if (_working)
            return;
        _working = true;
        try
        {
            await work();
            await Fill();
        }
        finally
        {
            _working = false;
        }
    }

    public async Task Fill()
    {
        _held = await _context.List();
        _content.Children.Clear();
        var total = _held.Sum(h => h.Size);
        _content.Children.Add(_ink.Label(_held.Count == 0
            ? "The Pardeh holding folder is empty."
            : $"{DuplicateText.Count(_held.Count, "file waits", "files wait")} in the Pardeh holding folder, taking {DuplicateText.Size(total)}.", 14, bold: true));
        if (_context.TrashNote.Length > 0)
            _content.Children.Add(_ink.Label(_context.TrashNote, 12, colour: _ink.Sub));
        foreach (var held in _held.Reverse().Take(200))
        {
            var name = _ink.Label(held.Name, bold: true);
            name.MaxLines = 2;
            var where = _ink.Label(string.Join(DuplicateText.Dot, DuplicateText.Size(held.Size),
                "removed " + held.HeldUtc.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture), "from " + DuplicateText.Folder(held.OriginalSource)),
                12, colour: _ink.Sub);
            var file = held;
            var buttons = DuplicateText.Buttons(_ink.Button("Restore", () => _ = Do(() => _context.Restore(file))),
                _ink.Button("Delete...", () => _ = Do(() => _context.Delete([file]))));
            _content.Children.Add(_ink.Boxed(new StackPanel { Spacing = 4, Children = { name, where, buttons } }));
        }

        if (_held.Count > 200)
            _content.Children.Add(_ink.Label($"and {_held.Count - 200} more.", 12, colour: _ink.Sub));
        _empty.IsEnabled = _held.Count > 0;
        _empty.Content = _held.Count > 0 ? $"Empty ({DuplicateText.Size(total)})..." : "Empty...";
    }
}

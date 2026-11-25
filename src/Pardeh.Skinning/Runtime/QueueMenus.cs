using Pardeh.Core.Library;
using Pardeh.Core.Playback;

namespace Pardeh.Skinning.Runtime;

/// <summary>
/// The queue menus of the phone looks: sorting and the whole-queue operations (reverse, shuffle now, remove duplicates, clear)
/// and the filter menu (by text, favourites only). Shown through the host's popup, so they take the look of the app's menus.
/// None of them restarts playback: the playing item keeps playing and keeps its place in the queue.
/// </summary>
public static class QueueMenus
{
    private static readonly SortField[] Fields = [SortField.Title, SortField.Artist, SortField.Album, SortField.Duration, SortField.Year, SortField.Default];

    private static string FieldLabel(SortField field) => field == SortField.Default ? "Order added" : SortSpec.Label(field);

    /// <summary>Sort by ..., reverse, shuffle now, remove duplicates, clear.</summary>
    public static void ShowSortMenu(SkinSession session, SkinView view)
    {
        var list = session.Player.CurrentPlaylist;
        var items = new List<string>();
        var actions = new List<Action>();
        var current = list.SortedBy;
        foreach (var field in Fields)
        {
            var field_ = field;
            var active = current is { } sorted && sorted.Field == field;
            var direction = active ? (current!.Value.Descending ? " (descending)" : " (ascending)") : string.Empty;
            items.Add(field == SortField.Default ? $"Order added{direction}" : $"Sort by {FieldLabel(field)}{direction}");
            actions.Add(() => list.Sort(new SortSpec(field_, active && !current!.Value.Descending)));
        }

        items.Add("Reverse order");
        actions.Add(list.Reverse);
        items.Add("Shuffle now");
        actions.Add(list.Randomize);
        items.Add("Remove duplicates");
        actions.Add(() =>
        {
            var removed = list.RemoveDuplicates();
            session.Host.Log($"Queue: {removed} duplicate(s) removed");
        });
        items.Add("Clear the queue...");
        actions.Add(() => ConfirmClear(session, view));
        // Moving around a long queue: the playing song, the first, the last (the list also has buttons for these).
        if (FindQueueList(view.Root) is { } queueList && list.Count > 0)
        {
            items.Add("Go to now playing");
            actions.Add(queueList.ScrollToCurrent);
            items.Add("Jump to the first song");
            actions.Add(queueList.ScrollToTop);
            items.Add("Jump to the last song");
            actions.Add(queueList.ScrollToBottom);
        }

        session.Host.ShowPopup(view, items, -1, choice =>
        {
            if (choice >= 0 && choice < actions.Count)
            {
                actions[choice]();
                view.Invalidate();
            }
        });
    }

    private static Elements.PlaylistElement? FindQueueList(Elements.SkinElement element)
    {
        if (element is Elements.PlaylistElement { Visible: true } found)
            return found;
        foreach (var child in element.Children)
            if (FindQueueList(child) is { } nested)
                return nested;
        return null;
    }

    private static void ConfirmClear(SkinSession session, SkinView view)
    {
        var count = session.Player.CurrentPlaylist.Count;
        if (count == 0)
            return;
        // Shown after the first menu has closed.
        session.Host.ShowPopup(view, [$"Yes, clear all {count} songs from the queue", "No, keep the queue"], -1, choice =>
        {
            if (choice != 0)
                return;
            session.Player.Stop();
            session.Player.CurrentPlaylist.Clear();
            view.Invalidate();
        });
    }

    /// <summary>Filter by text, favourites only, clear the filter.</summary>
    public static void ShowFilterMenu(SkinSession session, SkinView view)
    {
        var filter = session.Player.CurrentPlaylist.Filter;
        var items = new List<string> { filter.Text.Length > 0 ? $"Filter by text... (\"{filter.Text}\")" : "Filter by text...", filter.FavoritesOnly ? "Favourites only (on)" : "Favourites only" };
        if (filter.IsActive)
            items.Add("Show everything");
        session.Host.ShowPopup(view, items, -1, choice =>
        {
            switch (choice)
            {
                case 0:
                    session.Host.RunCommand("prompt", ["Show queue rows containing:", filter.Text, (Action<string>)(text => { filter.Text = text; view.Invalidate(); })]);
                    break;
                case 1:
                    filter.FavoritesOnly = !filter.FavoritesOnly;
                    break;
                case 2:
                    filter.Clear();
                    break;
            }

            view.Invalidate();
        });
    }
}

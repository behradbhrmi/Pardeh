using System.Text.Json;
using Pardeh.Core.Playback;

namespace Pardeh.UI;

/// <summary>
/// The queue of the last session (the items, the one that was current and where it was), saved as <c>queue.json</c> in the data folder
/// and put back at start-up with the track loaded and paused, so the app does not open empty.
/// </summary>
internal sealed class QueueStore
{
    private static readonly string FilePath = Path.Combine(AppPaths.DataDirectory, "queue.json");

    public sealed record Entry(string Source, string Name, double Duration, Dictionary<string, string> Attributes);

    public sealed record State(List<Entry> Items, int Index, double Position);

    /// <summary>
    /// The saved queue while the app has none yet: when a change to a file is finished at start-up, <c>queue.json</c> follows
    /// it before it is restored.
    /// </summary>
    public sealed class SavedQueue : Pardeh.Core.Changes.IQueueLink
    {
        public void Relink(string oldSource, string newSource, MediaItem fresh) => Rewrite(oldSource, entry =>
            new Entry(newSource, fresh.Name, fresh.Duration > TimeSpan.Zero ? fresh.Duration.TotalSeconds : entry.Duration, new Dictionary<string, string>(fresh.Attributes)));

        public void Remove(string source) => Rewrite(source, _ => null);

        private static void Rewrite(string source, Func<Entry, Entry?> change)
        {
            try
            {
                if (!File.Exists(FilePath) || JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath)) is not { } state)
                    return;
                var items = new List<Entry>();
                var index = state.Index;
                var changed = false;
                for (var i = 0; i < state.Items.Count; i++)
                {
                    var entry = state.Items[i];
                    var now = string.Equals(entry.Source, source, StringComparison.OrdinalIgnoreCase) ? change(entry) : entry;
                    changed |= !ReferenceEquals(now, entry);
                    if (now is not null)
                        items.Add(now);
                    else if (i < state.Index)
                        index--;
                }

                if (changed)
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(state with { Items = items, Index = Math.Clamp(index, 0, Math.Max(0, items.Count - 1)) }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }
    }

    private readonly MediaPlayer _player;
    private string? _lastJson;

    public QueueStore(MediaPlayer player) => _player = player;

    /// <summary>Writes the queue when it differs from what is on disk (cheap when nothing changed).</summary>
    public void Save()
    {
        try
        {
            var playlist = _player.CurrentPlaylist;
            if (playlist.Count == 0)
                return; // a cleared queue keeps the last saved one only until the user plays something else; never overwrite with "nothing"
            // Addresses that carry a login or an access token are never written to disk.
            var current = Math.Max(0, playlist.CurrentIndex);
            var index = current - playlist.Items.Take(current).Count(i => NetworkMedia.HasSecret(i.Source));
            var keptCurrent = current >= playlist.Count || !NetworkMedia.HasSecret(playlist.Items[current].Source);
            var items = playlist.Items
                .Where(i => !NetworkMedia.HasSecret(i.Source))
                .Select(i => new Entry(i.Source, i.Name, i.Duration.TotalSeconds, new Dictionary<string, string>(i.Attributes)))
                .ToList();
            if (items.Count == 0)
                return;
            var position = _player.CurrentMedia is null || !keptCurrent ? 0 : _player.CurrentPosition;
            var json = JsonSerializer.Serialize(new State(items, Math.Clamp(index, 0, items.Count - 1), position));
            if (json == _lastJson)
                return;
            _lastJson = json;
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A queue that cannot be saved is not worth interrupting playback for.
        }
    }

    /// <summary>Loads the saved queue into the player, paused. Returns false when there is none (or its files are gone).</summary>
    public bool Restore()
    {
        try
        {
            if (!File.Exists(FilePath) || JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath)) is not { Items.Count: > 0 } state)
                return false;
            var items = new List<MediaItem>();
            var index = state.Index;
            for (var i = 0; i < state.Items.Count; i++)
            {
                var entry = state.Items[i];
                var isUrl = entry.Source.Contains("://", StringComparison.Ordinal) && !entry.Source.StartsWith("file://", StringComparison.Ordinal);
                if (!isUrl && !entry.Source.StartsWith("content://", StringComparison.Ordinal) && !File.Exists(entry.Source))
                {
                    if (i < state.Index)
                        index--;
                    continue; // the file is gone
                }

                var item = new MediaItem(entry.Source) { Name = entry.Name, Duration = TimeSpan.FromSeconds(entry.Duration) };
                foreach (var (key, value) in entry.Attributes)
                    item.Attributes[key] = value;
                items.Add(item);
            }

            if (items.Count == 0)
                return false;
            _lastJson = null;
            _player.RestoreQueue(items, Math.Max(0, index), state.Position);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}

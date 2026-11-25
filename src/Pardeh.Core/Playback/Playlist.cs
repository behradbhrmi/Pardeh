namespace Pardeh.Core.Playback;

/// <summary>An ordered list of media items with a current position, shuffle and repeat.</summary>
public sealed class Playlist
{
    private readonly List<MediaItem> _items = [];
    private readonly Random _random = new();
    private List<int>? _shuffleOrder;

    public Playlist(string name) => Name = name;

    public string Name { get; set; }

    public IReadOnlyList<MediaItem> Items => _items;

    public int Count => _items.Count;

    /// <summary>Index of the current item, or -1.</summary>
    public int CurrentIndex { get; private set; } = -1;

    public MediaItem? Current => CurrentIndex >= 0 && CurrentIndex < _items.Count ? _items[CurrentIndex] : null;

    public bool Shuffle
    {
        get => _shuffleOrder is not null;
        set
        {
            if (value == Shuffle)
                return;
            _shuffleOrder = value ? BuildShuffleOrder() : null;
        }
    }

    public bool Loop { get; set; }

    public event EventHandler? Changed;

    /// <summary>Counts every change of the items or their order (lets a view know its cached rows are stale).</summary>
    public int Version { get; private set; }

    /// <summary>The sort the queue was last put in by <see cref="Sort"/> (null once it changed by adding, moving or shuffling it).</summary>
    public Library.SortSpec? SortedBy { get; private set; }

    /// <summary>A display-only filter (text, favourites only) for lists that show this queue; it never changes the order or the items.</summary>
    public QueueFilter Filter { get; } = new();

    private void Notify()
    {
        Version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        SortedBy = null;
        _items.Clear();
        _addedOrder.Clear();
        CurrentIndex = -1;
        if (_shuffleOrder is not null)
            _shuffleOrder = [];
        Notify();
    }

    public void Add(MediaItem item)
    {
        SortedBy = null;
        _items.Add(item);
        AddedOrderOf(item);
        if (_shuffleOrder is not null)
            _shuffleOrder.Insert(_random.Next(_shuffleOrder.Count + 1), _items.Count - 1);
        Notify();
    }

    /// <summary>Removes an item; the current item stays current (or nothing is, if it was removed).</summary>
    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count)
            return;
        _items.RemoveAt(index);
        if (CurrentIndex == index)
            CurrentIndex = -1;
        else if (CurrentIndex > index)
            CurrentIndex--;
        if (_shuffleOrder is not null)
            _shuffleOrder = _shuffleOrder.Where(i => i != index).Select(i => i > index ? i - 1 : i).ToList();
        Notify();
    }

    /// <summary>Moves an item (Move Up / Down); the current item stays current.</summary>
    public void Move(int from, int to)
    {
        if (from < 0 || from >= _items.Count || to < 0 || to >= _items.Count || from == to)
            return;
        SortedBy = null;
        var item = _items[from];
        var current = Current;
        _items.RemoveAt(from);
        _items.Insert(to, item);
        if (current is not null)
            CurrentIndex = _items.IndexOf(current);
        if (_shuffleOrder is not null)
            _shuffleOrder = BuildShuffleOrder();
        Notify();
    }

    // ---- Whole-queue operations (phone queue menu). None of them restarts playback: the current item stays the current item. ----

    private long _addCounter;

    /// <summary>The order the items were added in (for <see cref="RestoreAddedOrder"/>); an item gets its number when first seen.</summary>
    private readonly Dictionary<MediaItem, long> _addedOrder = new(ReferenceEqualityComparer.Instance);

    private long AddedOrderOf(MediaItem item)
    {
        if (!_addedOrder.TryGetValue(item, out var order))
            _addedOrder[item] = order = ++_addCounter;
        return order;
    }

    /// <summary>
    /// Puts the items in the order <paramref name="newOrder"/> lists them (old indexes). The current item is followed to its new
    /// place, the shuffle order is built again and <see cref="Changed"/> fires once.
    /// </summary>
    private void Reorder(IReadOnlyList<int> newOrder, Library.SortSpec? sortedBy = null)
    {
        if (newOrder.Count != _items.Count)
            return;
        SortedBy = sortedBy;
        var moved = newOrder.Select(i => _items[i]).ToList();
        var current = CurrentIndex >= 0 ? newOrder.ToList().IndexOf(CurrentIndex) : -1;
        _items.Clear();
        _items.AddRange(moved);
        CurrentIndex = current;
        if (_shuffleOrder is not null)
            _shuffleOrder = BuildShuffleOrder();
        Notify();
    }

    /// <summary>Stable sort by a field (<see cref="Library.SortField.Default"/> restores the order the items were added in).</summary>
    public void Sort(Library.SortSpec sort)
    {
        if (_items.Count < 2)
            return;
        if (sort.Field == Library.SortField.Default)
        {
            RestoreAddedOrder(sort.Descending);
            return;
        }

        var keys = _items.Select(Library.TrackKey.Of).ToArray();
        var order = Enumerable.Range(0, _items.Count)
            .OrderBy(i => i, Comparer<int>.Create((x, y) => Library.TrackSort.Compare(keys[x], keys[y], sort))).ToList();
        Reorder(order, sort);
    }

    /// <summary>The order the items were added to the queue in (or its reverse).</summary>
    public void RestoreAddedOrder(bool reverse = false)
    {
        var order = Enumerable.Range(0, _items.Count).OrderBy(i => AddedOrderOf(_items[i]) * (reverse ? -1 : 1)).ToList();
        Reorder(order, new Library.SortSpec(Library.SortField.Default, reverse));
    }

    /// <summary>Turns the queue around (the current item stays current).</summary>
    public void Reverse() =>
        Reorder(Enumerable.Range(0, _items.Count).Reverse().ToList(), SortedBy is { } sorted ? sorted with { Descending = !sorted.Descending } : null);

    /// <summary>
    /// Shuffles the queue once (the order itself changes; <see cref="Shuffle"/> is a mode and stays as it is). The current item
    /// moves to the front so the rest is "up next".
    /// </summary>
    public void Randomize()
    {
        if (_items.Count < 2)
            return;
        var order = Enumerable.Range(0, _items.Count).ToList();
        for (var i = order.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        if (CurrentIndex >= 0)
        {
            order.Remove(CurrentIndex);
            order.Insert(0, CurrentIndex);
        }

        Reorder(order);
    }

    /// <summary>
    /// Removes the same song listed twice (same <see cref="Library.TrackSort.IdentityOf"/>), keeping the first, or the one that is
    /// playing. Returns how many were removed.
    /// </summary>
    public int RemoveDuplicates()
    {
        var firstOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var drop = new HashSet<int>();
        for (var i = 0; i < _items.Count; i++)
        {
            var key = Library.TrackSort.IdentityOf(_items[i].Source);
            if (!firstOf.TryGetValue(key, out var first))
            {
                firstOf[key] = i;
            }
            else if (i == CurrentIndex)
            {
                drop.Add(first);
                firstOf[key] = i;
            }
            else
            {
                drop.Add(i);
            }
        }

        if (drop.Count == 0)
            return 0;
        var current = CurrentIndex >= 0 ? _items[CurrentIndex] : null;
        var kept = Enumerable.Range(0, _items.Count).Where(i => !drop.Contains(i)).Select(i => _items[i]).ToList();
        _items.Clear();
        _items.AddRange(kept);
        CurrentIndex = current is null ? -1 : kept.FindIndex(m => ReferenceEquals(m, current));
        if (_shuffleOrder is not null)
            _shuffleOrder = BuildShuffleOrder();
        Notify();
        return drop.Count;
    }

    public void SetCurrent(int index)
    {
        CurrentIndex = index >= 0 && index < _items.Count ? index : -1;
    }

    public bool HasNext => NextIndex() >= 0;

    /// <summary>The item <see cref="MoveNext"/> would move to, or null (the shuffle order is fixed, so this holds under shuffle too).</summary>
    public MediaItem? PeekNext() => NextIndex() is >= 0 and var next ? _items[next] : null;

    public bool HasPrevious => PreviousIndex() >= 0;

    /// <summary>Moves to the next item. Returns false at the end of a non-looping playlist.</summary>
    public bool MoveNext()
    {
        var next = NextIndex();
        if (next < 0)
            return false;
        CurrentIndex = next;
        return true;
    }

    public bool MovePrevious()
    {
        var previous = PreviousIndex();
        if (previous < 0)
            return false;
        CurrentIndex = previous;
        return true;
    }

    private int NextIndex()
    {
        if (_items.Count == 0)
            return -1;
        var order = Order();
        var position = order.IndexOf(CurrentIndex);
        if (position + 1 < order.Count)
            return order[position + 1];
        return Loop ? order[0] : -1;
    }

    private int PreviousIndex()
    {
        if (_items.Count == 0)
            return -1;
        var order = Order();
        var position = order.IndexOf(CurrentIndex);
        if (position > 0)
            return order[position - 1];
        return Loop ? order[^1] : -1;
    }

    private List<int> Order() => _shuffleOrder ?? Enumerable.Range(0, _items.Count).ToList();

    private List<int> BuildShuffleOrder()
    {
        var order = Enumerable.Range(0, _items.Count).ToList();
        for (var i = order.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        // Keep the current item first so toggling shuffle doesn't jump.
        if (CurrentIndex >= 0)
        {
            order.Remove(CurrentIndex);
            order.Insert(0, CurrentIndex);
        }

        return order;
    }
}

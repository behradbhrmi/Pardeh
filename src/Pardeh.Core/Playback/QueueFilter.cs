namespace Pardeh.Core.Playback;

/// <summary>
/// What a list showing the queue hides: rows that do not contain the text (title, artist, album) and, optionally, rows that are not
/// favourites. Display only: the queue itself, its order and the playing item never change. Matching ignores case and kana /
/// width differences, and Arabic yeh / kaf variants and Persian digits are folded (see <see cref="Library.TrackSort.SortText"/>).
/// </summary>
public sealed class QueueFilter
{
    private string _text = string.Empty;
    private bool _favoritesOnly;

    public string Text
    {
        get => _text;
        set
        {
            value = (value ?? string.Empty).Trim();
            if (value == _text)
                return;
            _text = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool FavoritesOnly
    {
        get => _favoritesOnly;
        set
        {
            if (value == _favoritesOnly)
                return;
            _favoritesOnly = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsActive => _text.Length > 0 || _favoritesOnly;

    public event EventHandler? Changed;

    public void Clear()
    {
        if (!IsActive)
            return;
        _text = string.Empty;
        _favoritesOnly = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Does the item pass the filter?</summary>
    public bool Matches(MediaItem item, bool isFavorite)
    {
        if (_favoritesOnly && !isFavorite)
            return false;
        return _text.Length == 0 || Contains(item.Name, _text) || Contains(item.GetAttribute("Author"), _text)
            || Contains(item.GetAttribute("Album"), _text);
    }

    /// <summary>Case-insensitive "contains" on folded text (culture-independent).</summary>
    public static bool Contains(string? haystack, string needle)
    {
        if (string.IsNullOrEmpty(haystack))
            return false;
        return Fold(haystack).Contains(Fold(needle), StringComparison.OrdinalIgnoreCase);
    }

    private static string Fold(string text) => Library.TrackSort.Fold(text);
}

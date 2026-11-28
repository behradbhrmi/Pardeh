using Pardeh.Core.Library;

namespace Pardeh.Core.Playback;

public sealed partial class MediaPlayer
{
    private FavoritesStore? _favorites;

    /// <summary>The tracks marked with a heart (set by the host; null: favourites are not available).</summary>
    public FavoritesStore? Favorites
    {
        get => _favorites;
        set
        {
            if (ReferenceEquals(_favorites, value))
                return;
            if (_favorites is not null)
                _favorites.Changed -= OnFavoritesChanged;
            _favorites = value;
            if (_favorites is not null)
                _favorites.Changed += OnFavoritesChanged;
            FavoritesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>A favourite was added or removed (marshalled to the player's thread like the other events).</summary>
    public event EventHandler? FavoritesChanged;

    private void OnFavoritesChanged(object? sender, EventArgs e)
    {
        if (_context is null || ReferenceEquals(SynchronizationContext.Current, _context))
            FavoritesChanged?.Invoke(this, EventArgs.Empty);
        else
            _context.Post(_ => FavoritesChanged?.Invoke(this, EventArgs.Empty), null);
    }

    public bool IsFavorite(MediaItem? item) => item is not null && _favorites?.IsFavorite(item.Source) == true;

    /// <summary>Is the playing track a favourite.</summary>
    public bool CurrentIsFavorite => IsFavorite(CurrentMedia);

    /// <summary>Hearts or un-hearts an item (the playing track when null). Returns the new state.</summary>
    public bool ToggleFavorite(MediaItem? item = null)
    {
        item ??= CurrentMedia;
        if (item is null || _favorites is null || RefuseSecret(item))
            return false;
        return _favorites.Toggle(item.Source, item.Name, item.GetAttribute("Author"));
    }

    /// <summary>Sets the favourite state of an item (the playing track when null).</summary>
    public void SetFavorite(bool favorite, MediaItem? item = null)
    {
        item ??= CurrentMedia;
        if (item is not null && !RefuseSecret(item))
            _favorites?.Set(item.Source, favorite, item.Name, item.GetAttribute("Author"));
    }

    /// <summary>An address that carries a password or token is never written into favourites or markers.</summary>
    private bool RefuseSecret(MediaItem item)
    {
        if (!NetworkMedia.HasSecret(item.Source))
            return false;
        SetStatus("Not saved: this address contains a password or token");
        return true;
    }
}

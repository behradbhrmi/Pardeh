using Pardeh.Core.Library;
using Pardeh.Core.Playback;

namespace Pardeh.Core.Changes;

/// <summary>Lets the change service see whether a file is playing, and make the player let go of it.</summary>
public interface IPlaybackGuard
{
    bool IsInUse(string source);

    /// <summary>Closes the file. Call <see cref="IPlaybackHold.Resume"/> afterwards, whatever happened.</summary>
    IPlaybackHold Release(string source);
}

public interface IPlaybackHold
{
    /// <summary>Takes playback up again with the file now at <paramref name="newSource"/> (null: it is gone).</summary>
    void Resume(string? newSource);
}

/// <summary>The change service's two links to the player. Its members must run on the player's thread.</summary>
public sealed class PlayerFileLink(MediaPlayer player) : IQueueLink, IPlaybackGuard
{
    /// <summary>The same file, also when it is reached another way (a path and a document-tree URI).</summary>
    private static Func<string, bool> Same(string source)
    {
        var identity = TrackSort.IdentityOf(source);
        return other => string.Equals(TrackSort.IdentityOf(other), identity, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsInUse(string source) => player.IsFileInUse(Same(source));

    public IPlaybackHold Release(string source) => new Hold(player, player.ReleaseFile(Same(source)));

    private sealed class Hold(MediaPlayer player, FileRelease release) : IPlaybackHold
    {
        public void Resume(string? newSource) => player.ResumeFile(release, newSource);
    }

    public void Relink(string oldSource, string newSource, MediaItem fresh) => player.RelinkQueue(Same(oldSource), old =>
    {
        var item = new MediaItem(newSource)
        {
            Name = fresh.Name,
            Duration = fresh.Duration > TimeSpan.Zero ? fresh.Duration : old.Duration,
            VideoWidth = old.VideoWidth,
            VideoHeight = old.VideoHeight,
        };
        foreach (var (key, value) in fresh.Attributes)
            item.Attributes[key] = value;
        return item;
    });

    public void Remove(string source) => player.RemoveFromQueue(Same(source));
}

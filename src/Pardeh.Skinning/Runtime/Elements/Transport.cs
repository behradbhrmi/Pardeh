using Pardeh.Core.Playback;

namespace Pardeh.Skinning.Runtime.Elements;

/// <summary>Built-in behaviour of the typed buttons (PLAYBUTTON, STOPELEMENT, MUTEBUTTON …).</summary>
public enum TransportAction
{
    None,
    Play,
    Pause,
    Stop,
    Previous,
    Next,
    Rewind,
    FastForward,
    Mute,
    Shuffle,
    Repeat,
    Return,

    /// <summary>FAVBUTTON (phone looks): hearts or un-hearts the playing track; shown "down" while it is a favourite.</summary>
    Favorite,

    /// <summary>QUEUESORTBUTTON (phone looks): the queue's sort and tools menu.</summary>
    QueueSort,

    /// <summary>QUEUEFILTERBUTTON (phone looks): the queue's filter menu; "down" while a filter is on.</summary>
    QueueFilter,
}

public static class Transport
{
    public static TransportAction FromTag(string tag) => tag switch
    {
        "PLAYBUTTON" or "PLAYELEMENT" => TransportAction.Play,
        "PAUSEBUTTON" or "PAUSEELEMENT" => TransportAction.Pause,
        "STOPBUTTON" or "STOPELEMENT" => TransportAction.Stop,
        "PREVBUTTON" or "PREVELEMENT" => TransportAction.Previous,
        "NEXTBUTTON" or "NEXTELEMENT" => TransportAction.Next,
        "REWBUTTON" or "REWELEMENT" => TransportAction.Rewind,
        "FFWDBUTTON" or "FFWDELEMENT" => TransportAction.FastForward,
        "MUTEBUTTON" or "MUTEELEMENT" => TransportAction.Mute,
        "SHUFFLEBUTTON" or "SHUFFLEELEMENT" => TransportAction.Shuffle,
        "REPEATBUTTON" or "REPEATELEMENT" => TransportAction.Repeat,
        "RETURNBUTTON" => TransportAction.Return,
        "FAVBUTTON" => TransportAction.Favorite,
        "QUEUESORTBUTTON" => TransportAction.QueueSort,
        "QUEUEFILTERBUTTON" => TransportAction.QueueFilter,
        _ => TransportAction.None,
    };

    /// <summary>Whether the action is currently possible (drives the default "enabled" state).</summary>
    public static bool IsAvailable(TransportAction action, MediaPlayer player) => action switch
    {
        TransportAction.Play => player.IsAvailable("play") || player.PlayState is PlayState.ScanForward or PlayState.ScanReverse,
        TransportAction.Pause => player.IsAvailable("pause"),
        TransportAction.Stop => player.IsAvailable("stop"),
        TransportAction.Previous => player.IsAvailable("previous"),
        TransportAction.Next => player.IsAvailable("next"),
        TransportAction.Rewind or TransportAction.FastForward => player.IsAvailable("fastForward"),
        TransportAction.Favorite => player.Favorites is not null && player.CurrentMedia is not null,
        TransportAction.QueueSort => player.CurrentPlaylist.Count > 0,
        _ => true,
    };

    /// <summary>Latched state of toggle actions, or null if the action has none.</summary>
    public static bool? DownState(TransportAction action, MediaPlayer player) => action switch
    {
        TransportAction.Mute => player.Mute,
        TransportAction.Shuffle => player.Shuffle,
        TransportAction.Repeat => player.Loop,
        TransportAction.Rewind => player.PlayState == PlayState.ScanReverse,
        TransportAction.FastForward => player.PlayState == PlayState.ScanForward,
        TransportAction.Favorite => player.CurrentIsFavorite,
        TransportAction.QueueFilter => player.CurrentPlaylist.Filter.IsActive,
        _ => null,
    };

    public static void Execute(TransportAction action, SkinSession session)
    {
        var player = session.Player;
        switch (action)
        {
            case TransportAction.Play: player.Play(); break;
            case TransportAction.Pause: player.Pause(); break;
            case TransportAction.Stop: player.Stop(); break;
            case TransportAction.Previous: player.Previous(); break;
            case TransportAction.Next: player.Next(); break;
            case TransportAction.Rewind:
                if (player.PlayState == PlayState.ScanReverse) player.Play(); else player.FastReverse();
                break;
            case TransportAction.FastForward:
                if (player.PlayState == PlayState.ScanForward) player.Play(); else player.FastForward();
                break;
            case TransportAction.Mute: player.Mute = !player.Mute; break;
            case TransportAction.Shuffle: player.Shuffle = !player.Shuffle; break;
            case TransportAction.Repeat: player.Loop = !player.Loop; break;
            case TransportAction.Return: session.Host.ReturnToFullMode(); break;
            case TransportAction.Favorite: player.ToggleFavorite(); break;
            case TransportAction.QueueSort when session.CurrentView is { } sortView: QueueMenus.ShowSortMenu(session, sortView); break;
            case TransportAction.QueueFilter when session.CurrentView is { } filterView: QueueMenus.ShowFilterMenu(session, filterView); break;
        }
    }
}

/// <summary>Elements whose state follows the player (synced every tick).</summary>
public interface IPlayerSynced
{
    void SyncWithPlayer();
}

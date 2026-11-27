using System.Globalization;
using Pardeh.Core.Cleanup;

namespace Pardeh.Core.OnlineInfo;

/// <summary>Where a song stands. Lists mark only the first two: a mark on every checked song would hide them.</summary>
public enum FixInfoState
{
    /// <summary>Nothing is known to be wrong, and it has not been looked up.</summary>
    None,

    /// <summary>The local checks found something.</summary>
    NeedsCleanup,

    /// <summary>A strong match online differs from the file.</summary>
    MatchFound,

    /// <summary>Fixed, or already correct.</summary>
    Checked,

    /// <summary>Looked up with nothing usable; asked about again later.</summary>
    NoMatch,

    /// <summary>The user said to leave it.</summary>
    Ignored,
}

public static class FixInfoStates
{
    /// <param name="local">The local checks of the song, or null when it has not been looked at.</param>
    /// <param name="online">The saved result for the file as it is now, or null.</param>
    public static FixInfoState Of(bool ignored, CleanupProposal? local, OnlineEntry? online)
    {
        if (ignored)
            return FixInfoState.Ignored;
        if (online is { Group: OnlineGroup.Strong, Applied: false })
            return FixInfoState.MatchFound;
        if (local is { Level: not CleanupLevel.Clean })
            return FixInfoState.NeedsCleanup;
        return online switch
        {
            { Applied: true } or { Group: OnlineGroup.Checked } => FixInfoState.Checked,
            { Group: OnlineGroup.NoMatch } => FixInfoState.NoMatch,
            _ => FixInfoState.None,
        };
    }

    /// <summary>The state in words, or null when there is nothing to say.</summary>
    public static string? Describe(FixInfoState state, OnlineEntry? online)
    {
        var when = online is null ? string.Empty : " on " + online.LookedUp.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture);
        return state switch
        {
            FixInfoState.MatchFound => "Match found online" + when + ".",
            FixInfoState.Checked => online is { Applied: true } ? "Fixed with details from online." : "Checked online" + when + ": nothing to change.",
            FixInfoState.NoMatch => "No match online" + when + ".",
            FixInfoState.Ignored => "You chose to ignore this song.",
            _ => null,
        };
    }
}

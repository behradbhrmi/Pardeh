namespace Pardeh.Core.Playback;

/// <summary>Play states. Numeric values match the classic WMP scripting model (psStopped = 1 …).</summary>
public enum PlayState
{
    Undefined = 0,
    Stopped = 1,
    Paused = 2,
    Playing = 3,
    ScanForward = 4,
    ScanReverse = 5,
    Buffering = 6,
    Waiting = 7,
    MediaEnded = 8,
    Transitioning = 9,
    Ready = 10,
    Reconnecting = 11,
}

/// <summary>Open states. Numeric values match the classic WMP scripting model (osMediaOpen = 13 …).</summary>
public enum OpenState
{
    Undefined = 0,
    PlaylistChanging = 1,
    PlaylistLocating = 2,
    PlaylistConnecting = 3,
    PlaylistLoading = 4,
    PlaylistOpening = 5,
    PlaylistOpenNoMedia = 6,
    PlaylistChanged = 7,
    MediaChanging = 8,
    MediaLocating = 9,
    MediaConnecting = 10,
    MediaLoading = 11,
    MediaOpening = 12,
    MediaOpen = 13,
    BeginCodecAcquisition = 14,
    EndCodecAcquisition = 15,
    BeginLicenseAcquisition = 16,
    EndLicenseAcquisition = 17,
    BeginIndividualization = 18,
    EndIndividualization = 19,
    MediaWaiting = 20,
    OpeningUnknownUrl = 21,
}

/// <summary>Why the position was set: a given place, a step from where it was, a marker, or the player's own (scanning, A-B repeat).</summary>
public enum SeekKind
{
    Direct,
    Relative,
    Marker,
    Silent,
}

/// <summary>One change of position, in seconds. <paramref name="Requested"/> is the step asked for (relative seeks).</summary>
public readonly record struct SeekInfo(SeekKind Kind, double From, double To, double Requested = 0, string? Label = null);

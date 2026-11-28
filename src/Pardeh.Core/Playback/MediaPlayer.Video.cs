namespace Pardeh.Core.Playback;

/// <summary>Two video conveniences: shifting subtitles in time, and carrying a film on where it was left.</summary>
public sealed partial class MediaPlayer
{
    // ---- Subtitle delay ---------------------------------------------------------------------------------

    /// <summary>One press of the delay keys.</summary>
    public static readonly TimeSpan SubtitleDelayStep = TimeSpan.FromMilliseconds(100);

    private const double MaxSubtitleDelaySeconds = 600;

    /// <summary>How much later the subtitles show (negative: earlier). It belongs to the current item.</summary>
    public TimeSpan SubtitleDelay { get; private set; }

    /// <summary>"Subtitle delay +0.3 s"</summary>
    public static string DescribeDelay(TimeSpan delay) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(delay > TimeSpan.Zero ? "+" : delay < TimeSpan.Zero ? "-" : "")}{Math.Abs(delay.TotalSeconds):0.0} s");

    public void SetSubtitleDelay(TimeSpan delay)
    {
        SubtitleDelay = TimeSpan.FromMilliseconds(Math.Round(Math.Clamp(delay.TotalSeconds, -MaxSubtitleDelaySeconds, MaxSubtitleDelaySeconds) * 1000));
        SetStatus("Subtitle delay " + DescribeDelay(SubtitleDelay));
    }

    public void NudgeSubtitleDelay(int steps) => SetSubtitleDelay(SubtitleDelay + steps * SubtitleDelayStep);

    // ---- Resume where a video was left -------------------------------------------------------------------

    /// <summary>Shorter videos always start at the beginning.</summary>
    public const double ResumeMinDuration = 60;

    /// <summary>A position this close to the start is not worth keeping.</summary>
    public const double ResumeMinPosition = 15;

    private const double ResumeSaveEvery = 5;
    private double _sinceResumeSave;

    /// <summary>Where the places in videos are kept (null: every video starts at the beginning). The head sets it.</summary>
    public Library.VideoPositionStore? VideoPositions { get; set; }

    /// <summary>Videos carry on where they were left.</summary>
    public bool ResumeVideos { get; set; } = true;

    /// <summary>
    /// The place to remember for a video left at <paramref name="position"/>: null at the very start and near the end (the credits;
    /// the last twentieth, at least 15 s), where the next time should start afresh.
    /// </summary>
    public static double? ResumePoint(double position, double duration) =>
        duration >= ResumeMinDuration && position >= ResumeMinPosition && position < duration - Math.Max(15, duration * 0.05) ? position : null;

    /// <summary>Remembers (or forgets) the current video's place. Called as it plays and when it is left.</summary>
    public void SaveVideoPosition()
    {
        if (!ResumeVideos || VideoPositions is not { } store || CurrentMedia is not { } media || !HasVideo || NetworkMedia.HasSecret(media.Source))
            return;
        // A stopped player sits at 0: that is not where the viewer left off.
        if (_playState is PlayState.Stopped or PlayState.Ready)
            return;
        var duration = Duration;
        if (duration > 0)
            store.Set(media.Source, ResumePoint(CurrentPosition, duration));
    }

    private void TickVideoPosition(TimeSpan elapsed)
    {
        if (_playState != PlayState.Playing)
            return;
        _sinceResumeSave += elapsed.TotalSeconds;
        if (_sinceResumeSave < ResumeSaveEvery)
            return;
        _sinceResumeSave = 0;
        SaveVideoPosition();
    }

    /// <summary>After a video opened: jumps to where it was left, when there is such a place.</summary>
    private void ResumeVideo(MediaItem item)
    {
        _sinceResumeSave = 0;
        if (!ResumeVideos || VideoPositions?.Get(item.Source) is not { } at || !HasVideo || ResumePoint(at, Duration) is null)
            return;
        _engine.Position = TimeSpan.FromSeconds(at);
        ResumedAt = at;
    }

    /// <summary>Where the current video was taken up again when it opened, or null when it started at the beginning.</summary>
    public double? ResumedAt { get; private set; }
}

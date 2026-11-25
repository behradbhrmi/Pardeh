using NAudio.Wave;

namespace Pardeh.Nava;

/// <summary>A decoded audio stream: interleaved float stereo samples plus position and duration.</summary>
internal interface IAudioSource : ISampleProvider, IDisposable
{
    TimeSpan TotalTime { get; }

    /// <summary>Position of the next sample <see cref="ISampleProvider.Read"/> returns. Setting it seeks.</summary>
    TimeSpan CurrentTime { get; set; }

    /// <summary>"FFmpeg" or "Media Foundation".</summary>
    string DecoderName { get; }
}

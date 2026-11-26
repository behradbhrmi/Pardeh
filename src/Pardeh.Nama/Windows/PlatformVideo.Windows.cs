#pragma warning disable CA1416 // Windows-only file: only compiled into the net11.0 (desktop) build.
namespace Pardeh.Nama;

/// <summary>Windows: Media Foundation decodes pictures when FFmpeg cannot.</summary>
internal static class PlatformVideo
{
    public static TimedVideoDecoder? TryOpenFallback(string source, Func<TimeSpan> clock) => MfVideoDecoder.TryOpen(source, clock);
}

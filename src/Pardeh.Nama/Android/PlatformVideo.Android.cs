namespace Pardeh.Nama;

/// <summary>Android: there is no platform fallback for pictures yet.</summary>
internal static class PlatformVideo
{
    public static TimedVideoDecoder? TryOpenFallback(string source, Func<TimeSpan> clock) => null;
}

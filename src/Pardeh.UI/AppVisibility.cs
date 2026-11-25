namespace Pardeh.UI;

/// <summary>
/// Whether the app's window is on screen. Android's activity sets it (started / stopped: screen off, another app in front, the home screen);
/// on Windows it stays true. While it is false the view stops drawing and slows its tick, so no animation (live bars, visualizer) runs in
/// the background; playback itself is unaffected.
/// </summary>
public static class AppVisibility
{
    private static bool _foreground = true;

    public static bool Foreground => _foreground;

    /// <summary>Raised when <see cref="Foreground"/> changes (on the thread that set it).</summary>
    public static event Action<bool>? Changed;

    public static void Set(bool foreground)
    {
        if (_foreground == foreground)
            return;
        _foreground = foreground;
        Changed?.Invoke(foreground);
    }
}

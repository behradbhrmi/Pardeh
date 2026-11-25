using System.Runtime.InteropServices;

namespace Pardeh.App.Platform;

/// <summary>Windows 11 corner rounding by DWM (ignored on Windows 10, where the attribute doesn't exist).</summary>
internal static partial class WindowCorners
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;

    public static void Set(IntPtr hwnd, bool rounded)
    {
        if (hwnd == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;
        var preference = rounded ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

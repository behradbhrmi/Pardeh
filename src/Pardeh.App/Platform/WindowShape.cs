using System.Runtime.InteropServices;
using Pardeh.Skinning.Runtime;

namespace Pardeh.App.Platform;

/// <summary>
/// Shapes a window to the skin with a Win32 region: transparent pixels stop belonging to the window,
/// so they neither draw nor catch the mouse.
/// </summary>
internal static partial class WindowShape
{
    /// <param name="scale">Physical pixels per skin pixel.</param>
    public static void Apply(IntPtr hwnd, SkinMask mask, double scale)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero)
            return;

        var rects = new List<Rect>();
        // Merge identical runs on consecutive rows into taller rectangles (keeps the region small).
        var open = new Dictionary<(int X0, int X1), int>(); // run -> index in rects
        var previousRow = -1;
        var current = new Dictionary<(int, int), int>();
        foreach (var (y, x0, x1) in mask.Runs())
        {
            if (y != previousRow)
            {
                (open, current) = (y == previousRow + 1 ? current : [], []);
                previousRow = y;
            }

            if (open.TryGetValue((x0, x1), out var index))
            {
                var r = rects[index];
                rects[index] = r with { Bottom = y + 1 };
                current[(x0, x1)] = index;
            }
            else
            {
                rects.Add(new Rect(x0, y, x1, y + 1));
                current[(x0, x1)] = rects.Count - 1;
            }
        }

        var region = CreateRegion(rects, scale);
        if (region == IntPtr.Zero)
            return;
        if (SetWindowRgn(hwnd, region, true) == 0)
            DeleteObject(region); // on success the system owns the region
    }

    public static void Clear(IntPtr hwnd)
    {
        if (OperatingSystem.IsWindows() && hwnd != IntPtr.Zero)
            SetWindowRgn(hwnd, IntPtr.Zero, true);
    }

    private static IntPtr CreateRegion(List<Rect> rects, double scale)
    {
        const int headerSize = 32;
        var data = new byte[headerSize + rects.Count * 16];
        int Scale(int v) => (int)Math.Round(v * scale);

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (var i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            int l = Scale(r.Left), t = Scale(r.Top), rr = Scale(r.Right), b = Scale(r.Bottom);
            var offset = headerSize + i * 16;
            BitConverter.TryWriteBytes(data.AsSpan(offset), l);
            BitConverter.TryWriteBytes(data.AsSpan(offset + 4), t);
            BitConverter.TryWriteBytes(data.AsSpan(offset + 8), rr);
            BitConverter.TryWriteBytes(data.AsSpan(offset + 12), b);
            minX = Math.Min(minX, l); minY = Math.Min(minY, t); maxX = Math.Max(maxX, rr); maxY = Math.Max(maxY, b);
        }

        if (rects.Count == 0)
            minX = minY = maxX = maxY = 0;

        BitConverter.TryWriteBytes(data.AsSpan(0), headerSize);       // dwSize
        BitConverter.TryWriteBytes(data.AsSpan(4), 1);                // iType = RDH_RECTANGLES
        BitConverter.TryWriteBytes(data.AsSpan(8), rects.Count);      // nCount
        BitConverter.TryWriteBytes(data.AsSpan(12), rects.Count * 16); // nRgnSize
        BitConverter.TryWriteBytes(data.AsSpan(16), minX);
        BitConverter.TryWriteBytes(data.AsSpan(20), minY);
        BitConverter.TryWriteBytes(data.AsSpan(24), maxX);
        BitConverter.TryWriteBytes(data.AsSpan(28), maxY);

        return ExtCreateRegion(IntPtr.Zero, data.Length, data);
    }

    private readonly record struct Rect(int Left, int Top, int Right, int Bottom);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr ExtCreateRegion(IntPtr transform, int count, byte[] data);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr handle);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
}

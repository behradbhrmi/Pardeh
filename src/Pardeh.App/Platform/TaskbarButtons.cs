using System.Runtime.InteropServices;
using Avalonia.Controls;
using Pardeh.Core.Playback;

namespace Pardeh.App.Platform;

/// <summary>
/// Previous / Play-Pause / Next under the taskbar preview (the modern stand-in for the classic Taskbar Player,
/// since Windows 11 has no desk bands). Uses ITaskbarList3 thumbnail toolbar buttons with drawn icons.
/// </summary>
internal sealed class TaskbarButtons
{
    private const int ButtonPrevious = 1, ButtonPlay = 2, ButtonNext = 3;
    private const uint WmCommand = 0x0111;
    private const int ThbnClicked = 0x1800;

    private readonly IntPtr _hwnd;
    private readonly MediaPlayer _player;
    private readonly uint _taskbarCreated;
    private ITaskbarList3? _taskbar;
    private bool _added;
    private bool _showingPause;
    private readonly IntPtr _playIcon, _pauseIcon, _previousIcon, _nextIcon;

    private TaskbarButtons(Window window, IntPtr hwnd, MediaPlayer player)
    {
        _hwnd = hwnd;
        _player = player;
        _taskbarCreated = RegisterWindowMessage("TaskbarButtonCreated");
        _playIcon = Icon(Glyph.Play);
        _pauseIcon = Icon(Glyph.Pause);
        _previousIcon = Icon(Glyph.Previous);
        _nextIcon = Icon(Glyph.Next);
        Win32Properties.AddWndProcHookCallback(window, Hook);
        player.PlayStateChanged += (_, _) => Update();
        AddButtons(); // usually the button exists already; TaskbarButtonCreated retries otherwise
    }

    public static TaskbarButtons? TryCreate(Window window, IntPtr hwnd, MediaPlayer player, Action<string> log)
    {
        try
        {
            return new TaskbarButtons(window, hwnd, player);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or EntryPointNotFoundException)
        {
            log("Taskbar buttons unavailable: " + ex.Message);
            return null;
        }
    }

    private IntPtr Hook(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == _taskbarCreated)
        {
            _added = false;
            AddButtons();
        }
        else if (message == WmCommand && ((int)wParam >> 16 & 0xFFFF) == ThbnClicked)
        {
            switch ((int)wParam & 0xFFFF)
            {
                case ButtonPrevious: _player.Previous(); break;
                case ButtonNext: _player.Next(); break;
                case ButtonPlay:
                    if (_player.PlayState == PlayState.Playing) _player.Pause(); else _player.Play();
                    break;
            }

            handled = true;
        }

        return IntPtr.Zero;
    }

    private void AddButtons()
    {
        try
        {
            if (_added)
                return;
            _taskbar ??= (ITaskbarList3)new TaskbarList();
            _taskbar.HrInit();
            var buttons = new[]
            {
                Button(ButtonPrevious, _previousIcon, "Previous"),
                Button(ButtonPlay, _playIcon, "Play"),
                Button(ButtonNext, _nextIcon, "Next"),
            };
            if (_taskbar.ThumbBarAddButtons(_hwnd, (uint)buttons.Length, buttons) == 0)
                _added = true;
            Update();
        }
        catch (COMException)
        {
        }
    }

    private void Update()
    {
        if (!_added || _taskbar is null)
            return;
        var playing = _player.PlayState == PlayState.Playing;
        if (playing == _showingPause)
            return;
        _showingPause = playing;
        var buttons = new[] { Button(ButtonPlay, playing ? _pauseIcon : _playIcon, playing ? "Pause" : "Play") };
        _taskbar.ThumbBarUpdateButtons(_hwnd, 1, buttons);
    }

    private static ThumbButton Button(int id, IntPtr icon, string tip) => new()
    {
        Mask = ThumbButtonMask.Icon | ThumbButtonMask.Tooltip | ThumbButtonMask.Flags,
        Id = (uint)id,
        Icon = icon,
        Tip = tip,
        Flags = ThumbButtonFlags.Enabled,
    };

    // ---- Icons: 16×16, drawn into a 32-bit ARGB bitmap -------------------------------------------

    private enum Glyph { Play, Pause, Previous, Next }

    private static IntPtr Icon(Glyph glyph)
    {
        const int size = 16;
        var pixels = new int[size * size];
        bool Inside(double x, double y) => glyph switch
        {
            Glyph.Play => x >= 4 && x <= 13 && Math.Abs(y - 7.5) <= (x - 4) * -0.7 + 6.3,
            Glyph.Pause => y >= 3 && y <= 12 && ((x >= 4 && x <= 6.5) || (x >= 9.5 && x <= 12)),
            Glyph.Previous => (x >= 3 && x <= 4.5 && y >= 3.5 && y <= 11.5) || (x >= 5 && x <= 13 && Math.Abs(y - 7.5) <= (13 - x) * 0.5),
            _ => (x >= 11.5 && x <= 13 && y >= 3.5 && y <= 11.5) || (x >= 3 && x <= 11 && Math.Abs(y - 7.5) <= (x - 3) * 0.5),
        };
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                // 4×4 supersampling for smooth edges; white glyph.
                var hits = 0;
                for (var sy = 0; sy < 4; sy++)
                    for (var sx = 0; sx < 4; sx++)
                        if (Inside(x + (sx + 0.5) / 4, y + (sy + 0.5) / 4))
                            hits++;
                var alpha = hits * 255 / 16;
                pixels[y * size + x] = (alpha << 24) | (alpha << 16) | (alpha << 8) | alpha; // premultiplied white
            }
        }

        var header = new BitmapInfoHeader { Size = 40, Width = size, Height = -size, Planes = 1, BitCount = 32 };
        var color = CreateDIBSection(IntPtr.Zero, ref header, 0, out var bits, IntPtr.Zero, 0);
        Marshal.Copy(pixels, 0, bits, pixels.Length);
        var mask = CreateBitmap(size, size, 1, 1, IntPtr.Zero);
        var info = new IconInfo { IsIcon = true, Color = color, Mask = mask };
        var icon = CreateIconIndirect(ref info);
        DeleteObject(color);
        DeleteObject(mask);
        return icon;
    }

    // ---- Interop ------------------------------------------------------------------------------------

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
    private class TaskbarList;

    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);

        // ITaskbarList3
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, int flags);
        void RegisterTab(IntPtr tab, IntPtr mdi);
        void UnregisterTab(IntPtr tab);
        void SetTabOrder(IntPtr tab, IntPtr insertBefore);
        void SetTabActive(IntPtr tab, IntPtr mdi, uint reserved);

        [PreserveSig]
        int ThumbBarAddButtons(IntPtr hwnd, uint count, [MarshalAs(UnmanagedType.LPArray)] ThumbButton[] buttons);

        [PreserveSig]
        int ThumbBarUpdateButtons(IntPtr hwnd, uint count, [MarshalAs(UnmanagedType.LPArray)] ThumbButton[] buttons);
    }

    [Flags]
    private enum ThumbButtonMask : uint { Bitmap = 1, Icon = 2, Tooltip = 4, Flags = 8 }

    [Flags]
    private enum ThumbButtonFlags : uint { Enabled = 0 }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ThumbButton
    {
        public ThumbButtonMask Mask;
        public uint Id;
        public uint Bitmap;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string Tip;
        public ThumbButtonFlags Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
        public int HotspotX, HotspotY;
        public IntPtr Mask, Color;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, IntPtr bits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref IconInfo info);
}

using System.Runtime.InteropServices;

namespace Pardeh.App.Platform;

/// <summary>Synchronous Win32 dialogs. Skin scripts call theme.openDialog() and expect the answer immediately.</summary>
internal static partial class NativeDialogs
{
    private const string MediaFilter =
        "All Media Files\0*.mp3;*.wma;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.opus;*.mid;*.midi;*.mp4;*.m4v;*.wmv;*.avi;*.mkv;*.mov;*.mpg;*.mpeg;*.asf;*.webm;*.ts;*.m3u;*.m3u8;*.pls;*.wpl;*.asx;*.xspf\0" +
        "Audio Files\0*.mp3;*.wma;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.opus;*.mid;*.midi\0" +
        "Video Files\0*.mp4;*.m4v;*.wmv;*.avi;*.mkv;*.mov;*.mpg;*.mpeg;*.asf;*.webm;*.ts\0" +
        "Playlists\0*.m3u;*.m3u8;*.pls;*.wpl;*.asx;*.wax;*.wvx;*.xspf\0" +
        "All Files (*.*)\0*.*\0\0";

    public static string? OpenMediaFile(IntPtr owner)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        const int bufferChars = 32768;
        var buffer = Marshal.AllocHGlobal(bufferChars * 2);
        var filter = Marshal.StringToHGlobalUni(MediaFilter);
        var title = Marshal.StringToHGlobalUni("Open");
        try
        {
            Marshal.WriteInt16(buffer, 0);
            var ofn = new OpenFileName
            {
                lStructSize = Marshal.SizeOf<OpenFileName>(),
                hwndOwner = owner,
                lpstrFilter = filter,
                nFilterIndex = 1,
                lpstrFile = buffer,
                nMaxFile = bufferChars,
                lpstrTitle = title,
                Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_HIDEREADONLY | OFN_NOCHANGEDIR,
            };

            return GetOpenFileNameW(ref ofn) ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            Marshal.FreeHGlobal(filter);
            Marshal.FreeHGlobal(title);
        }
    }

    public static bool Confirm(IntPtr owner, string text, string caption)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        return MessageBoxW(owner, text, caption, MB_YESNO | MB_ICONQUESTION) == IDYES;
    }

    public static void Inform(IntPtr owner, string text, string caption)
    {
        if (OperatingSystem.IsWindows())
            MessageBoxW(owner, text, caption, MB_OK | MB_ICONINFORMATION);
    }

    private const int OFN_EXPLORER = 0x00080000;
    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_HIDEREADONLY = 0x00000004;
    private const int OFN_NOCHANGEDIR = 0x00000008;
    private const uint MB_OK = 0x0, MB_YESNO = 0x4, MB_ICONQUESTION = 0x20, MB_ICONINFORMATION = 0x40;
    private const int IDYES = 6;

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public IntPtr lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public IntPtr lpstrInitialDir;
        public IntPtr lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public IntPtr lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [LibraryImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetOpenFileNameW(ref OpenFileName ofn);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}

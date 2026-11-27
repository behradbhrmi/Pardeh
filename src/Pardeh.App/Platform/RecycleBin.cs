using System.Runtime.InteropServices;
using Pardeh.Core.Changes;

namespace Pardeh.App.Platform;

/// <summary>
/// The Windows Recycle Bin. Windows deletes for good where it has no bin (network and most removable drives), so such files
/// are not accepted here and go to the holding folder; if Windows would still delete one for good (a bin that is too small
/// or turned off), it is asked to warn first, and the user can refuse.
/// </summary>
internal sealed partial class RecycleBin : ISystemTrash
{
    private const int MaxPath = 260; // the shell function below takes no longer names

    private const uint Delete = 3; // FO_DELETE
    private const ushort Silent = 0x0004, NoConfirmation = 0x0010, AllowUndo = 0x0040, NoErrorUi = 0x0400, WantNukeWarning = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileOperation
    {
        public IntPtr Window;
        public uint Function;
        public IntPtr From;
        public IntPtr To;
        public ushort Flags;
        public int Aborted;
        public IntPtr NameMappings;
        public IntPtr ProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BinInfo
    {
        public int Size;
        public long Bytes;
        public long Items;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    private static partial int ShellFileOperation(ref FileOperation operation);

    [LibraryImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int QueryRecycleBin(string root, ref BinInfo info);

    public bool Accepts(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (full.Length >= MaxPath || full.StartsWith(@"\\", StringComparison.Ordinal) || Path.GetPathRoot(full) is not { Length: > 0 } root)
                return false;
            if (new DriveInfo(root).DriveType != DriveType.Fixed)
                return false;
            var info = new BinInfo { Size = Marshal.SizeOf<BinInfo>() };
            return QueryRecycleBin(root, ref info) == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public void Trash(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new IOException("The file is missing.");
        // The list of names ends with an extra null.
        var from = Marshal.StringToHGlobalUni(full + '\0');
        try
        {
            var operation = new FileOperation
            {
                Function = Delete,
                From = from,
                Flags = (ushort)(Silent | NoConfirmation | AllowUndo | NoErrorUi | WantNukeWarning),
            };
            var result = ShellFileOperation(ref operation);
            if (result != 0 || operation.Aborted != 0 || File.Exists(full))
                throw new IOException("The file was not moved to the Recycle Bin.");
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }
}

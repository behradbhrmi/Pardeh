using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Pardeh.Core.Library;

namespace Pardeh.Core.Duplicates;

/// <summary>
/// Tells whether two sources are one and the same file on the storage: overlapping library folders, links, junctions,
/// substituted drives, and Android's several routes to one file. Removing one "copy" of a single file would delete the
/// song, so nothing is removed unless the two are <i>shown</i> to be different files: when that can't be told, the answer
/// is no.
/// </summary>
public static class PhysicalFiles
{
    private const string Unsure = "Pardeh can't tell whether these are two files or one file reached two ways, so it removes neither.";
    private const string OneFile = "These are one file reached two ways: removing it would delete the song.";
    private const int Attempts = 5;

    /// <summary>
    /// The head's way of naming the file behind a source (Android: the device and file numbers of a path or content URI).
    /// Equal names are the same file; null: unknown. Not set: Windows asks the file system.
    /// </summary>
    public static Func<string, string?>? PlatformId { get; set; }

    /// <summary>A name for the file behind a source, the same for every route to it, or null when it can't be told.</summary>
    public static string? IdOf(string source)
    {
        try
        {
            if (PlatformId is { } platform)
                return platform(source);
            return OperatingSystem.IsWindows() && !source.Contains("://", StringComparison.Ordinal) ? WindowsId(source) : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    // The path an identity hook gives a source (Android folds /sdcard and picked-folder URIs into the real path).
    private static string Route(string source) => TrackSort.IdentityOf(source);

    /// <summary>
    /// True when the two routes lead to the same place by their text alone. Names that differ only in case count as the same:
    /// being wrong that way only leaves a duplicate in place.
    /// </summary>
    public static bool SameRoute(string first, string second) => string.Equals(Route(first), Route(second), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Null when <paramref name="removed"/> is shown to be another file than <paramref name="kept"/>, else why it must not be
    /// removed. Asked right before a removal.
    /// </summary>
    public static string? WhyNotDistinct(string removed, string kept)
    {
        if (SameRoute(removed, kept))
            return OneFile;
        if (IdOf(removed) is not { } first || IdOf(kept) is not { } second)
            return Unsure;
        if (first == second)
            return OneFile;
        // A second opinion that needs no file numbers (a network drive may give them wrongly): with one of the two held so
        // that nobody else can open it, the other must still open.
        return IsPath(removed) && IsPath(kept) ? Probe(removed, kept) : null;
    }

    private static bool IsPath(string source) => !source.Contains("://", StringComparison.Ordinal);

    private static string? Probe(string removed, string kept)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var held = new FileStream(removed, FileMode.Open, FileAccess.Read, FileShare.None);
                try
                {
                    using var other = new FileStream(kept, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    return null;
                }
                catch (IOException) when (attempt >= Attempts)
                {
                    return OneFile + " (Or the copy to keep is in use.)";
                }
            }
            catch (IOException ex) when (attempt >= Attempts)
            {
                return "The file is in use. " + ex.Message;
            }
            catch (UnauthorizedAccessException ex)
            {
                return ex.Message;
            }
            catch (IOException)
            {
                // Something reads one of them for a moment (a thumbnail, a virus scanner): waited out below.
            }

            Thread.Sleep(150 * attempt);
        }
    }

    // ---- Windows ---------------------------------------------------------------------------------

    private const uint ReadAttributes = 0x80, ShareAll = 0x7, OpenExisting = 3, BackupSemantics = 0x02000000;
    private const int FileIdInfoClass = 18;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        public ulong Volume;
        public ulong Low;
        public ulong High;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, out FileIdInfo info, int size);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct HandleInfoNative
    {
        public uint Attributes;
        public ulong Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out HandleInfoNative info);

    // Links are followed: no "open the link itself" flag.
    private static string? WindowsId(string path)
    {
        using var handle = CreateFile(Path.GetFullPath(path), ReadAttributes, ShareAll, IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
            return null;
        if (GetFileInformationByHandleEx(handle, FileIdInfoClass, out var wide, Marshal.SizeOf<FileIdInfo>()) && (wide.Low != 0 || wide.High != 0))
            return $"{wide.Volume:x}:{wide.High:x}:{wide.Low:x}";
        if (!GetFileInformationByHandle(handle, out var info) || info is { IndexHigh: 0, IndexLow: 0 })
            return null;
        return $"v{info.Volume:x}:{info.IndexHigh:x}:{info.IndexLow:x}";
    }
}

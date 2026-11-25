using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Pardeh.App.Platform;

/// <summary>
/// "File › Import from Windows XP…": copies the classic player's user interface out of the user's own Windows XP
/// (an install ISO, the CD's I386 folder, WMPLOC.DL_ or an installed wmploc.dll) into %APPDATA%\Pardeh\WMP9.
/// Nothing in the DLL runs: it is loaded as a data file and only its resources are read.
/// </summary>
internal static partial class WmpImporter
{
    public static string TargetDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pardeh", "WMP9");

    /// <summary>Imports from <paramref name="source"/> and returns a summary, or throws with a user-readable message.</summary>
    public static string Import(string source, Action<string> progress)
    {
        var work = Path.Combine(Path.GetTempPath(), "Pardeh-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var dll = Path.Combine(work, "wmploc.dll");
            var extension = Path.GetExtension(source).ToLowerInvariant();
            if (Directory.Exists(source))
                ExpandFromFolder(source, dll);
            else if (extension == ".iso")
                ExpandFromIso(source, dll, progress);
            else if (extension == ".dl_")
                Expand(source, dll);
            else if (extension == ".dll")
                File.Copy(source, dll);
            else
                throw new InvalidOperationException("Choose a Windows XP ISO, its I386 folder, WMPLOC.DL_ or wmploc.dll.");

            progress("Reading the player's resources…");
            var staging = Path.Combine(work, "res");
            var summary = ExtractResources(dll, staging);
            if (!File.Exists(Path.Combine(staging, "text", "MAINAPPSKIN.WSZ")))
                throw new InvalidOperationException("This wmploc.dll doesn't contain Windows Media Player 9's full mode (it may be from another version).");

            var target = TargetDirectory;
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            CopyDirectory(staging, target);
            return summary;
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void ExpandFromFolder(string folder, string dll)
    {
        foreach (var candidate in new[] { Path.Combine(folder, "WMPLOC.DL_"), Path.Combine(folder, "I386", "WMPLOC.DL_"), Path.Combine(folder, "wmploc.dll") })
        {
            if (!File.Exists(candidate))
                continue;
            if (candidate.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                File.Copy(candidate, dll);
            else
                Expand(candidate, dll);
            return;
        }

        throw new InvalidOperationException("No WMPLOC.DL_ or wmploc.dll in that folder (look for the CD's I386 folder).");
    }

    private static void ExpandFromIso(string iso, string dll, Action<string> progress)
    {
        progress("Opening the ISO…");
        var drive = RunPowerShell($"(Mount-DiskImage -ImagePath '{iso.Replace("'", "''")}' -Access ReadOnly -PassThru | Get-Volume).DriveLetter").Trim();
        if (drive.Length != 1)
            throw new InvalidOperationException("Windows couldn't open the ISO.");
        try
        {
            progress("Copying wmploc.dll…");
            ExpandFromFolder(drive + @":\", dll);
        }
        finally
        {
            RunPowerShell($"Dismount-DiskImage -ImagePath '{iso.Replace("'", "''")}' | Out-Null");
        }
    }

    private static void Expand(string cabinet, string target)
    {
        var expand = Path.Combine(Environment.SystemDirectory, "expand.exe");
        Run(expand, $"\"{cabinet}\" \"{target}\"");
        if (!File.Exists(target))
            throw new InvalidOperationException("Couldn't unpack " + Path.GetFileName(cabinet) + ".");
    }

    private static string RunPowerShell(string command) =>
        Run("powershell.exe", $"-NoProfile -NonInteractive -Command \"{command.Replace("\"", "\\\"")}\"");

    private static string Run(string file, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
        }) ?? throw new InvalidOperationException("Couldn't start " + Path.GetFileName(file) + ".");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(120_000);
        return output;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    // ---- Resource extraction (data only) ----------------------------------------------------------

    private const uint LoadLibraryAsDatafile = 0x2, LoadLibraryAsImageResource = 0x20;

    /// <summary>
    /// Writes the layout WmpResources reads: text/ (compiled skins and scripts), bitmap/&lt;id&gt;.bmp, image/&lt;id&gt;.bin,
    /// jpg/&lt;id&gt;.jpg, html/ and strings.tsv.
    /// </summary>
    private static string ExtractResources(string dll, string outDir)
    {
        var module = LoadLibraryEx(dll, IntPtr.Zero, LoadLibraryAsDatafile | LoadLibraryAsImageResource);
        if (module == IntPtr.Zero)
            throw new InvalidOperationException("Couldn't read wmploc.dll.");
        try
        {
            var counts = new Dictionary<string, int>();
            var strings = new StringBuilder();
            // Named types and names are only valid inside the callbacks, so keep them as keys.
            var types = new List<string>();
            EnumResourceTypes(module, (_, type, _) => { types.Add(Key(type)); return true; }, IntPtr.Zero);
            foreach (var typeKey in types)
            {
                (string? Folder, string Extension) layout = typeKey switch
                {
                    "#2" => ("bitmap", ".bmp"),
                    "#6" => (null, string.Empty),
                    "#23" => ("html", string.Empty),
                    "#256" => ("text", string.Empty),
                    "#257" => ("image", ".bin"),
                    "JPGTYPE" => ("jpg", ".jpg"),
                    _ => ("", string.Empty),
                };
                if (layout.Folder == "")
                    continue;

                var names = new List<string>();
                EnumResourceNames(module, typeKey, (_, _, name, _) => { names.Add(Key(name)); return true; }, IntPtr.Zero);
                foreach (var name in names)
                {
                    var data = Read(module, typeKey, name);
                    if (data is null)
                        continue;
                    if (layout.Folder is null)
                    {
                        AppendStringBlock(strings, int.Parse(name.TrimStart('#')), data);
                        continue;
                    }

                    if (typeKey == "#2")
                        data = DibToBmp(data);
                    var directory = Path.Combine(outDir, layout.Folder);
                    Directory.CreateDirectory(directory);
                    File.WriteAllBytes(Path.Combine(directory, SafeName(name) + layout.Extension), data);
                    counts[layout.Folder] = counts.GetValueOrDefault(layout.Folder) + 1;
                }
            }

            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "strings.tsv"), strings.ToString(), new UTF8Encoding(false));
            return string.Join(", ", counts.Select(c => $"{c.Value} {c.Key}"));
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    private static byte[]? Read(IntPtr module, string typeKey, string name)
    {
        var namePtr = ToPointer(name);
        var typePtr = ToPointer(typeKey);
        try
        {
            var resource = FindResource(module, namePtr, typePtr);
            if (resource == IntPtr.Zero)
                return null;
            var size = SizeofResource(module, resource);
            var data = new byte[size];
            Marshal.Copy(LockResource(LoadResource(module, resource)), data, 0, size);
            return data;
        }
        finally
        {
            Free(name, namePtr);
            Free(typeKey, typePtr);
        }
    }

    /// <summary>"#123" → MAKEINTRESOURCE(123); a name → a native string (free with <see cref="Free"/>).</summary>
    private static IntPtr ToPointer(string key) => key.StartsWith('#') ? (IntPtr)int.Parse(key[1..]) : Marshal.StringToHGlobalUni(key);

    private static void Free(string key, IntPtr pointer)
    {
        if (!key.StartsWith('#'))
            Marshal.FreeHGlobal(pointer);
    }

    /// <summary>An RT_STRING block: 16 length-prefixed UTF-16 strings; block n holds ids (n-1)*16 … (n-1)*16+15.</summary>
    private static void AppendStringBlock(StringBuilder strings, int block, byte[] data)
    {
        var position = 0;
        for (var i = 0; i < 16 && position + 2 <= data.Length; i++)
        {
            int length = BitConverter.ToUInt16(data, position);
            position += 2;
            if (length > 0 && position + length * 2 <= data.Length)
                strings.Append((block - 1) * 16 + i).Append('\t').Append(Encoding.Unicode.GetString(data, position, length * 2)).Append('\n');
            position += length * 2;
        }
    }

    /// <summary>RT_BITMAP resources are DIBs without the 14-byte BITMAPFILEHEADER.</summary>
    private static byte[] DibToBmp(byte[] dib)
    {
        var headerSize = BitConverter.ToInt32(dib, 0);
        var bitCount = BitConverter.ToInt16(dib, headerSize == 12 ? 10 : 14);
        var compression = headerSize >= 40 ? BitConverter.ToInt32(dib, 16) : 0;
        var colorsUsed = headerSize >= 40 ? BitConverter.ToInt32(dib, 32) : 0;
        var colors = colorsUsed > 0 ? colorsUsed : bitCount <= 8 ? 1 << bitCount : 0;
        var masks = headerSize == 40 && compression == 3 ? 12 : 0;
        var offset = 14 + headerSize + masks + colors * (headerSize == 12 ? 3 : 4);
        var bmp = new byte[14 + dib.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
        BitConverter.GetBytes(offset).CopyTo(bmp, 10);
        Buffer.BlockCopy(dib, 0, bmp, 14, dib.Length);
        return bmp;
    }

    private static string Key(IntPtr value) =>
        (value.ToInt64() >> 16) == 0 ? "#" + value.ToInt64() : Marshal.PtrToStringUni(value) ?? string.Empty;

    private static string SafeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Replace("#", string.Empty);
    }

    private delegate bool EnumTypesProc(IntPtr module, IntPtr type, IntPtr param);

    private delegate bool EnumNamesProc(IntPtr module, IntPtr type, IntPtr name, IntPtr param);

    [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", EntryPoint = "EnumResourceTypesW")]
    private static extern bool EnumResourceTypes(IntPtr module, EnumTypesProc callback, IntPtr param);

    [DllImport("kernel32.dll", EntryPoint = "EnumResourceNamesW")]
    private static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumNamesProc callback, IntPtr param);

    private static void EnumResourceNames(IntPtr module, string typeKey, EnumNamesProc callback, IntPtr param)
    {
        var type = ToPointer(typeKey);
        try
        {
            EnumResourceNames(module, type, callback, param);
        }
        finally
        {
            Free(typeKey, type);
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "FindResourceW")]
    private static partial IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LoadResource(IntPtr module, IntPtr resource);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LockResource(IntPtr data);

    [LibraryImport("kernel32.dll")]
    private static partial int SizeofResource(IntPtr module, IntPtr resource);
}

using System.Security.Cryptography;

namespace Pardeh.Core.Changes;

/// <summary>The few file operations whose exact behaviour the change service depends on.</summary>
public static class SafeFiles
{
    public static string Hash(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));

    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Hash(stream);
    }

    /// <summary>Copies into a file that must not exist yet, and flushes it to disk before returning.</summary>
    public static void CopyNew(Stream from, string to)
    {
        var created = false;
        try
        {
            using var target = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created = true;
            from.CopyTo(target);
            target.Flush(flushToDisk: true);
        }
        catch when (created)
        {
            TryDelete(to); // half a copy is worse than none
            throw;
        }
    }

    public static void CopyNew(string from, string to)
    {
        using var source = File.OpenRead(from);
        CopyNew(source, to);
    }

    /// <summary>Writes a small file so that it is either the old or the new one after a crash.</summary>
    public static void WriteDurably(string path, byte[] data)
    {
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(data);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, path, overwrite: true);
    }

    public static void TryDelete(string? path)
    {
        if (path is null)
            return;
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
    }

    /// <summary>"name.ext", then "name (suffix).ext", "name (suffix 2).ext" … until <paramref name="taken"/> says no.</summary>
    public static string FreeName(string name, Func<string, bool> taken, string suffix = "")
    {
        if (!taken(name))
            return name;
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var number = 1; number < 1000; number++)
        {
            var mark = suffix.Length == 0 ? $"{number + 1}" : number == 1 ? suffix : $"{suffix} {number}";
            var candidate = $"{stem} ({mark}){extension}";
            if (!taken(candidate))
                return candidate;
        }

        throw new IOException($"No free name for {name}.");
    }
}

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace Pardeh.Nava.Ffmpeg;

/// <summary>
/// Resolves FFmpeg functions from the APK's native library folder, where Android only accepts
/// unversioned lib*.so names (libavcodec.so, libavformat.so …).
/// </summary>
internal sealed class AndroidFfmpegResolver(string directory) : IFunctionResolver
{
    private readonly ConcurrentDictionary<string, IntPtr> _libraries = new();

    public T GetFunctionDelegate<T>(string libraryName, string functionName, bool throwOnError = true)
    {
        var handle = _libraries.GetOrAdd(libraryName, name =>
        {
            var path = Path.Combine(directory, "lib" + name + ".so");
            return NativeLibrary.TryLoad(path, out var loaded) ? loaded : IntPtr.Zero;
        });
        if (handle != IntPtr.Zero && NativeLibrary.TryGetExport(handle, functionName, out var function))
            return Marshal.GetDelegateForFunctionPointer<T>(function);
        if (throwOnError)
            throw new EntryPointNotFoundException($"{functionName} not found in lib{libraryName}.so ({directory}).");
        return default!;
    }
}

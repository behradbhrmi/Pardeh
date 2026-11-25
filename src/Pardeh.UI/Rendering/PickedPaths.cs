using Avalonia.Platform.Storage;
using Pardeh.Core.Library;

namespace Pardeh.UI.Rendering;

/// <summary>What a file or folder picker returned, as a path the library and player can use.</summary>
internal static class PickedPaths
{
    /// <summary>"Device music": asks for permission, then lists the media store (music and videos) in the library.</summary>
    public static async Task AddDeviceMedia(MediaLibrary library)
    {
        if (!await PlatformServices.Current.RequestMediaAccess())
        {
            PlatformServices.Current.Inform(null, "Pardeh needs permission to read your music. You can allow it in the system settings.", "Pardeh");
            return;
        }

        library.AddFolder(MediaSources.DeviceAudio);
        library.AddFolder(MediaSources.DeviceVideo);
    }

    /// <summary>A local path, or on Android the content URI (access is made persistent for folders and files).</summary>
    public static string? Of(IStorageItem? item)
    {
        if (item is null)
            return null;
        if (item.TryGetLocalPath() is { } local)
            return local;
        if (item.Path is { IsAbsoluteUri: true } uri && uri.Scheme == "content")
        {
            var text = uri.ToString();
            try
            {
                MediaSources.PersistAccess?.Invoke(text);
            }
            catch (Exception)
            {
                // Access then lasts until the app is closed.
            }

            return text;
        }

        return null;
    }
}

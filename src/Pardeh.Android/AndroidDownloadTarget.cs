using Android.Content;
using Android.Provider;
using Pardeh.Core.Downloads;
using AndroidUri = Android.Net.Uri;

namespace Pardeh.Android;

/// <summary>
/// Downloads go to Music/Pardeh in shared storage. On Android 10 and later an app needs no permission for a media file it
/// makes itself: the media store entry is marked pending while it is written, so no other app lists half a file. Android 7
/// to 9 need the storage permission and take a plain file.
/// </summary>
internal sealed class AndroidDownloadTarget : IDownloadTarget
{
    private const string Folder = "Pardeh";

    private static Context AppContext => global::Android.App.Application.Context;

    public string Description => "Music/Pardeh on this device";

    private static string PlainFolder => System.IO.Path.Combine(global::Android.OS.Environment.ExternalStorageDirectory!.AbsolutePath,
        global::Android.OS.Environment.DirectoryMusic!, Folder);

    public async Task<string> PublishAsync(string localFile, string fileName, CancellationToken cancel)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            return await Task.Run(() => Insert(localFile, fileName, cancel), cancel).ConfigureAwait(false);

        // The question must be asked from the screen's thread.
        var granted = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            () => MainActivity.Current?.RequestAsync("android.permission.WRITE_EXTERNAL_STORAGE") ?? Task.FromResult(false));
        if (!granted)
            throw new IOException("Permission to save in the Music folder was not given.");
        var path = await new FolderDownloadTarget(PlainFolder).PublishAsync(localFile, fileName, cancel).ConfigureAwait(false);
        global::Android.Media.MediaScannerConnection.ScanFile(AppContext, [path], null, (global::Android.Media.MediaScannerConnection.IOnScanCompletedListener?)null);
        return path;
    }

    private static string Insert(string localFile, string fileName, CancellationToken cancel)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
            throw new NotSupportedException();
        var resolver = AppContext.ContentResolver ?? throw new IOException("Android's media store is not there.");
        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(MediaStore.IMediaColumns.MimeType, AudioSniffer.MimeType(System.IO.Path.GetExtension(fileName)));
        values.Put(MediaStore.IMediaColumns.RelativePath, global::Android.OS.Environment.DirectoryMusic + "/" + Folder);
        values.Put(MediaStore.IMediaColumns.IsPending, 1);
        AndroidUri? uri = null;
        var finished = false;
        try
        {
            // The store never overwrites: a taken name gets a number.
            uri = resolver.Insert(MediaStore.Audio.Media.GetContentUri(MediaStore.VolumeExternalPrimary)!, values)
                ?? throw new IOException("Android did not make the new file.");
            using (var from = File.OpenRead(localFile))
            using (var to = resolver.OpenOutputStream(uri) ?? throw new IOException("Android did not open the new file."))
            {
                from.CopyTo(to);
                to.Flush();
            }

            cancel.ThrowIfCancellationRequested();
            var done = new ContentValues();
            done.Put(MediaStore.IMediaColumns.IsPending, 0);
            if (resolver.Update(uri, done, null, null) != 1)
                throw new IOException("Android did not finish the new file.");

#pragma warning disable CA1422 // _data is deprecated but is the only way to give FFmpeg and TagLib a real path
            using var cursor = resolver.Query(uri, ["_data"], null, null, null);
#pragma warning restore CA1422
            var path = cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) : null;
            if (path is null || !File.Exists(path) || new FileInfo(path).Length != new FileInfo(localFile).Length)
                throw new IOException("The new file can't be read back.");
            finished = true;
            return path;
        }
        catch (Java.Lang.Exception ex)
        {
            throw new IOException("Android refused the new file: " + ex.Message, ex);
        }
        finally
        {
            // Half a file is worse than none.
            if (!finished && uri is not null)
            {
                try
                {
                    resolver.Delete(uri, null, null);
                }
                catch (Java.Lang.Exception)
                {
                }
            }
        }
    }
}

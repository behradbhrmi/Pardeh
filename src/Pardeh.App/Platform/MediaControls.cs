using Avalonia.Threading;
using Pardeh.Core.Playback;
using Windows.Media;
using Windows.Storage.Streams;

namespace Pardeh.App.Platform;

/// <summary>
/// Windows' media controls (System Media Transport Controls): media keys and headset buttons work while Pardeh is
/// in the background, and the volume / media overlay shows the title, artist and cover.
/// </summary>
internal sealed class MediaControls : IDisposable
{
    private readonly MediaPlayer _player;
    private readonly SystemMediaTransportControls _controls;
    private readonly Action<string> _log;

    private MediaControls(MediaPlayer player, SystemMediaTransportControls controls, Action<string> log)
    {
        _player = player;
        _controls = controls;
        _log = log;
        controls.IsEnabled = true;
        controls.IsPlayEnabled = controls.IsPauseEnabled = controls.IsStopEnabled = true;
        controls.IsNextEnabled = controls.IsPreviousEnabled = true;
        controls.ButtonPressed += OnButtonPressed;
        player.PlayStateChanged += OnPlayStateChanged;
        player.CurrentMediaChanged += OnMediaChanged;
    }

    /// <summary>Connects to the window's media controls, or returns null where Windows doesn't offer them.</summary>
    public static MediaControls? TryCreate(IntPtr hwnd, MediaPlayer player, Action<string> log)
    {
        try
        {
            return new MediaControls(player, SystemMediaTransportControlsInterop.GetForWindow(hwnd), log);
        }
        catch (Exception ex)
        {
            log("Media controls unavailable: " + ex.Message);
            return null;
        }
    }

    private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                    _player.Play();
                    break;
                case SystemMediaTransportControlsButton.Pause:
                    _player.Pause();
                    break;
                case SystemMediaTransportControlsButton.Stop:
                    _player.Stop();
                    break;
                case SystemMediaTransportControlsButton.Next:
                    _player.Next();
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    _player.Previous();
                    break;
            }
        });
    }

    private void OnPlayStateChanged(object? sender, PlayState state) =>
        _controls.PlaybackStatus = state switch
        {
            PlayState.Playing or PlayState.ScanForward or PlayState.ScanReverse => MediaPlaybackStatus.Playing,
            PlayState.Paused => MediaPlaybackStatus.Paused,
            PlayState.Transitioning or PlayState.Buffering or PlayState.Waiting => MediaPlaybackStatus.Changing,
            PlayState.Stopped or PlayState.MediaEnded => MediaPlaybackStatus.Stopped,
            _ => MediaPlaybackStatus.Closed,
        };

    private async void OnMediaChanged(object? sender, EventArgs e)
    {
        try
        {
            var updater = _controls.DisplayUpdater;
            updater.ClearAll();
            if (_player.CurrentMedia is not { } item)
            {
                updater.Update();
                return;
            }

            if (_player.HasVideo)
            {
                updater.Type = MediaPlaybackType.Video;
                updater.VideoProperties.Title = item.Name;
            }
            else
            {
                updater.Type = MediaPlaybackType.Music;
                updater.MusicProperties.Title = item.Name;
                updater.MusicProperties.Artist = item.GetAttribute("Author");
                updater.MusicProperties.AlbumTitle = item.GetAttribute("Album");
            }

            if (_player.GetAlbumArt(item) is { Length: > 0 } art)
            {
                var stream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(art);
                    await writer.StoreAsync();
                    writer.DetachStream();
                }

                updater.Thumbnail = RandomAccessStreamReference.CreateFromStream(stream);
            }

            updater.Update();
        }
        catch (Exception ex)
        {
            _log("Media controls update failed: " + ex.Message);
        }
    }

    public void Dispose()
    {
        _controls.ButtonPressed -= OnButtonPressed;
        _player.PlayStateChanged -= OnPlayStateChanged;
        _player.CurrentMediaChanged -= OnMediaChanged;
        _controls.IsEnabled = false;
    }
}

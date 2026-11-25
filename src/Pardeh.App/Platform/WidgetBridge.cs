using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Pardeh.Core.Playback;

namespace Pardeh.App;

/// <summary>
/// The link between the player and the Windows 11 Widgets board provider (a separate packaged process, src/Pardeh.Widgets).
/// A local named pipe (current user only): the provider sends one line, "state" or "cmd NAME", and gets one JSON line back with
/// the playing state (title, artist, position, length, shuffle, repeat, favourite and a small cover as a data URI).
/// Commands: play, pause, playpause, next, prev, shuffle, repeat, fav, back10, fwd10, stop.
/// </summary>
internal sealed class WidgetBridge : IDisposable
{
    public const string PipeName = "PardehWidgetPipe";

    private readonly MediaPlayer _p;
    private readonly CancellationTokenSource _stop = new();
    private string? _coverFor;
    private string _coverUri = string.Empty;

    public WidgetBridge(MediaPlayer player)
    {
        _p = player;
        _ = Task.Run(() => Serve(_stop.Token));
    }

    public void Dispose() => _stop.Cancel();

    private async Task Serve(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token);
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
                var request = await reader.ReadLineAsync(token);
                var response = request is null ? "{}" : await Dispatcher.UIThread.InvokeAsync(() => Handle(request));
                var bytes = Encoding.UTF8.GetBytes(response + "\n");
                await pipe.WriteAsync(bytes, token);
                await pipe.FlushAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                await Task.Delay(500, token).ContinueWith(_ => { });
            }
        }
    }

    private string Handle(string request)
    {
        if (request.StartsWith("cmd ", StringComparison.Ordinal))
        {
            var playing = _p.PlayState is PlayState.Playing or PlayState.ScanForward or PlayState.ScanReverse;
            switch (request[4..].Trim())
            {
                case "play":
                    _p.Play();
                    break;
                case "pause":
                    _p.Pause();
                    break;
                case "playpause":
                    if (playing)
                        _p.Pause();
                    else
                        _p.Play();
                    break;
                case "next":
                    _p.Next();
                    break;
                case "prev":
                    _p.Previous();
                    break;
                case "stop":
                    _p.Stop();
                    break;
                case "shuffle":
                    _p.Shuffle = !_p.Shuffle;
                    break;
                case "repeat":
                    _p.Loop = !_p.Loop;
                    break;
                case "fav":
                    _p.ToggleFavorite();
                    break;
                case "back10":
                    _p.SeekBy(-10);
                    break;
                case "fwd10":
                    _p.SeekBy(10);
                    break;
            }
        }

        return State();
    }

    private string State()
    {
        var media = _p.CurrentMedia;
        var active = media is not null && _p.PlayState != PlayState.Stopped;
        var artist = active ? media!.GetAttribute("Author") : string.Empty;
        if (active && artist.Length == 0)
            artist = media!.GetAttribute("Album");
        return JsonSerializer.Serialize(new
        {
            active,
            title = active ? media!.Name : string.Empty,
            artist,
            playing = _p.PlayState is PlayState.Playing or PlayState.ScanForward or PlayState.ScanReverse,
            position = active ? _p.CurrentPosition : 0,
            duration = active ? _p.Duration : 0,
            shuffle = _p.Shuffle,
            repeat = _p.Loop,
            favorite = active && _p.IsFavorite(media),
            cover = active ? Cover(media!) : string.Empty,
        });
    }

    /// <summary>The cover as a small JPEG data URI (cached per track); empty when there is none.</summary>
    private string Cover(MediaItem media)
    {
        if (_coverFor == media.Source)
            return _coverUri;
        _coverFor = media.Source;
        _coverUri = string.Empty;
        try
        {
            if (_p.GetAlbumArt(media) is { Length: > 0 } data)
            {
                using var input = new MemoryStream(data);
                using var small = Bitmap.DecodeToWidth(input, 160);
                using var output = new MemoryStream();
                small.Save(output, 80);
                _coverUri = "data:image/jpeg;base64," + Convert.ToBase64String(output.ToArray());
            }
        }
        catch (Exception)
        {
            // Unreadable picture: the card shows the logo.
        }

        return _coverUri;
    }
}

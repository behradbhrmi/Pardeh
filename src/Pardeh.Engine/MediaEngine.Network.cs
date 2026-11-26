using Pardeh.Core.Playback;
using Pardeh.Ffmpeg;
using Pardeh.Nama;
using Pardeh.Nava;

namespace Pardeh.Engine;

/// <summary>
/// Network media: connecting happens in <see cref="PrepareOpen"/> off the interface thread; the sound is read ahead
/// (<see cref="ReadAheadSource"/>). A video decoder, with its own connection, is opened only when the address really has
/// a picture, so a radio station keeps a single connection.
/// </summary>
public sealed partial class MediaEngine
{
    public bool OpensInBackground(string source) => NetworkMedia.IsNetwork(source);

    public bool IsBuffering => _sound.IsBuffering;

    public int BufferingPercent => _sound.BufferingPercent;

    private sealed class Prepared : IDisposable
    {
        public IAudioSource? Audio { get; set; }

        public TimedVideoDecoder? Video { get; set; }

        /// <summary>The engine took the parts: disposing then leaves them alone.</summary>
        public bool Used { get; set; }

        public void Dispose()
        {
            if (Used)
                return;
            Used = true;
            Audio?.Dispose();
            Video?.Dispose();
        }
    }

    /// <summary>
    /// The address is opened as media. When that fails and the server turns out to have sent a web page, the video address
    /// the page publishes is opened instead (<see cref="PageVideo"/>). The item keeps the page's address, so the recent list
    /// and the saved positions never hold a video address that stops working later.
    /// </summary>
    public IDisposable PrepareOpen(string source, NetworkOptions options, CancellationToken cancel)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            return PrepareMedia(source, source, options, cancel);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            // A server that did not answer in time is not asked a second time.
            if (started.Elapsed >= options.ConnectTimeout || LookAtPage(source, options, cancel) is not { } page)
                throw;
            if (page.Video is not { } video)
                throw new InvalidDataException(PageVideo.NoAddressMessage, failure);
            try
            {
                return PrepareMedia(source, video, options, cancel);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                throw new InvalidDataException("The page gives an address for its video, but Pardeh could not play it. "
                    + MediaPlayer.FriendlyReason(ex.Message), ex);
            }
        }
    }

    /// <param name="source">The address the item is known by.</param>
    /// <param name="address">The address of the media itself (the same, unless it was found on the page at <paramref name="source"/>).</param>
    private IDisposable PrepareMedia(string source, string address, NetworkOptions options, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        var prepared = new Prepared();
        try
        {
            Exception? error = null;
            var tryVideo = false;
            var soundAddress = address;
            var pictureAddress = address;
            if (FfmpegLibrary.IsAvailable && ChooseFromHlsMaster(address, options, cancel) is { } choice)
            {
                soundAddress = choice.Sound;
                pictureAddress = choice.Picture ?? choice.Sound;
                tryVideo = choice.Picture is not null; // the sound playlist may be a separate one without a picture
            }

            if (FfmpegLibrary.IsAvailable)
            {
                var abort = new NetworkAbort();
                using var onCancel = cancel.Register(abort.Cancel);
                try
                {
                    abort.Limit(options.ConnectTimeout);
                    var decoder = new FfmpegAudioSource(soundAddress, -1, new NetworkInput(options, abort)); // owns the abort from here
                    abort.Limit(null);
                    tryVideo |= decoder.HasVideo;
                    var buffered = new ReadAheadSource(decoder, options.BufferSeconds, abort.Cancel);
                    prepared.Audio = buffered;
                    if (!buffered.WaitUntilReady(options.ConnectTimeout, cancel))
                    {
                        cancel.ThrowIfCancellationRequested();
                        throw new TimeoutException("The server sends too little to play.");
                    }
                }
                catch (NoAudioStreamException ex)
                {
                    error = ex;
                    tryVideo = true; // it may be a video without sound
                }
            }
            else
            {
                prepared.Audio = NavaPlayer.OpenSource(address, out error);
            }

            cancel.ThrowIfCancellationRequested();
            if (tryVideo && FfmpegLibrary.IsAvailable)
            {
                var abort = new NetworkAbort();
                using var onCancel = cancel.Register(abort.Cancel);
                prepared.Video = _picture.OpenNetwork(pictureAddress, new NetworkInput(options, abort));
            }

            cancel.ThrowIfCancellationRequested();
            if (prepared.Audio is null && prepared.Video is null)
                throw error ?? new InvalidDataException("No playable audio or video stream.");
            return prepared;
        }
        catch (Exception ex)
        {
            prepared.Dispose();
            if (cancel.IsCancellationRequested)
                throw new OperationCanceledException("Connecting was cancelled.", ex, cancel);
            throw;
        }
    }

    private const int MaxPageBytes = 3 * 1024 * 1024;

    /// <summary>What a web page offers: the video address it publishes, if any.</summary>
    private sealed record WebPage(string? Video);

    /// <summary>Null when the address is not a web page (or cannot be read): the first failure is then the one to report.</summary>
    private static WebPage? LookAtPage(string source, NetworkOptions options, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return null;
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            limit.CancelAfter(options.ConnectTimeout);
            using var http = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.TryParseAdd(options.UserAgent);
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");
            using var response = Send(http, request, limit.Token);
            if (!response.IsSuccessStatusCode
                || response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() is not ("text/html" or "application/xhtml+xml"))
            {
                return null;
            }

            // A long page is read as far as the limit: what describes it stands near the top.
            using var stream = response.Content.ReadAsStreamAsync(limit.Token).GetAwaiter().GetResult();
            var bytes = new byte[MaxPageBytes];
            var total = 0;
            int read;
            while (total < bytes.Length && (read = stream.ReadAsync(bytes.AsMemory(total), limit.Token).AsTask().GetAwaiter().GetResult()) > 0)
                total += read;
            var from = response.RequestMessage?.RequestUri?.ToString() ?? source; // after redirects
            return new WebPage(PageVideo.Find(from, System.Text.Encoding.UTF8.GetString(bytes, 0, total)));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or NotSupportedException or InvalidOperationException)
        {
            cancel.ThrowIfCancellationRequested();
            return null;
        }
    }

    /// <summary>
    /// Waits for the headers of an answer (this runs on a worker). Not HttpClient.Send: Android's handler does not have it,
    /// and the failure looked like an address that could not be read.
    /// </summary>
    private static HttpResponseMessage Send(HttpClient http, HttpRequestMessage request, CancellationToken cancel) =>
        http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).GetAwaiter().GetResult();

    private const int MaxMasterBytes = 1024 * 1024;

    /// <summary>
    /// For an HLS master playlist: the one size and the sound to open (<see cref="HlsMaster"/>). Null for anything else, and
    /// when the list cannot be read here: FFmpeg then gets the address as it is and reports what is wrong.
    /// </summary>
    private static HlsChoice? ChooseFromHlsMaster(string source, NetworkOptions options, CancellationToken cancel)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || !uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            limit.CancelAfter(options.ConnectTimeout);
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.TryParseAdd(options.UserAgent);
            using var response = Send(http, request, limit.Token);
            if (!response.IsSuccessStatusCode)
                return null;
            using var stream = response.Content.ReadAsStreamAsync(limit.Token).GetAwaiter().GetResult();
            var bytes = new byte[MaxMasterBytes];
            var total = 0;
            int read;
            while (total < bytes.Length && (read = stream.ReadAsync(bytes.AsMemory(total), limit.Token).AsTask().GetAwaiter().GetResult()) > 0)
                total += read;
            if (total >= bytes.Length)
                return null;
            var from = response.RequestMessage?.RequestUri?.ToString() ?? source; // after redirects
            return HlsMaster.Choose(from, System.Text.Encoding.UTF8.GetString(bytes, 0, total), options.MaxVideoHeight);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or NotSupportedException or InvalidOperationException)
        {
            cancel.ThrowIfCancellationRequested();
            return null;
        }
    }

    public void OpenPrepared(IDisposable media)
    {
        var prepared = (Prepared)media;
        Close();
        _picture.Show(prepared.Video);
        if (prepared.Audio is not { } audio)
        {
            prepared.Used = true;
            _picture.StartWithoutSound();
            return;
        }

        _sound.Start(audio); // when this throws, the caller disposes the prepared media
        prepared.Used = true;
    }
}

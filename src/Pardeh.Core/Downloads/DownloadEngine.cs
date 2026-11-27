using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Pardeh.Core.Changes;
using Pardeh.Core.Duplicates;

namespace Pardeh.Core.Downloads;

/// <summary>Where a checked download ends up.</summary>
public interface IDownloadTarget
{
    /// <summary>What the user is told, such as a folder path.</summary>
    string Description { get; }

    /// <summary>
    /// Copies the file in under <paramref name="fileName"/>, or under a free name next to it: nothing is ever overwritten,
    /// and no library or app sees half a file. Returns the new file as the library knows it.
    /// </summary>
    Task<string> PublishAsync(string localFile, string fileName, CancellationToken cancel);
}

/// <summary>A plain folder.</summary>
public sealed class FolderDownloadTarget(string folder) : IDownloadTarget
{
    public string Folder => folder;

    public string Description => folder;

    public Task<string> PublishAsync(string localFile, string fileName, CancellationToken cancel) => Task.Run(() =>
    {
        Directory.CreateDirectory(folder);
        var partial = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".pardehpart");
        try
        {
            SafeFiles.CopyNew(localFile, partial);
            cancel.ThrowIfCancellationRequested();
            for (var attempt = 0; ; attempt++)
            {
                var path = Path.Combine(folder, SafeFiles.FreeName(fileName, name => Path.Exists(Path.Combine(folder, name))));
                try
                {
                    File.Move(partial, path); // refuses a taken name
                    return path;
                }
                catch (IOException) when (attempt < 3 && Path.Exists(path))
                {
                    // Taken just now: the next free one.
                }
            }
        }
        finally
        {
            SafeFiles.TryDelete(partial);
        }
    }, cancel);
}

/// <summary>What the engine needs to say that a downloaded file really is a complete piece of audio.</summary>
/// <param name="Available">False where there is no decoder to check with: nothing is downloaded then.</param>
/// <param name="Scan">Reads the whole file; null when it can't be read to the end.</param>
public sealed record AudioCheck(Func<bool> Available, Func<string, CancellationToken, AudioScan?> Scan);

/// <param name="Done">Bytes there so far.</param>
public readonly record struct DownloadBytes(long Done, long? Total);

/// <summary>
/// Downloads one file the way a browser does a plain file: the address itself has to give audio. No page is read for
/// addresses, nothing signs in, and no cookie, referrer or browser name is sent.
/// </summary>
/// <param name="http">From <see cref="DownloadRules.CreateClient"/>: it must not follow redirects by itself.</param>
/// <param name="contactAgent">The agent with the project's address, sent to Wikimedia only; every other host gets the plain one.</param>
public sealed class DownloadEngine(HttpClient http, DownloadRules rules, AudioCheck audio, ITagEditor tags, string contactAgent)
{
    public const string PlainAgent = "Pardeh/1.0";

    private const int MaxHops = 5;

    /// <summary>How long a server may stay silent.</summary>
    internal TimeSpan Silence { get; set; } = TimeSpan.FromSeconds(30);

    public DownloadRules Rules => rules;

    public long MaxSize { get; init; } = DownloadRules.MaxSize;

    private static string Megabytes(long bytes) => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.#} MB");

    private string TooLarge => $"The file is larger than {Megabytes(MaxSize)}, which is the most Pardeh downloads.";

    private const string NoDecoder = "Downloads need FFmpeg, to check that a file really is audio. It isn't there.";

    // ---- asking ----------------------------------------------------------------------------------

    private async Task<(HttpResponseMessage Response, Uri Final)> SendAsync(Uri address, DownloadKind kind, Action<HttpRequestHeaders>? headers,
        CancellationToken cancel)
    {
        var current = address;
        for (var hop = 0; ; hop++)
        {
            await rules.CheckAsync(current, kind, cancel).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.TryAddWithoutValidation("User-Agent", DownloadRules.IsWikimedia(current.IdnHost) ? contactAgent : PlainAgent);
            request.Headers.TryAddWithoutValidation("Accept", "audio/*, application/ogg, application/octet-stream;q=0.8, */*;q=0.1");
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity"); // the bytes of the file, as they are
            headers?.Invoke(request.Headers);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(Silence);
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancel.IsCancellationRequested)
            {
                throw new DownloadException($"{current.IdnHost} did not answer in time.", ex) { CanResume = true };
            }
            catch (HttpRequestException ex)
            {
                // The connection check speaks through this one.
                if (ex.InnerException is DownloadException refused)
                    throw new DownloadException(refused.Message, ex) { CanResume = refused.CanResume };
                throw new DownloadException($"{current.IdnHost} couldn't be reached. Check the internet connection.", ex) { CanResume = true };
            }

            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308))
                return (response, current);
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null || hop >= MaxHops || !Uri.TryCreate(current, location, out var next))
                throw new DownloadException("The server sent the download round in a way that can't be followed.");
            current = next;
        }
    }

    private static DownloadException Refusal(HttpResponseMessage response, Uri from)
    {
        var status = (int)response.StatusCode;
        if (status is 401 or 402 or 403 or 407 or 451)
            return new DownloadException(string.Create(CultureInfo.InvariantCulture,
                $"{from.IdnHost} does not allow this download ({status}). Pardeh does not sign in or get around that."));
        if (status is 404 or 410)
            return new DownloadException($"There is no file at this address on {from.IdnHost}.");
        if (status == 429 || status >= 500)
        {
            var wait = response.Headers.RetryAfter?.Delta is { TotalSeconds: > 0 } asked
                ? string.Create(CultureInfo.InvariantCulture, $" It asks to wait {Math.Ceiling(asked.TotalSeconds):0} seconds.")
                : string.Empty;
            return new DownloadException($"{from.IdnHost} is busy.{wait} Try again later.") { CanResume = true };
        }

        return new DownloadException(string.Create(CultureInfo.InvariantCulture, $"{from.IdnHost} refused the request ({status})."));
    }

    private static string? StrongETag(HttpResponseMessage response) => response.Headers.ETag is { IsWeak: false } tag ? tag.Tag : null;

    private static async Task<int> ReadAsync(Stream body, Memory<byte> into, CancellationTokenSource silence, TimeSpan limit, CancellationToken cancel)
    {
        silence.CancelAfter(limit);
        try
        {
            return await body.ReadAsync(into, silence.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancel.IsCancellationRequested)
        {
            throw new DownloadException("The server stopped sending.", ex) { CanResume = true };
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            cancel.ThrowIfCancellationRequested();
            throw new DownloadException("The connection was lost.", ex) { CanResume = true };
        }
    }

    /// <summary>
    /// Looks at what an address gives, from its headers and first bytes, and drops the rest. Throws
    /// <see cref="DownloadIsPageException"/> for a web page and <see cref="DownloadException"/> for anything else that is
    /// not a file to download.
    /// </summary>
    public async Task<DownloadOffer> LookAsync(Uri address, DownloadKind kind, CancellationToken cancel)
    {
        if (!audio.Available())
            throw new DownloadException(NoDecoder);
        var (response, final) = await SendAsync(address, kind, null, cancel).ConfigureAwait(false);
        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
                throw Refusal(response, final);
            if (response.Content.Headers.ContentEncoding.Any(e => !e.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                throw new DownloadException("The server sends this file packed in a way Pardeh does not unpack.");
            var type = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? string.Empty;
            if (type is "text/html" or "application/xhtml+xml")
                throw new DownloadIsPageException(final);
            if (IsPlaylistType(type) || IsPlaylistName(final.AbsolutePath))
                throw new DownloadException(PlaylistRefused);

            var start = new byte[AudioSniffer.Enough];
            var got = 0;
            using (var silence = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            await using (var body = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false))
            {
                int read;
                while (got < start.Length && (read = await ReadAsync(body, start.AsMemory(got), silence, Silence, cancel).ConfigureAwait(false)) > 0)
                    got += read;
            }

            var found = AudioSniffer.Look(start.AsSpan(0, got));
            switch (found.Kind)
            {
                case SniffedKind.WebPage:
                    throw new DownloadIsPageException(final);
                case SniffedKind.Playlist:
                    throw new DownloadException(PlaylistRefused);
                case SniffedKind.Other:
                    throw new DownloadException(type.StartsWith("text/", StringComparison.Ordinal)
                        ? "This address gives text, not an audio file."
                        : "This is not an audio file Pardeh knows.");
            }

            var size = response.Content.Headers.ContentLength;
            if (size > MaxSize)
                throw new DownloadException(TooLarge);
            var stem = DownloadNames.Stem(DownloadNames.Suggested(response.Content.Headers.ContentDisposition, final));
            return new DownloadOffer(kind, address, final, stem, found.Extension, found.TypeName, size)
            {
                ETag = StrongETag(response),
                LastModified = response.Content.Headers.LastModified,
            };
        }
    }

    private const string PlaylistRefused = "This is a playlist or a stream, not an audio file. Pardeh does not turn those into files.";

    private static bool IsPlaylistType(string type) =>
        type.Contains("mpegurl", StringComparison.Ordinal) || type is "application/dash+xml" or "audio/x-scpls" or "application/pls+xml"
            or "application/xspf+xml" or "video/x-ms-asx" or "application/vnd.ms-wpl";

    private static bool IsPlaylistName(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".m3u" or ".m3u8" or ".pls" or ".mpd" or ".asx" or ".xspf" or ".wpl" or ".ism" or ".f4m";

    // ---- fetching ----------------------------------------------------------------------------------

    /// <summary>
    /// Fills <paramref name="partial"/> with the file, going on where it left off when the server allows that and the file
    /// there is still the same; otherwise from the start. Returns the job with what the server says now, and a note when
    /// it started again.
    /// </summary>
    /// <param name="changed">Told the job's new facts before bytes are written, so that a later resume compares with them.</param>
    public async Task<(DownloadJob Job, string? Note)> FetchAsync(DownloadJob job, string partial, Action<DownloadJob>? changed,
        IProgress<DownloadBytes>? progress, CancellationToken cancel)
    {
        if (!audio.Available())
            throw new DownloadException(NoDecoder);
        var address = new Uri(job.Address);
        var have = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (have > 0 && have == job.Size)
            return (job, null); // all of it is here: only the checks are left

        // Without something to tell one version of the file from another, a part of it can't be gone on with.
        var validator = job.ETag ?? job.LastModified?.ToString("R", CultureInfo.InvariantCulture);
        var resume = have > 0 && validator is not null && job.Size is not null && have < job.Size;
        string? note = have > 0 && !resume ? "The server can't say whether the file is the same, so it started again." : null;

        var (response, final) = await SendAsync(address, job.Kind, resume
            ? headers =>
            {
                headers.Range = new RangeHeaderValue(have, null);
                headers.TryAddWithoutValidation("If-Range", validator);
            }
            : null, cancel).ConfigureAwait(false);

        if (resume && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            response.Dispose();
            resume = false;
            note = "The file on the server changed, so it started again.";
            (response, final) = await SendAsync(address, job.Kind, null, cancel).ConfigureAwait(false);
        }

        using (response)
        {
            if (!string.Equals(final.IdnHost, job.Host, StringComparison.OrdinalIgnoreCase))
                throw new DownloadException($"The server now sends this file from {final.IdnHost} instead of {job.Host}. Look at the address again.");
            if (response.Content.Headers.ContentEncoding.Any(e => !e.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                throw new DownloadException("The server sends this file packed in a way Pardeh does not unpack.");

            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                var range = response.Content.Headers.ContentRange;
                var tag = StrongETag(response);
                if (!resume || range?.From != have || range.Length != job.Size || job.ETag is not null && tag is not null && tag != job.ETag)
                {
                    // Not the rest of the same file: nothing of it is put on the end of what is here.
                    SafeFiles.TryDelete(partial);
                    throw new DownloadException("The server sent a different part than the one asked for. Resume to start again.") { CanResume = true };
                }
            }
            else if (response.StatusCode == HttpStatusCode.OK)
            {
                if (resume)
                    note = "The file on the server changed, or it can't go on in the middle, so it started again.";
                have = 0;
                var size = response.Content.Headers.ContentLength;
                if (size > MaxSize)
                    throw new DownloadException(TooLarge);
                job = job with { Size = size ?? job.Size, ETag = StrongETag(response), LastModified = response.Content.Headers.LastModified };
                changed?.Invoke(job);
            }
            else
            {
                throw Refusal(response, final);
            }

            using var silence = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            await using var body = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            var done = have;
            await using (var file = new FileStream(partial, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var chunk = new byte[64 * 1024];
                int read;
                progress?.Report(new DownloadBytes(done, job.Size));
                while ((read = await ReadAsync(body, chunk, silence, Silence, cancel).ConfigureAwait(false)) > 0)
                {
                    done += read;
                    if (done > MaxSize)
                        throw new DownloadException(TooLarge);
                    if (done > job.Size)
                        throw new DownloadException("The server sent more than it said it would.");
                    await file.WriteAsync(chunk.AsMemory(0, read), cancel).ConfigureAwait(false);
                    progress?.Report(new DownloadBytes(done, job.Size));
                }
            }

            if (done < job.Size)
                throw new DownloadException("The connection ended before the whole file had come.") { CanResume = true };
            return (job, note);
        }
    }

    // ---- checking and adding -------------------------------------------------------------------------

    /// <summary>What the first bytes of a whole file say, looking past a tag in front, however large.</summary>
    internal static Sniffed SniffFile(string path)
    {
        using var file = File.OpenRead(path);
        var start = new byte[AudioSniffer.Enough];
        var got = file.ReadAtLeast(start, start.Length, throwOnEndOfStream: false);
        if (got >= 10 && start.AsSpan(0, 3).SequenceEqual("ID3"u8))
        {
            long after = 10 + ((start[6] & 0x7F) << 21 | (start[7] & 0x7F) << 14 | (start[8] & 0x7F) << 7 | start[9] & 0x7F) + ((start[5] & 0x10) != 0 ? 10 : 0);
            if (after + 4 > got && after < file.Length)
            {
                // An empty tag followed by what really comes after the long one.
                var moved = new byte[AudioSniffer.Enough];
                "ID3"u8.CopyTo(moved);
                moved[3] = start[3];
                file.Position = after;
                got = 10 + file.ReadAtLeast(moved.AsSpan(10), moved.Length - 10, throwOnEndOfStream: false);
                return AudioSniffer.Look(moved.AsSpan(0, got));
            }
        }

        return AudioSniffer.Look(start.AsSpan(0, got));
    }

    /// <summary>
    /// For WAV and AIFF, whose first bytes state the whole length: less than that is a file cut off. FFmpeg takes the
    /// length of what is there, so it can't tell. A length of 0 or of "unknown" says nothing.
    /// </summary>
    internal static bool ShorterThanItsHeader(string path)
    {
        using var file = File.OpenRead(path);
        Span<byte> head = stackalloc byte[8];
        if (file.ReadAtLeast(head, 8, throwOnEndOfStream: false) < 8)
            return false;
        uint stated;
        if (head[..4].SequenceEqual("RIFF"u8))
            stated = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(head[4..]);
        else if (head[..4].SequenceEqual("FORM"u8))
            stated = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(head[4..]);
        else
            return false;
        return stated is > 0 and < 0x7FFFFFFF && stated + 8L > file.Length + 1; // one byte of padding may be left off
    }

    /// <summary>
    /// Checks that the whole file is what it should be, puts a catalog file's licence into it, and adds it to
    /// <paramref name="target"/> under its real name. Throws <see cref="DownloadException"/> when a check fails; only one
    /// with <see cref="DownloadException.CanResume"/> leaves the downloaded file worth keeping.
    /// </summary>
    /// <param name="workFolder">A folder of the app's own, for the copies the checks and the tag writing use.</param>
    public async Task<DownloadRecord> CompleteAsync(DownloadJob job, string partial, string workFolder, IDownloadTarget target, Action? adding,
        CancellationToken cancel)
    {
        var length = new FileInfo(partial).Length;
        if (length == 0 || job.Size is { } expected && length != expected)
            throw new DownloadException("The downloaded file does not have the size the server gave.");
        var found = await Task.Run(() => SniffFile(partial), cancel).ConfigureAwait(false);
        if (found.Kind != SniffedKind.Audio)
            throw new DownloadException("What came is not an audio file.");

        var name = job.Stem + found.Extension;
        var check = Path.Combine(workFolder, job.Id + ".check" + found.Extension);
        var tagged = Path.Combine(workFolder, job.Id + ".tag" + found.Extension);
        try
        {
            await Task.Run(() =>
            {
                if (job.Sha1 is { Length: > 0 } sha1)
                {
                    using var stream = File.OpenRead(partial);
                    if (!Convert.ToHexString(SHA1.HashData(stream)).Equals(sha1, StringComparison.OrdinalIgnoreCase))
                        throw new DownloadException("The file does not match the checksum the catalog gives.");
                }

                File.Copy(partial, check, overwrite: true);
                var scan = audio.Scan(check, cancel);
                if (scan is null)
                    throw new DownloadException("The file can't be read as audio to the end: it may be damaged or cut off.");
                if (scan.HasVideo)
                    throw new DownloadException("This file has moving pictures. Only audio files are downloaded.");
                if (scan.Codec.Length == 0)
                    throw new DownloadException("The file has no sound in it.");
                if (scan.CutShort || ShorterThanItsHeader(check))
                    throw new DownloadException("The file holds less sound than it says: it is cut off.");
            }, cancel).ConfigureAwait(false);

            var publish = check;
            if (job.Credit is { NoDerivatives: false } credit && await Task.Run(() => WriteCredit(check, tagged, name, found.Extension, credit), cancel).ConfigureAwait(false))
                publish = tagged;

            cancel.ThrowIfCancellationRequested();
            adding?.Invoke();
            string source;
            try
            {
                source = await target.PublishAsync(publish, name, cancel).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                throw new DownloadException("The file couldn't be added: " + ex.Message, ex) { CanResume = true };
            }

            var address = job.MayBeSaved ? job.Address : Playback.NetworkMedia.Redact(job.Address);
            return new DownloadRecord(job.Id, DateTime.UtcNow, job.Kind, source, name, address, length) { Credit = job.Credit };
        }
        finally
        {
            SafeFiles.TryDelete(check);
            SafeFiles.TryDelete(tagged);
        }
    }

    // The licence and the file's page go into the file where it has room for them and holds nothing there already. A file
    // that can't take them safely stays as it came: the app's own record always has them.
    private bool WriteCredit(string from, string to, string name, string extension, DownloadCredit credit)
    {
        var sourceField = extension switch
        {
            ".mp3" => "id3v2:WOAS",
            ".ogg" or ".opus" or ".flac" => "xiph:SOURCEURL",
            _ => null,
        };
        foreach (var withSource in new[] { true, false })
        {
            try
            {
                File.Copy(from, to, overwrite: true);
                var now = tags.Read(to, name);
                var change = new TagFields
                {
                    Copyright = string.IsNullOrWhiteSpace(now.Copyright) ? credit.CopyrightLine : null,
                    Texts = withSource && sourceField is not null && now.TextOf(sourceField).Length == 0 ? [new TagText(sourceField, credit.SourcePage)] : null,
                };
                if (change.IsEmpty)
                    return false;
                return tags.Edit(to, name, change).Written;
            }
            catch (Exception ex) when (ex is TagEditException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Tried with less, then left as it was.
            }
        }

        return false;
    }
}

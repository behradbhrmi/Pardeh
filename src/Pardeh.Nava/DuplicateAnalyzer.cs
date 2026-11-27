using System.Security.Cryptography;
using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;
using Pardeh.Core.Duplicates;
using Pardeh.Core.Library;

namespace Pardeh.Nava;

/// <summary>The duplicate check's reading of a file: tags and stream details, and hashes of its sound and of all of it.</summary>
public sealed class DuplicateAnalyzer : IDuplicateAnalyzer
{
    // A few seconds of sound: enough to tell most different recordings apart without reading whole files.
    private const int StartPackets = 160;

    private static Stream? OpenStream(string source) =>
        MediaSources.IsContent(source) ? MediaSources.ContentOpener?.Invoke(source)
        : File.Exists(source) ? new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete) : null;

    public AudioFacts? Read(string source)
    {
        try
        {
            List<RawFrame>? raw;
            using (var stream = OpenStream(source))
            {
                if (stream is null)
                    return null;
                raw = RawId3.Read(stream);
            }

            var start = AudioDigest.Scan(source, StartPackets)?.Digest;
            // Some files that play well trip the tag reader up: over their length (then the tags alone are read), or
            // altogether (then only the sound and the file's name are compared).
            using var file = TryOpen(source, TagLib.ReadStyle.Average) ?? TryOpen(source, TagLib.ReadStyle.None);
            if (file is null)
                return start is null ? null : new AudioFacts(MediaSources.DisplayName(source), new TagFields(), null, 0, 0, 0, start);
            var tags = TagEditor.All(file, raw);
            var cover = tags.Pictures is { Length: > 0 } pictures ? pictures[0].Data : null;
            var properties = file.Properties;
            return new AudioFacts(MediaSources.DisplayName(source), tags with { Pictures = null }, cover is null ? null : CleanupAnalyzer.HashCover(cover),
                cover?.Length ?? 0, properties?.Duration.TotalSeconds ?? 0, properties?.AudioBitrate ?? 0, start);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null; // an unknown or damaged file
        }
    }

    private static TagLib.File? TryOpen(string source, TagLib.ReadStyle style)
    {
        try
        {
            return TagMediaInfoReader.Open(source, style);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    public AudioScan? Scan(string source, CancellationToken cancel)
    {
        try
        {
            return AudioDigest.Scan(source, 0, cancel);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            return null;
        }
    }

    public string? ContentHash(string source, CancellationToken cancel)
    {
        try
        {
            using var stream = OpenStream(source);
            if (stream is null)
                return null;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1 << 17];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancel.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, read);
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            return null;
        }
    }
}

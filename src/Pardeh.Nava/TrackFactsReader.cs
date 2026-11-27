using Pardeh.Core.Cleanup;
using Pardeh.Core.Library;

namespace Pardeh.Nava;

/// <summary>The tags and cover hash of a song for the local cleanup checks, from a path or a content URI.</summary>
public sealed class TrackFactsReader : ITrackFactsReader
{
    private static Stream? OpenStream(string source) =>
        MediaSources.IsContent(source) ? MediaSources.ContentOpener?.Invoke(source) : File.Exists(source) ? File.OpenRead(source) : null;

    public TrackFacts? Read(string source)
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

            using var file = TagMediaInfoReader.Open(source, TagLib.ReadStyle.None);
            if (file is null)
                return null;
            var tags = TagEditor.All(file, raw);
            var cover = tags.Pictures is { Length: > 0 } pictures ? CleanupAnalyzer.HashCover(pictures[0].Data) : null;
            // The pictures themselves are not kept: a hash tells them apart.
            return new TrackFacts(source, MediaSources.DisplayName(source), tags with { Pictures = null }, cover);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null; // an unknown or damaged file
        }
    }
}

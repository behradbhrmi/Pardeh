using Pardeh.Core.Library;
using Pardeh.Core.Playback;

namespace Pardeh.Nava;

/// <summary>Reads tags and stream info with TagLib#.</summary>
public sealed class TagMediaInfoReader : IMediaInfoReader
{
    /// <summary>Opens a file for TagLib: a path, or an Android content URI through a stream (the name gives the format).</summary>
    internal static TagLib.File? Open(string source, TagLib.ReadStyle style = TagLib.ReadStyle.Average)
    {
        if (MediaSources.IsContent(source))
        {
            if (MediaSources.ContentOpener?.Invoke(source) is not { } stream)
                return null;
            return TagLib.File.Create(new StreamAbstraction(MediaSources.DisplayName(source), stream), style);
        }

        return File.Exists(source) ? TagLib.File.Create(source, style) : null;
    }

    private sealed class StreamAbstraction(string name, Stream stream) : TagLib.File.IFileAbstraction
    {
        public string Name => name;

        public Stream ReadStream => stream;

        public Stream WriteStream => throw new NotSupportedException();

        public void CloseStream(Stream s) => s.Dispose();
    }

    public MediaItem Read(string source)
    {
        var item = new MediaItem(source);
        if (MediaSources.IsContent(source))
            item.Name = Path.GetFileNameWithoutExtension(MediaSources.DisplayName(source));
        try
        {
            using var file = Open(source);
            if (file is null)
                return item;
            var tag = file.Tag;

            if (!string.IsNullOrWhiteSpace(tag.Title))
            {
                item.Name = tag.Title;
                item.Attributes["Title"] = tag.Title;
            }

            var artist = tag.JoinedPerformers;
            if (string.IsNullOrWhiteSpace(artist))
                artist = tag.JoinedAlbumArtists;
            if (!string.IsNullOrWhiteSpace(artist))
                item.Attributes["Author"] = artist;
            if (!string.IsNullOrWhiteSpace(tag.Album))
                item.Attributes["Album"] = tag.Album;
            if (!string.IsNullOrWhiteSpace(tag.Copyright))
                item.Attributes["Copyright"] = tag.Copyright;
            if (!string.IsNullOrWhiteSpace(tag.JoinedGenres))
                item.Attributes["Genre"] = tag.JoinedGenres;
            if (tag.Year > 0)
                item.Attributes["Year"] = tag.Year.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (tag.Track > 0)
                item.Attributes["TrackNumber"] = tag.Track.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // ReplayGain (loudness) tags, kept as text for Core's ReplayGain class to read.
            void Gain(string key, double value)
            {
                if (double.IsFinite(value))
                    item.Attributes[key] = value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            }

            Gain(ReplayGain.TrackGain, tag.ReplayGainTrackGain);
            Gain(ReplayGain.TrackPeak, tag.ReplayGainTrackPeak);
            Gain(ReplayGain.AlbumGain, tag.ReplayGainAlbumGain);
            Gain(ReplayGain.AlbumPeak, tag.ReplayGainAlbumPeak);

            item.Duration = file.Properties.Duration;
            item.VideoWidth = file.Properties.VideoWidth;
            item.VideoHeight = file.Properties.VideoHeight;
        }
        catch (Exception)
        {
            // Unknown or damaged file: keep the file name only.
        }

        return item;
    }

    public byte[]? ReadPicture(string source)
    {
        try
        {
            using var file = Open(source);
            if (file is null)
                return null;
            var pictures = file.Tag.Pictures;
            var front = pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover) ?? pictures.FirstOrDefault();
            return front?.Data?.Data;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

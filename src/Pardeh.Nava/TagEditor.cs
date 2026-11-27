using System.Globalization;
using System.Security.Cryptography;
using Pardeh.Core.Changes;

namespace Pardeh.Nava;

/// <summary>
/// Tag writing with TagLib#, on a private copy of the file. Only the fields that differ are set; tag kinds the library adds
/// by itself are taken out again; and the result is read back and compared with the original.
/// </summary>
public sealed class TagEditor : ITagEditor
{
    /// <summary>The digest of the sound and picture data (tests replace it). Null result: not available for this file.</summary>
    public Func<string, string?> Digest { get; init; } = AudioDigest.Compute;

    // The working copy has an extension of its own, so the format is taken from the real name.
    private static TagLib.File Open(string path, string name) =>
        TagLib.File.Create(path, "taglib/" + Path.GetExtension(name).TrimStart('.').ToLowerInvariant(), TagLib.ReadStyle.Average);

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (TagLib.UnsupportedFormatException ex)
        {
            throw new TagEditException("This kind of file has no information Pardeh can change.", ex);
        }
        catch (TagLib.CorruptFileException ex)
        {
            throw new TagEditException("The file looks damaged, so it was left alone.", ex);
        }
        catch (Exception ex) when (ex is not (TagEditException or IOException or UnauthorizedAccessException or OutOfMemoryException))
        {
            throw new TagEditException("The file's information could not be read or written: " + ex.Message, ex);
        }
    }

    public TagFields Read(string path, string name) => Guard(() =>
    {
        var raw = Frames(path);
        using var file = Open(path, name);
        return All(file, raw);
    });

    /// <summary>Every field of an open file, the extra text fields included.</summary>
    internal static TagFields All(TagLib.File file, IReadOnlyList<RawFrame>? raw) => Fields(file.Tag) with { Texts = TagTexts.Read(file, raw) };

    private static List<RawFrame>? Frames(string path)
    {
        using var stream = File.OpenRead(path);
        return RawId3.Read(stream);
    }

    // The frames a change may touch; every other one must say the same afterwards.
    private static bool MayChange(string key, TagFields change)
    {
        bool Is(string id) => key == "id3v2:" + id;
        return change.Texts?.Any(t => t.Id == key) == true
            || change.Title is not null && Is("TIT2")
            || change.Artists is not null && Is("TPE1")
            || change.AlbumArtists is not null && Is("TPE2")
            || change.Album is not null && Is("TALB")
            || Is("TCON") // genre numbers are rewritten as names; the genres themselves are compared as a field
            || Is("TDRC") || Is("TYER") || Is("TDAT") // the date is compared as a whole
            || change.Track is not null && Is("TRCK")
            || change.Comment is not null && key.StartsWith("id3v2:COMM:", StringComparison.Ordinal) && key.EndsWith(':')
            || change.Composers is not null && Is("TCOM")
            || change.Copyright is not null && Is("TCOP")
            || change.Pictures is not null && Is("APIC");
    }

    // An independent look at the bytes: every frame the change has nothing to do with is still there.
    private static void CheckFrames(List<RawFrame> before, List<RawFrame> after, TagFields change)
    {
        if (change.Year is null && RawId3.Date(before) is { Length: > 0 } date && RawId3.Date(after) != date)
            throw new TagEditException("Writing would have changed the release date in the file, so it was left as it was.");
        var there = after.Select(RawId3.Describe).Where(d => d.Content is not null).ToList();
        foreach (var frame in before.Select(RawId3.Describe))
        {
            if (frame.Content is null || MayChange(frame.Key, change))
                continue;
            if (!there.Remove(frame))
                throw new TagEditException($"Writing would have changed other information in the file ({frame.Key}), so it was left as it was.");
        }
    }

    private static TagFields Fields(TagLib.Tag tag) => new()
    {
        Title = tag.Title ?? string.Empty,
        Artists = tag.Performers ?? [],
        AlbumArtists = tag.AlbumArtists ?? [],
        Album = tag.Album ?? string.Empty,
        Genres = tag.Genres ?? [],
        Year = tag.Year,
        Track = tag.Track,
        Comment = tag.Comment ?? string.Empty,
        Composers = tag.Composers ?? [],
        Copyright = tag.Copyright ?? string.Empty,
        Pictures = [.. (tag.Pictures ?? []).Select(p => new TagPicture(p.Type.ToString(), p.MimeType ?? string.Empty, p.Description ?? string.Empty, p.Data.Data))],
    };

    private static void Apply(TagLib.Tag tag, TagFields change)
    {
        static string? OrNone(string text) => text.Length == 0 ? null : text;
        if (change.Title is { } title)
            tag.Title = OrNone(title);
        if (change.Artists is { } artists)
            tag.Performers = artists;
        if (change.AlbumArtists is { } albumArtists)
            tag.AlbumArtists = albumArtists;
        if (change.Album is { } album)
            tag.Album = OrNone(album);
        if (change.Genres is { } genres)
            tag.Genres = genres;
        if (change.Year is { } year)
            tag.Year = year;
        if (change.Track is { } track)
            tag.Track = track;
        if (change.Comment is { } comment)
            tag.Comment = OrNone(comment);
        if (change.Composers is { } composers)
            tag.Composers = composers;
        if (change.Copyright is { } copyright)
            tag.Copyright = OrNone(copyright);
        if (change.Pictures is { } pictures)
        {
            tag.Pictures = [.. pictures.Select(p => (TagLib.IPicture)new TagLib.Picture(new TagLib.ByteVector(p.Data))
            {
                Type = Enum.TryParse<TagLib.PictureType>(p.Type, out var type) ? type : TagLib.PictureType.FrontCover,
                MimeType = p.MimeType,
                Description = p.Description,
            })];
        }
    }

    // Some containers don't keep a picture's type or description (MP4), so pictures are compared by their bytes.
    private static bool Arrived(TagFields read, TagFields wanted)
    {
        var missing = read.Differences(wanted);
        if (missing.Pictures is { } pictures && pictures.Length == (read.Pictures?.Length ?? 0)
            && pictures.Zip(read.Pictures ?? []).All(p => p.First.Data.AsSpan().SequenceEqual(p.Second.Data)))
            missing = missing with { Pictures = null };
        // With the comment cleared, another comment frame the file keeps is read in its place.
        if (missing.Comment is { Length: 0 } && wanted.Texts?.Any(t => t.Value == read.Comment) != true)
            missing = missing with { Comment = null };
        return missing.IsEmpty;
    }

    public TagEditResult Edit(string path, string name, TagFields change, long? restoreTagKinds = null) => Guard(() =>
    {
        var digest = Digest(path);
        TagFields before, effective;
        Dictionary<string, string> untouched;
        TagLib.TagTypes onDisk;
        TimeSpan duration;
        var raw = Frames(path);

        using (var file = Open(path, name))
        {
            if (file.PossiblyCorrupt)
                throw new TagEditException("The file looks damaged, so it was left alone.");
            before = All(file, raw);
            effective = before.Differences(change);
            onDisk = file.TagTypesOnDisk;
            duration = file.Properties?.Duration ?? TimeSpan.Zero;
            var unwanted = restoreTagKinds is { } keep ? onDisk & ~(TagLib.TagTypes)keep : TagLib.TagTypes.None;
            if (effective.IsEmpty && unwanted == TagLib.TagTypes.None)
                return new TagEditResult(new TagFields(), effective, (long)onDisk, Written: false);

            untouched = Untouched(file, onDisk, effective, frames: raw is null);

            // An MP3 with only the old ID3v1 tag can't hold long or non-Latin text: it gets an ID3v2 tag with the same values.
            var id3Container = file is TagLib.Mpeg.AudioFile or TagLib.Aac.File or TagLib.Riff.File or TagLib.Aiff.File;
            if (id3Container && restoreTagKinds is null && !onDisk.HasFlag(TagLib.TagTypes.Id3v2) && file.GetTag(TagLib.TagTypes.Id3v2, true) is { } id3
                && onDisk.HasFlag(TagLib.TagTypes.Id3v1) && file.GetTag(TagLib.TagTypes.Id3v1) is { } old)
                old.CopyTo(id3, false);

            // The extra fields go first: which comment frame is "the comment" is judged by the old one.
            var mainComment = file.Tag.Comment;
            if (effective.Texts is { } texts)
                TagTexts.Write(file, texts, mainComment);
            // TagLib# removes every comment frame when the comment is cleared: the others are set aside and put back.
            var otherComments = effective.Comment is not null && file.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag comments
                ? comments.GetFrames<TagLib.Id3v2.CommentsFrame>().Where(f => !string.IsNullOrEmpty(f.Description)).ToList()
                : [];
            Apply(file.Tag, effective);
            if (otherComments.Count > 0 && file.GetTag(TagLib.TagTypes.Id3v2, true) is TagLib.Id3v2.Tag kept3)
                foreach (var comment in otherComments.Where(c => !kept3.GetFrames<TagLib.Id3v2.CommentsFrame>().Contains(c)))
                    kept3.AddFrame(comment);

            if (restoreTagKinds is { } wanted)
            {
                var extra = file.TagTypes & ~(TagLib.TagTypes)wanted;
                if (extra != TagLib.TagTypes.None)
                    file.RemoveTags(extra);
            }
            else if (id3Container || onDisk != TagLib.TagTypes.None)
            {
                // TagLib adds tags nobody asked for (ID3v1 to MP3, which garbles non-Latin text; MovieID and DivX to WAV).
                var extra = file.TagTypes & ~onDisk;
                if (id3Container)
                    extra &= ~TagLib.TagTypes.Id3v2;
                if (extra != TagLib.TagTypes.None)
                    file.RemoveTags(extra);
            }

            if (file.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag saving)
            {
                if (raw is not null)
                    TagTexts.KeepAsTheyWere(saving, raw, effective.Year is not null);
                TagTexts.KeepUserUrls(saving, raw, effective.Texts ?? []);
            }
            file.Save();
        }

        var rawAfter = Frames(path);
        using (var written = Open(path, name))
        {
            if (!Arrived(All(written, rawAfter), effective))
                throw new TagEditException("This kind of file could not store the new details, so it was left as it was.");
            var kept = restoreTagKinds is { } wanted ? onDisk & (TagLib.TagTypes)wanted : onDisk;
            if (raw is not null && kept.HasFlag(TagLib.TagTypes.Id3v2))
                CheckFrames(raw, rawAfter ?? [], effective);
            var now = Untouched(written, kept, effective, frames: raw is null);
            foreach (var (key, value) in untouched)
            {
                // Data of a tag kind an undo removes goes with it.
                if (restoreTagKinds is not null && !IsOfKinds(key, kept))
                    continue;
                if (!now.TryGetValue(key, out var after) || after != value)
                    throw new TagEditException($"Writing would have changed other information in the file ({key}), so it was left as it was.");
            }

            if (digest is not null)
            {
                if (Digest(path) != digest)
                    throw new TagEditException("Writing would have changed the sound itself, so the file was left as it was.");
            }
            else if (Math.Abs(((written.Properties?.Duration ?? TimeSpan.Zero) - duration).TotalSeconds) > 0.5)
            {
                throw new TagEditException("Writing would have changed the length of the track, so the file was left as it was.");
            }
        }

        return new TagEditResult(before.Matching(effective), effective, (long)onDisk, Written: true);
    });

    private static bool IsOfKinds(string key, TagLib.TagTypes kinds) =>
        (!key.StartsWith("id3v2:", StringComparison.Ordinal) || kinds.HasFlag(TagLib.TagTypes.Id3v2))
        && (!key.StartsWith("xiph:", StringComparison.Ordinal) || kinds.HasFlag(TagLib.TagTypes.Xiph));

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    /// <summary>Everything in the tags that the change must leave as it is.</summary>
    /// <param name="frames">Compare ID3v2 frames as TagLib# sees them: only for a tag <see cref="RawId3"/> can't read.</param>
    private static Dictionary<string, string> Untouched(TagLib.File file, TagLib.TagTypes kinds, TagFields change, bool frames)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        var tag = file.Tag;

        void Field(string key, bool changing, object? value)
        {
            var text = value switch
            {
                null => string.Empty,
                string[] list => string.Join("\u001f", list),
                double number => double.IsNaN(number) ? string.Empty : number.ToString("R", CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            };
            if (!changing && text.Length > 0 && text != "0")
                found[key] = text;
        }

        Field("title", change.Title is not null, tag.Title);
        Field("artists", change.Artists is not null, tag.Performers);
        Field("album artists", change.AlbumArtists is not null, tag.AlbumArtists);
        Field("album", change.Album is not null, tag.Album);
        Field("genres", change.Genres is not null, tag.Genres);
        Field("year", change.Year is not null, tag.Year);
        Field("track", change.Track is not null, tag.Track);
        Field("comment", change.Comment is not null, tag.Comment);
        Field("composers", change.Composers is not null, tag.Composers);
        Field("copyright", change.Copyright is not null, tag.Copyright);
        Field("track count", false, tag.TrackCount);
        Field("disc", false, tag.Disc);
        Field("disc count", false, tag.DiscCount);
        Field("lyrics", false, tag.Lyrics);
        var texts = new HashSet<string>(change.Texts?.Select(t => t.Id) ?? [], StringComparer.Ordinal);
        Field("conductor", texts.Overlaps(["id3v2:TPE3", "xiph:CONDUCTOR", "asf:WM/Conductor"]), tag.Conductor);
        Field("grouping", texts.Overlaps(["id3v2:TIT1", "xiph:GROUPING", "asf:WM/ContentGroupDescription", "apple:©grp"]), tag.Grouping);
        Field("tempo", false, tag.BeatsPerMinute);
        Field("track gain", false, tag.ReplayGainTrackGain);
        Field("track peak", false, tag.ReplayGainTrackPeak);
        Field("album gain", false, tag.ReplayGainAlbumGain);
        Field("album peak", false, tag.ReplayGainAlbumPeak);
        Field("MusicBrainz track", false, tag.MusicBrainzTrackId);
        Field("MusicBrainz release", false, tag.MusicBrainzReleaseId);
        Field("MusicBrainz artist", false, tag.MusicBrainzArtistId);
        if (change.Pictures is null)
            Field("pictures", false, string.Join(',', (tag.Pictures ?? []).Select(p => Hash(p.Data.Data))));

        // Frames and fields TagLib has no property for (ratings, private data, custom text) are compared one by one.
        if (frames && kinds.HasFlag(TagLib.TagTypes.Id3v2) && file.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3)
        {
            var changing = new HashSet<string>(StringComparer.Ordinal);
            void Frames(bool when, params string[] ids)
            {
                if (when)
                    changing.UnionWith(ids);
            }

            Frames(change.Title is not null, "TIT2");
            Frames(change.Artists is not null, "TPE1");
            Frames(change.AlbumArtists is not null, "TPE2");
            Frames(change.Album is not null, "TALB");
            Frames(change.Genres is not null, "TCON");
            Frames(change.Year is not null, "TDRC", "TYER", "TDAT", "TIME");
            Frames(change.Track is not null, "TRCK");
            Frames(change.Comment is not null, "COMM");
            Frames(change.Composers is not null, "TCOM");
            Frames(change.Copyright is not null, "TCOP");
            Frames(change.Pictures is not null, "APIC");
            var main = tag.Comment;
            foreach (var group in id3.GetFrames()
                         .Select(f => (Id: f.FrameId.ToString(), Frame: f))
                         .Where(f => !changing.Contains(f.Id) && !(TagTexts.IdOf(f.Frame, main) is { } id && texts.Contains(id)))
                         .GroupBy(f => f.Id))
            {
                var contents = group.Select(f => Hash(f.Frame.Render(4).Data)).Order(StringComparer.Ordinal);
                found[$"id3v2:{group.Key}"] = string.Join(',', contents);
            }
        }

        if (kinds.HasFlag(TagLib.TagTypes.Xiph) && file.GetTag(TagLib.TagTypes.Xiph, false) is TagLib.Ogg.XiphComment xiph)
        {
            var changing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Keys(bool when, params string[] keys)
            {
                if (when)
                    changing.UnionWith(keys);
            }

            Keys(change.Title is not null, "TITLE");
            Keys(change.Artists is not null, "ARTIST");
            Keys(change.AlbumArtists is not null, "ALBUMARTIST", "ALBUM ARTIST");
            Keys(change.Album is not null, "ALBUM");
            Keys(change.Genres is not null, "GENRE");
            Keys(change.Year is not null, "DATE", "YEAR");
            Keys(change.Track is not null, "TRACKNUMBER");
            Keys(change.Comment is not null, "COMMENT", "DESCRIPTION");
            Keys(change.Composers is not null, "COMPOSER");
            Keys(change.Copyright is not null, "COPYRIGHT");
            Keys(change.Pictures is not null, "METADATA_BLOCK_PICTURE", "COVERART", "COVERARTMIME");
            changing.UnionWith(texts.Where(t => t.StartsWith("xiph:", StringComparison.Ordinal)).Select(t => t[5..]));
            foreach (string key in xiph)
            {
                // A picture may be rewritten in another form; the pictures are compared above.
                if (!changing.Contains(key) && key is not ("METADATA_BLOCK_PICTURE" or "COVERART" or "COVERARTMIME"))
                    found[$"xiph:{key.ToUpperInvariant()}"] = string.Join("\u001f", xiph.GetField(key));
            }
        }

        return found;
    }
}

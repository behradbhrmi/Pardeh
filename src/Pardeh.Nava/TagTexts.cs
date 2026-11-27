using Pardeh.Core.Changes;

namespace Pardeh.Nava;

/// <summary>
/// The text fields that have no property of their own, where download sites like to put their address: ID3v2 text, web
/// address, custom and extra comment frames, Vorbis comments, WMA attributes and a few MP4 boxes. Lyrics, ratings,
/// pictures, loudness and identifiers are not among them.
/// </summary>
internal static class TagTexts
{
    private const char Separator = TagText.ValueSeparator;

    // ID3v2 text and address frames that hold free text and have no field of their own.
    private static readonly HashSet<string> TextFrames = new(StringComparer.Ordinal)
    {
        "TPUB", "TENC", "TSSE", "TOPE", "TEXT", "TOAL", "TOLY", "TOWN", "TPE3", "TPE4", "TIT1", "TIT3", "TRSN", "TRSO", "TFLT", "TOFN",
    };

    // WXXX, the user's own address frame, is handled from the raw bytes.
    private static readonly HashSet<string> UrlFrames = new(StringComparer.Ordinal) { "WCOM", "WCOP", "WOAF", "WOAR", "WOAS", "WORS", "WPAY", "WPUB" };

    // Vorbis keys that have a field of their own or are not free text.
    private static readonly HashSet<string> HandledXiph = new(StringComparer.OrdinalIgnoreCase)
    {
        "TITLE", "ARTIST", "ALBUMARTIST", "ALBUM ARTIST", "ALBUM", "GENRE", "DATE", "YEAR", "TRACKNUMBER", "TRACKTOTAL", "TOTALTRACKS", "DISCNUMBER",
        "DISCTOTAL", "TOTALDISCS", "COMMENT", "DESCRIPTION", "COMPOSER", "COPYRIGHT", "LYRICS", "UNSYNCEDLYRICS", "SYNCEDLYRICS",
        "METADATA_BLOCK_PICTURE", "COVERART", "COVERARTMIME", "BPM",
    };

    private static readonly string[] AsfNames = ["WM/Publisher", "WM/AuthorURL", "WM/PromotionURL", "WM/AlbumArtistSortOrder", "WM/EncodedBy", "WM/ToolName", "WM/Writer", "WM/Conductor", "WM/ContentGroupDescription", "WM/SubTitle", "Rating"];

    private static readonly string[] AppleNames = ["©too", "desc", "ldes", "©pub", "©url", "©enc", "©grp", "©wrk"];

    private static bool Machine(string name) =>
        name.StartsWith("REPLAYGAIN", StringComparison.OrdinalIgnoreCase) || name.StartsWith("MUSICBRAINZ", StringComparison.OrdinalIgnoreCase)
        || name.Contains("MusicBrainz", StringComparison.OrdinalIgnoreCase) || name.StartsWith("ACOUSTID", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("iTun", StringComparison.Ordinal) || name.Contains("LYRICS", StringComparison.OrdinalIgnoreCase)
        || name.Equals("USLT", StringComparison.OrdinalIgnoreCase);

    private static ByteVectorKey Apple(string name) => new(name);

    private readonly record struct ByteVectorKey(string Name)
    {
        // MP4 box names are four Latin-1 bytes.
        public TagLib.ByteVector Bytes => new(System.Text.Encoding.Latin1.GetBytes(Name));
    }

    /// <summary>The id of an ID3v2 frame that is one of these fields, or null.</summary>
    public static string? IdOf(TagLib.Id3v2.Frame frame, string? mainComment)
    {
        var id = frame.FrameId.ToString();
        switch (frame)
        {
            case TagLib.Id3v2.UserTextInformationFrame custom:
                return Machine(custom.Description ?? string.Empty) ? null : "id3v2:TXXX:" + custom.Description;
            case TagLib.Id3v2.TextInformationFrame when TextFrames.Contains(id):
                return "id3v2:" + id;
            case TagLib.Id3v2.UrlLinkFrame when UrlFrames.Contains(id):
                return "id3v2:" + id;
            case TagLib.Id3v2.CommentsFrame comment when !Machine(comment.Description ?? string.Empty):
                // The comment without a description that TagLib reads as "the comment" has its own field.
                return string.IsNullOrEmpty(comment.Description) && comment.Text == mainComment ? null : $"id3v2:COMM:{comment.Language}:{comment.Description}";
            default:
                return null;
        }
    }

    /// <param name="raw">The ID3v2 frames as they are in the file: TagLib# can't read user web address frames.</param>
    public static TagText[] Read(TagLib.File file, IReadOnlyList<RawFrame>? raw)
    {
        var found = new SortedDictionary<string, string>(StringComparer.Ordinal);

        void Add(string id, IEnumerable<string?> values)
        {
            var text = string.Join(Separator, values.Where(v => !string.IsNullOrEmpty(v)));
            if (text.Length == 0)
                return;
            found[id] = found.TryGetValue(id, out var before) ? before + Separator + text : text;
        }

        if (file.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3)
        {
            var main = file.Tag.Comment;
            foreach (var frame in id3.GetFrames())
            {
                if (IdOf(frame, main) is not { } id)
                    continue;
                Add(id, frame switch
                {
                    TagLib.Id3v2.UserTextInformationFrame custom => custom.Text,
                    TagLib.Id3v2.TextInformationFrame text => text.Text,
                    TagLib.Id3v2.UrlLinkFrame url => url.Text,
                    TagLib.Id3v2.CommentsFrame comment => [comment.Text],
                    _ => [],
                });
            }
        }

        foreach (var frame in raw ?? [])
        {
            if (frame.Id == "WXXX" && RawId3.UserUrl(frame) is var (description, url))
                Add(UserUrlPrefix + description, [url]);
        }

        if (file.GetTag(TagLib.TagTypes.Xiph, false) is TagLib.Ogg.XiphComment xiph)
        {
            foreach (string key in xiph)
                if (!HandledXiph.Contains(key) && !Machine(key))
                    Add("xiph:" + key.ToUpperInvariant(), xiph.GetField(key));
        }

        if (file.GetTag(TagLib.TagTypes.Asf, false) is TagLib.Asf.Tag asf)
        {
            foreach (var name in AsfNames)
                Add("asf:" + name, asf.GetDescriptors(name).Where(d => d.Type == TagLib.Asf.DataType.Unicode).Select(d => d.ToString()));
        }

        if (file.GetTag(TagLib.TagTypes.Apple, false) is TagLib.Mpeg4.AppleTag apple)
        {
            foreach (var name in AppleNames)
                Add("apple:" + name, apple.GetText(Apple(name).Bytes));
        }

        return [.. found.Select(f => new TagText(f.Key, f.Value))];
    }

    public static void Write(TagLib.File file, IEnumerable<TagText> changes, string? mainComment)
    {
        foreach (var change in changes)
        {
            var values = change.Value.Length == 0 ? [] : change.Value.Split(Separator);
            if (change.Id.StartsWith(UserUrlPrefix, StringComparison.Ordinal))
                continue; // see KeepUserUrls
            var parts = change.Id.Split(':', 2);
            var name = parts.Length == 2 ? parts[1] : string.Empty;
            switch (parts[0])
            {
                case "id3v2" when file.GetTag(TagLib.TagTypes.Id3v2, values.Length > 0) is TagLib.Id3v2.Tag id3:
                    WriteId3(id3, change.Id, name, values, mainComment);
                    break;
                case "xiph" when file.GetTag(TagLib.TagTypes.Xiph, values.Length > 0) is TagLib.Ogg.XiphComment xiph:
                    if (values.Length == 0)
                        xiph.RemoveField(name);
                    else
                        xiph.SetField(name, values);
                    break;
                case "asf" when file.GetTag(TagLib.TagTypes.Asf, values.Length > 0) is TagLib.Asf.Tag asf:
                    asf.RemoveDescriptors(name);
                    foreach (var value in values)
                        asf.AddDescriptor(new TagLib.Asf.ContentDescriptor(name, value));
                    break;
                case "apple" when file.GetTag(TagLib.TagTypes.Apple, values.Length > 0) is TagLib.Mpeg4.AppleTag apple:
                    if (values.Length == 0)
                        apple.ClearData(Apple(name).Bytes);
                    else
                        apple.SetText(Apple(name).Bytes, values);
                    break;
            }
        }
    }

    private const string UserUrlPrefix = "id3v2:WXXX:";

    /// <summary>
    /// What TagLib# alters even when nothing asked it to. Producer credits in a version 2.3 tag lose a name: they are put
    /// back byte for byte. A full release date (TDRC) in a version 2.3 tag, which has no such frame, comes back with day
    /// and month the wrong way round: it is rewritten here as that version's year and day-month frames.
    /// </summary>
    public static void KeepAsTheyWere(TagLib.Id3v2.Tag id3, IReadOnlyList<RawFrame> raw, bool changingYear)
    {
        var credits = raw.Where(f => f.Id == "TIPL" && !f.Encoded).ToList();
        if (credits.Count > 0)
        {
            id3.RemoveFrames("TIPL");
            foreach (var frame in credits)
                id3.AddFrame(Raw("TIPL", frame.Body));
        }

        if (changingYear || id3.Version != 3 || raw.FirstOrDefault(f => f.Id == "TDRC") is not { } recorded
            || RawId3.Describe(recorded).Content is not { Length: >= 10 } date)
            return;
        foreach (var id in new[] { "TDRC", "TYER", "TDAT" })
            id3.RemoveFrames(id);
        id3.AddFrame(Raw("TYER", [0, .. System.Text.Encoding.Latin1.GetBytes(date[..4])]));
        id3.AddFrame(Raw("TDAT", [0, .. System.Text.Encoding.Latin1.GetBytes(date.Substring(8, 2) + date.Substring(5, 2))]));
    }

    private static TagLib.Id3v2.UnknownFrame Raw(string id, byte[] body) => new(id, new TagLib.ByteVector(body));

    /// <summary>
    /// TagLib# empties user web address frames when it saves. So before every save they are put back by hand: those not
    /// being changed byte for byte as they were, the changed ones built anew.
    /// </summary>
    public static void KeepUserUrls(TagLib.Id3v2.Tag id3, IReadOnlyList<RawFrame>? raw, IReadOnlyList<TagText> changes)
    {
        var changed = changes.Where(c => c.Id.StartsWith(UserUrlPrefix, StringComparison.Ordinal))
            .ToDictionary(c => c.Id[UserUrlPrefix.Length..], c => c.Value, StringComparer.Ordinal);
        if (raw is null)
        {
            if (changed.Count > 0 || id3.GetFrames().Any(f => f.FrameId.ToString() == "WXXX"))
                throw new TagEditException("This file has an old kind of tag with a web address that writing would lose, so it was left alone.");
            return;
        }

        var originals = raw.Where(f => f.Id == "WXXX").ToList();
        if (originals.Count == 0 && changed.Count == 0)
            return;
        if (originals.Any(f => f.Encoded))
            throw new TagEditException("This file has a packed web address that writing would lose, so it was left alone.");

        var bodies = originals.Where(f => !changed.ContainsKey(RawId3.UserUrl(f).Description)).Select(f => f.Body).ToList();
        foreach (var (description, value) in changed)
            bodies.AddRange(value.Split(Separator, StringSplitOptions.RemoveEmptyEntries).Select(url => RawId3.UserUrlBody(description, url)));

        id3.RemoveFrames("WXXX");
        foreach (var body in bodies)
            id3.AddFrame(new TagLib.Id3v2.UnknownFrame("WXXX", new TagLib.ByteVector(body)));
    }

    private static void WriteId3(TagLib.Id3v2.Tag id3, string wholeId, string name, string[] values, string? mainComment)
    {
        var existing = id3.GetFrames().Where(f => IdOf(f, mainComment) == wholeId).ToList();
        if (values.Length == 0)
        {
            foreach (var frame in existing)
                id3.RemoveFrame(frame);
            return;
        }

        var frameId = name.Split(':', 2)[0];
        var rest = name.Length > frameId.Length ? name[(frameId.Length + 1)..] : string.Empty;
        if (frameId == "COMM")
        {
            var parts = rest.Split(':', 2);
            var (language, description) = (parts[0], parts.Length > 1 ? parts[1] : string.Empty);
            foreach (var frame in existing)
                id3.RemoveFrame(frame);
            foreach (var value in values)
                id3.AddFrame(new TagLib.Id3v2.CommentsFrame(description, language, TagLib.StringType.UTF8) { Text = value });
            return;
        }

        if (UrlFrames.Contains(frameId))
        {
            foreach (var frame in existing)
                id3.RemoveFrame(frame);
            foreach (var value in values)
            {
                var url = TagLib.Id3v2.UrlLinkFrame.Get(id3, TagLib.ByteVector.FromString(frameId, TagLib.StringType.Latin1), true);
                url.Text = [value];
            }

            return;
        }

        // A text frame that is there is changed in place, which keeps its encoding.
        if (existing.OfType<TagLib.Id3v2.TextInformationFrame>().FirstOrDefault() is { } present)
        {
            present.Text = values;
            foreach (var extra in existing.Where(f => !ReferenceEquals(f, present)))
                id3.RemoveFrame(extra);
        }
        else if (existing.OfType<TagLib.Id3v2.UrlLinkFrame>().FirstOrDefault() is { } link)
        {
            link.Text = values;
        }
        else if (frameId == "TXXX")
        {
            TagLib.Id3v2.UserTextInformationFrame.Get(id3, rest, true).Text = values;
        }
        else
        {
            id3.SetTextFrame(TagLib.ByteVector.FromString(frameId, TagLib.StringType.Latin1), values);
        }
    }
}

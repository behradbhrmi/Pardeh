using System.Text.Json.Serialization;

namespace Pardeh.Core.Changes;

/// <summary>An embedded picture. <see cref="Type"/> is the ID3 picture type name ("FrontCover").</summary>
public sealed record TagPicture(string Type, string MimeType, string Description, byte[] Data)
{
    public bool Equals(TagPicture? other) =>
        other is not null && Type == other.Type && MimeType == other.MimeType && Description == other.Description && Data.AsSpan().SequenceEqual(other.Data);

    public override int GetHashCode() => HashCode.Combine(Type, MimeType, Description, Data.Length);
}

/// <summary>
/// A text field without a property of its own (publisher, a web address, a custom or second comment). <see cref="Id"/> says
/// which tag and field it is ("id3v2:TPUB", "id3v2:TXXX:description", "xiph:ORGANIZATION"); several values of one field
/// are joined by <see cref="ValueSeparator"/>. An empty value removes the field.
/// </summary>
public sealed record TagText(string Id, string Value)
{
    public const char ValueSeparator = '\u001f';
}

/// <summary>
/// A set of tag values: as a change, null means "leave it alone", an empty text, an empty list or 0 means "clear it".
/// </summary>
public sealed record TagFields
{
    public string? Title { get; init; }
    public string[]? Artists { get; init; }
    public string[]? AlbumArtists { get; init; }
    public string? Album { get; init; }
    public string[]? Genres { get; init; }
    public uint? Year { get; init; }
    public uint? Track { get; init; }
    public string? Comment { get; init; }
    public string[]? Composers { get; init; }
    public string? Copyright { get; init; }
    public TagPicture[]? Pictures { get; init; }

    /// <summary>The other text fields. As values: all of them; as a change: only the ones listed are set.</summary>
    public TagText[]? Texts { get; init; }

    [JsonIgnore]
    public bool IsEmpty => Title is null && Artists is null && AlbumArtists is null && Album is null && Genres is null && Year is null
        && Track is null && Comment is null && Composers is null && Copyright is null && Pictures is null && Texts is null;

    public string TextOf(string id) => Texts?.FirstOrDefault(t => t.Id == id)?.Value ?? string.Empty;

    /// <summary>The fields of <paramref name="change"/> that differ from these (current) values.</summary>
    public TagFields Differences(TagFields change)
    {
        static string? Text(string? now, string? wanted) => wanted is null || wanted == (now ?? string.Empty) ? null : wanted;
        static T[]? List<T>(T[]? now, T[]? wanted) => wanted is null || wanted.SequenceEqual(now ?? []) ? null : wanted;
        static uint? Number(uint? now, uint? wanted) => wanted is null || wanted == (now ?? 0) ? null : wanted;
        return new TagFields
        {
            Title = Text(Title, change.Title),
            Artists = List(Artists, change.Artists),
            AlbumArtists = List(AlbumArtists, change.AlbumArtists),
            Album = Text(Album, change.Album),
            Genres = List(Genres, change.Genres),
            Year = Number(Year, change.Year),
            Track = Number(Track, change.Track),
            Comment = Text(Comment, change.Comment),
            Composers = List(Composers, change.Composers),
            Copyright = Text(Copyright, change.Copyright),
            Pictures = List(Pictures, change.Pictures),
            Texts = change.Texts?.Where(t => t.Value != TextOf(t.Id)).ToArray() is { Length: > 0 } texts ? texts : null,
        };
    }

    /// <summary>These (current) values of exactly the fields <paramref name="change"/> sets: what undoes it.</summary>
    public TagFields Matching(TagFields change) => new()
    {
        Title = change.Title is null ? null : Title ?? string.Empty,
        Artists = change.Artists is null ? null : Artists ?? [],
        AlbumArtists = change.AlbumArtists is null ? null : AlbumArtists ?? [],
        Album = change.Album is null ? null : Album ?? string.Empty,
        Genres = change.Genres is null ? null : Genres ?? [],
        Year = change.Year is null ? null : Year ?? 0,
        Track = change.Track is null ? null : Track ?? 0,
        Comment = change.Comment is null ? null : Comment ?? string.Empty,
        Composers = change.Composers is null ? null : Composers ?? [],
        Copyright = change.Copyright is null ? null : Copyright ?? string.Empty,
        Pictures = change.Pictures is null ? null : Pictures ?? [],
        Texts = change.Texts?.Select(t => new TagText(t.Id, TextOf(t.Id))).ToArray(),
    };
}

/// <param name="Before">The old values of the fields that were changed.</param>
/// <param name="Applied">The fields that differed and were written (empty: the file was not touched).</param>
/// <param name="TagKindsBefore">The kinds of tag the file had (a bit set of the editor's), so an undo can remove added ones.</param>
public sealed record TagEditResult(TagFields Before, TagFields Applied, long TagKindsBefore, bool Written);

/// <summary>A tag change that could not be made safely. The file given to the editor must then be thrown away.</summary>
public sealed class TagEditException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Reads and writes the tags of a local file. It only ever edits a private working copy.</summary>
public interface ITagEditor
{
    /// <summary>The current values of every field. <paramref name="name"/> is the file's real name (the format comes from its extension).</summary>
    TagFields Read(string path, string name);

    /// <summary>
    /// Writes the fields of <paramref name="change"/> that differ into the copy at <paramref name="path"/>, then reads it back and
    /// checks that the new values are there, that no other tag data changed and that the audio is the same. Throws
    /// <see cref="TagEditException"/> otherwise.
    /// </summary>
    /// <param name="restoreTagKinds">For an undo: the tag kinds the file had before; kinds not among them are removed.</param>
    TagEditResult Edit(string path, string name, TagFields change, long? restoreTagKinds = null);
}

using Pardeh.Core.Changes;
using Pardeh.UI;

namespace Pardeh.Skinning.Tests;

public sealed class TagFormTests
{
    private static readonly TagFields Current = new()
    {
        Title = "Title",
        Artists = ["First", "Second"],
        AlbumArtists = [],
        Album = "Album",
        Genres = ["Pop"],
        Year = 1975,
        Track = 0,
    };

    private static (TagFields? Change, string? Error) Enter(int field, string value)
    {
        var shown = TagForm.Values(Current);
        var entered = shown.ToArray();
        entered[field] = value;
        return TagForm.Read(shown, entered);
    }

    [Fact]
    public void Untouched_fields_are_not_written()
    {
        var shown = TagForm.Values(Current);
        var (change, error) = TagForm.Read(shown, shown);

        Assert.Null(error);
        Assert.True(change!.IsEmpty);
    }

    [Fact]
    public void Only_the_edited_field_is_set()
    {
        var (change, _) = Enter(0, "  New title ");

        Assert.Equal(new TagFields { Title = "New title" }, change);
    }

    [Fact]
    public void Several_artists_are_separated_by_semicolons_and_clearing_clears()
    {
        Assert.Equal(["A", "B/C"], Enter(1, "A; B/C ;").Change!.Artists!);
        Assert.Empty(Enter(4, "").Change!.Genres!);
        Assert.Equal(0u, Enter(5, "").Change!.Year);
    }

    [Theory]
    [InlineData("2001", 2001)]
    [InlineData("۱۳۵۴", 1354)] // Persian digits
    [InlineData("١٩٧٥", 1975)] // Arabic digits
    public void A_year_can_be_typed_in_any_digits(string typed, uint expected) => Assert.Equal(expected, Enter(5, typed).Change!.Year);

    [Theory]
    [InlineData(5, "abc")]
    [InlineData(5, "-3")]
    [InlineData(5, "123456")]
    [InlineData(6, "1/12")]
    public void A_number_field_with_something_else_is_an_error_not_a_cleared_tag(int field, string typed)
    {
        var (change, error) = Enter(field, typed);

        Assert.Null(change);
        Assert.Contains("number", error);
    }
}

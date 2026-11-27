using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;
using Pardeh.UI.CleanupViews;

namespace Pardeh.Skinning.Tests;

/// <summary>What the cleanup screens hand back for what is ticked and typed. The screens only ask; nothing is written.</summary>
public sealed class CleanupViewTests
{
    public CleanupViewTests() => TestPlatform.Ensure();

    private static readonly TagFields Current = new()
    {
        Title = "Song | site.com", Artists = ["Singer"], AlbumArtists = [], Album = "Album", Genres = [], Composers = [], Comment = "", Copyright = "",
        Texts = [new TagText("id3v2:TPUB", "site.com"), new TagText("id3v2:WOAR", "http://site.com")], Pictures = [],
    };

    private static CleanupProposal Proposal(TagFields? tags = null) =>
        CleanupAnalyzer.Analyze(new TrackFacts("/m/x.mp3", "x.mp3", tags ?? Current, null));

    private static (ReviewView View, Func<ReviewResult?> Result) Review(CleanupInk ink, CleanupStore store, CleanupProposal? proposal = null, TagFields? tags = null)
    {
        ReviewResult? result = null;
        var view = new ReviewView(ink, proposal ?? Proposal(tags), tags ?? Current, store, () => Task.CompletedTask, r => result = r);
        return (view, () => result);
    }

    private static void Click(Control root, string text) =>
        root.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static List<CheckBox> Ticks(Control root) => [.. root.GetLogicalDescendants().OfType<CheckBox>()];

    public static TheoryData<bool> Layouts => [false, true];

    private static CleanupInk Ink(bool narrow) => narrow ? CleanupInk.Phone : CleanupInk.Desktop;

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Apply_hands_back_what_is_ticked_with_renaming_off_by_default(bool narrow)
    {
        var (view, result) = Review(Ink(narrow), new CleanupStore());

        Click(view, narrow ? "Apply" : "Apply selected");

        var choice = result()!.Choice!;
        Assert.Equal(ReviewAnswer.Apply, result()!.Answer);
        Assert.Null(choice.Pattern);
        Assert.Equal([CleanupField.Title, CleanupField.Text, CleanupField.Text], choice.Accepted.Select(s => s.Field));
        Assert.Equal("Song", choice.Accepted[0].Suggested);
    }

    [Fact]
    public void An_edited_value_is_ticked_and_handed_back_and_unticking_leaves_a_field_out()
    {
        var store = new CleanupStore();
        store.SetNaming("{title}", renameFiles: true);
        var (view, result) = Review(CleanupInk.Desktop, store);

        view.GetLogicalDescendants().OfType<TextBox>().First().Text = "My own title";
        // The one tick for the other fields (the second after the title's).
        Ticks(view).Single(t => t.Content is TextBlock { Text: var text } && text!.StartsWith("Remove website text")).IsChecked = false;
        Click(view, "Apply selected");

        var choice = result()!.Choice!;
        var title = Assert.Single(choice.Accepted);
        Assert.Equal("My own title", title.Suggested);
        Assert.Equal("{title}", choice.Pattern);
        Assert.Contains(view.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "New: My own title.mp3");
    }

    [Fact]
    public void A_missing_artist_can_be_typed_in()
    {
        var tags = Current with { Title = "Fine", Artists = [], Texts = [] };
        var (view, result) = Review(CleanupInk.Phone, new CleanupStore(), tags: tags);

        view.GetLogicalDescendants().OfType<TextBox>().Last().Text = "Typed Artist";
        Click(view, "Apply");

        Assert.Equal((CleanupField.Artists, "Typed Artist"), result()!.Choice!.Accepted.Select(s => (s.Field, s.Suggested)).Single());
    }

    [Theory]
    [InlineData("Ignore song", 2)]
    [InlineData("Cancel", 0)]
    public void The_other_buttons_answer_without_a_change(string button, int answer)
    {
        var (view, result) = Review(CleanupInk.Desktop, new CleanupStore());

        Click(view, button);

        Assert.Equal((ReviewAnswer)answer, result()!.Answer);
        Assert.Null(result()!.Choice);
    }

    [Fact]
    public void The_pattern_editor_saves_only_a_pattern_that_can_be_used()
    {
        var store = new CleanupStore();
        var closed = 0;
        var view = new PatternView(CleanupInk.Desktop, store, [(Current with { Title = "Song" }, "x.mp3")], () => closed++);
        var box = view.GetLogicalDescendants().OfType<TextBox>().Single();
        var save = view.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Save as default");

        box.Text = "{artist}";
        Assert.False(save.IsEnabled);
        box.Text = "{title} by {artist}";
        Assert.True(save.IsEnabled);
        Assert.Contains(view.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "x.mp3  →  Song by Singer.mp3");
        Click(view, "Save as default");

        Assert.Equal(("{title} by {artist}", 1), (store.DefaultPattern, closed));
        Assert.Contains("{title} by {artist}", store.AllPatterns);
        Assert.False(store.RenameFiles);
    }

    [Fact]
    public void The_users_rules_and_choices_are_saved_to_disk()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("pardeh-rules-").FullName, "cleanup.json");
        var store = new CleanupStore(path);
        var view = new RulesView(CleanupInk.Desktop, store, () => { });
        var boxes = view.GetLogicalDescendants().OfType<TextBox>().ToList();
        boxes[0].Text = "پخش تهران\r\n  MySite \n\nx";
        boxes[1].Text = "Radio.com";
        Ticks(view).Single().IsChecked = false;

        Click(view, "Save");
        store.SetNaming("{track} {title}", renameFiles: true);
        store.SetIgnored("/m/a.mp3", true);

        var again = new CleanupStore(path);
        Assert.Equal(["پخش تهران", "MySite"], again.Rules.Junk);
        Assert.Equal(["Radio.com"], again.Rules.Keep);
        Assert.Equal((false, "{track} {title}", true, true), (again.ShowBadges, again.DefaultPattern, again.RenameFiles, again.IsIgnored("/M/A.mp3")));
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }
}

using System.Diagnostics;
using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;
using Pardeh.Nava;
using static Pardeh.Skinning.Tests.LyricsTestData;

namespace Pardeh.Skinning.Tests;

public sealed class CleanupAnalyzerTests
{
    private static TrackFacts Track(string file, string title = "", string artist = "", string album = "", string? cover = null, string comment = "", TagText[]? texts = null) =>
        new("/m/" + file, file, new TagFields
        {
            Title = title, Artists = artist.Length > 0 ? [artist] : [], AlbumArtists = [], Album = album, Genres = [], Composers = [],
            Comment = comment, Copyright = "", Year = 0, Track = 0, Texts = texts ?? [],
        }, cover);

    private static CleanupSuggestion Of(CleanupProposal proposal, CleanupField field) => Assert.Single(proposal.Suggestions, s => s.Field == field);

    [Fact]
    public void A_clean_song_has_nothing_to_do()
    {
        var proposal = CleanupAnalyzer.Analyze(Track("whatever.mp3", "Real Title", "Real Artist", "Real Album"));

        Assert.Equal(CleanupLevel.Clean, proposal.Level);
    }

    [Fact]
    public void Junk_in_any_field_is_suggested_and_ticked()
    {
        var proposal = CleanupAnalyzer.Analyze(Track("x.mp3", "Song | site.com", "Singer", "www.site.com", comment: "Download from site.com",
            texts: [new TagText("id3v2:TPUB", "Site.com"), new TagText("id3v2:TXXX:mood", "calm")]));

        Assert.Equal(CleanupLevel.Ready, proposal.Level);
        Assert.Equal("Song", Of(proposal, CleanupField.Title).Suggested);
        Assert.Equal("", Of(proposal, CleanupField.Album).Suggested);
        Assert.Equal("", Of(proposal, CleanupField.Comment).Suggested);
        Assert.Equal(("id3v2:TPUB", ""), (Of(proposal, CleanupField.Text).TextId, Of(proposal, CleanupField.Text).Suggested));
        Assert.All(proposal.Suggestions, s => Assert.True(s.Selected));
    }

    [Fact]
    public void A_junk_title_and_artist_are_replaced_from_the_file_name()
    {
        var proposal = CleanupAnalyzer.Analyze(Track("03 - Googoosh - Hamsafar (site.com).mp3", "www.site.com", "Site.com"));

        Assert.Equal(("Hamsafar", CleanupReason.FromFileName, true),
            (Of(proposal, CleanupField.Title).Suggested, Of(proposal, CleanupField.Title).Reason, Of(proposal, CleanupField.Title).Selected));
        Assert.Equal("Googoosh", Of(proposal, CleanupField.Artists).Suggested);
        Assert.Equal(CleanupLevel.Ready, proposal.Level);
    }

    [Fact]
    public void A_guess_from_a_name_with_no_artist_part_waits_for_a_look()
    {
        var proposal = CleanupAnalyzer.Analyze(Track("some song.mp3"));

        Assert.False(Of(proposal, CleanupField.Title).Selected);
        Assert.True(proposal.MissingDetails); // no artist anywhere
        Assert.Equal(CleanupLevel.Review, proposal.Level);
    }

    [Fact]
    public void Nothing_to_go_on_is_still_flagged()
    {
        var proposal = CleanupAnalyzer.Analyze(Track("track01.mp3"));

        Assert.Empty(proposal.Suggestions);
        Assert.Equal(CleanupLevel.Review, proposal.Level);
    }

    [Fact]
    public void Junk_is_still_removed_when_what_is_left_is_only_a_placeholder()
    {
        var proposal = CleanupAnalyzer.Analyze(Track("x.mp3", "Track 3 | site.com", "Singer"));

        Assert.Equal("Track 3", Of(proposal, CleanupField.Title).Suggested);
        Assert.Equal(CleanupLevel.Review, proposal.Level);
    }

    [Fact]
    public void Broken_text_is_offered_unticked()
    {
        var proposal = CleanupAnalyzer.Analyze(Track("x.mp3", "ÓáÇã", "Singer"));

        var title = Of(proposal, CleanupField.Title);
        Assert.Equal(("سلام", CleanupReason.BrokenText, false), (title.Suggested, title.Reason, title.Selected));
        Assert.Equal(CleanupLevel.Review, proposal.Level);
    }

    private static List<TrackFacts> SiteLibrary() =>
    [
        .. Enumerable.Range(0, 6).Select(i => Track($"{i}.mp3", $"Song {(char)('A' + i)} - Nava Media", $"Artist {i}", $"Album {i}", cover: "logo")),
        // One artist's album keeps its own cover and subtitle on every song.
        .. Enumerable.Range(0, 8).Select(i => Track($"a{i}.mp3", $"Part {i} (The Wall Sessions)", "One Band", "One Album", cover: "album art")),
        .. Enumerable.Range(0, 6).Select(i => Track($"r{i}.mp3", $"Tune {i} (Remix)", $"Dj {i}", "")),
        .. Enumerable.Range(0, 6).Select(i => Track($"i{i}.mp3", "Intro", $"Group {i}", "")),
    ];

    [Fact]
    public void Words_and_pictures_repeated_across_unrelated_artists_are_suspected_but_never_ticked()
    {
        var library = SiteLibrary();
        var signals = LibrarySignals.Build(library);

        Assert.Equal(["nava media"], signals.RepeatedPhrases);
        Assert.Equal(["logo"], signals.RepeatedCovers);
        var proposal = CleanupAnalyzer.Analyze(library[0], signals);
        Assert.Equal(("Song A", CleanupReason.Repeated, false),
            (Of(proposal, CleanupField.Title).Suggested, Of(proposal, CleanupField.Title).Reason, Of(proposal, CleanupField.Title).Selected));
        Assert.False(Of(proposal, CleanupField.Cover).Selected);
        Assert.Equal(CleanupLevel.Review, proposal.Level);
        Assert.Equal(CleanupLevel.Clean, CleanupAnalyzer.Analyze(library[6], signals).Level);
    }

    [Fact]
    public void A_phrase_the_user_keeps_is_not_suggested()
    {
        var library = SiteLibrary();
        var rules = new JunkRules([], ["Nava Media"]);

        var proposal = CleanupAnalyzer.Analyze(library[0], LibrarySignals.Build(library, rules), rules);

        Assert.DoesNotContain(proposal.Suggestions, s => s.Field == CleanupField.Title);
    }

    [Fact]
    public void A_change_is_only_made_to_a_field_that_still_holds_what_was_looked_at()
    {
        var proposal = CleanupAnalyzer.Analyze(Track("x.mp3", "Song | site.com", "Singer", "Album | site.com"));
        var now = new TagFields { Title = "Song | site.com", Artists = ["Singer"], Album = "The user changed this since" };

        var change = CleanupAnalyzer.ToChange(now, proposal.Suggestions, CleanupAnalyzer.HashCover);

        Assert.Equal(new TagFields { Title = "Song" }, change);
    }

    [Fact]
    public void Only_the_suspected_picture_is_removed()
    {
        var logo = new TagPicture("FrontCover", "image/jpeg", "", [1, 2, 3]);
        var back = new TagPicture("BackCover", "image/jpeg", "", [4, 5, 6]);
        var suggestion = new CleanupSuggestion(CleanupField.Cover, null, "", "", CleanupReason.RepeatedCover, true) { CoverHash = CleanupAnalyzer.HashCover(logo.Data) };

        var change = CleanupAnalyzer.ToChange(new TagFields { Pictures = [logo, back] }, [suggestion], CleanupAnalyzer.HashCover);

        Assert.Equal([back], change.Pictures!);
    }
}

[Collection("MediaSources statics")]
public sealed class CleanupLibraryTests : IDisposable
{
    private readonly ChangeFixture _f = new();

    public void Dispose() => _f.Dispose();

    private CleanupScanner Scanner(CleanupStore? store = null, ITrackFactsReader? reader = null) =>
        new(_f.Library, reader ?? new TrackFactsReader(), store ?? new CleanupStore(Path.Combine(_f.Data, "cleanup.json")), Path.Combine(_f.Data, "cleanup-cache.json"));

    private sealed class Counting : ITrackFactsReader
    {
        public int Reads;

        public TrackFacts? Read(string source)
        {
            Interlocked.Increment(ref Reads);
            return new TrackFactsReader().Read(source);
        }
    }

    [Fact]
    public async Task A_scan_finds_what_needs_cleanup_and_reads_only_changed_files_again()
    {
        var dirty = _f.Make("dirty.mp3", "Song | www.site.com");
        var clean = _f.Make("clean.mp3", "Fine Song");
        await _f.ScanAsync();
        var reader = new Counting();
        var scanner = Scanner(reader: reader);

        await scanner.ScanAsync();

        Assert.True(scanner.NeedsCleanup(dirty));
        Assert.False(scanner.NeedsCleanup(clean));
        Assert.Equal([dirty], scanner.Pending().Select(p => p.Source));
        Assert.Equal(2, reader.Reads);
        await scanner.ScanAsync();
        Assert.Equal(2, reader.Reads);
        // After a restart too: the cache is on disk.
        var later = Scanner(reader: reader);
        await later.ScanAsync();
        Assert.Equal(2, reader.Reads);
        Assert.True(later.NeedsCleanup(dirty));
    }

    [Fact]
    public async Task An_ignored_song_loses_its_badge_and_stays_ignored_through_a_rename()
    {
        var path = _f.Make("dirty.mp3", "Song | www.site.com");
        await _f.ScanAsync();
        var store = new CleanupStore(Path.Combine(_f.Data, "cleanup.json"));
        var scanner = Scanner(store);
        await scanner.ScanAsync();

        store.SetIgnored(path, true);

        Assert.False(scanner.NeedsCleanup(path));
        Assert.Empty(scanner.Pending());
        var references = new LibraryReferences(_f.Library, new TagMediaInfoReader()) { Cleanup = store };
        var service = new FileChangeService(_f.Service.Backends, new TagEditor(), references, _f.Journal, _f.Library);
        var renamed = (await service.ApplyAsync("test", [ChangeRequest.Rename(path, "other.mp3")])).Results[0].NewSource!;
        Assert.True(new CleanupStore(Path.Combine(_f.Data, "cleanup.json")).IsIgnored(renamed));
    }

    [Fact]
    public async Task A_batch_cleans_renames_summarises_and_undoes_as_one()
    {
        var a = _f.Make("01 - Singer - First (site.com).mp3", "First [www.site.com]", "Singer", "site.com");
        var b = _f.Make("b.mp3", "Second - 320", "Singer");
        var original = new[] { File.ReadAllBytes(a), File.ReadAllBytes(b) };
        await _f.ScanAsync();
        var scanner = Scanner();
        await scanner.ScanAsync();
        var ready = scanner.Pending().Where(p => p.Level == CleanupLevel.Ready).ToList();
        Assert.Equal(2, ready.Count);

        var plan = await CleanupPlanner.PlanAsync(_f.Service,
            [.. ready.Select(p => new CleanupChoice(p.Source, [.. p.Suggestions.Where(s => s.Selected)], "{artist} - {title}"))],
            cancel: TestContext.Current.CancellationToken);

        Assert.Equal("2 files: 2 titles, 1 album, 2 renames", plan.Summary);
        Assert.Equal(original[0], File.ReadAllBytes(a)); // planning changes nothing
        var result = await _f.Service.ApplyAsync("Clean up 2 songs", plan.Requests);
        ChangeFixture.AssertAll(ResultStatus.Done, result);
        Assert.Equal(["Singer - First.mp3", "Singer - Second.mp3"], Directory.GetFiles(_f.Music).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        await _f.ScanAsync();
        await scanner.ScanAsync();
        Assert.Empty(scanner.Pending());

        ChangeFixture.AssertAll(ResultStatus.Done, await _f.Service.UndoAsync(result.BatchId!));
        Assert.True(File.Exists(a) && File.Exists(b));
        Assert.Equal("First [www.site.com]", new TagEditor().Read(a, "a.mp3").Title);
    }

    [Fact]
    public async Task A_batch_can_be_stopped_between_files_and_what_was_done_can_be_undone()
    {
        var files = Enumerable.Range(0, 5).Select(i => _f.Make($"{i}.mp3", "Old")).ToList();
        await _f.ScanAsync();
        var stop = new CancellationTokenSource();
        var seen = new List<int>();
        var progress = new Watcher(p =>
        {
            seen.Add(p.Done);
            if (p.Done == 2)
                stop.Cancel();
        });

        var result = await _f.Service.ApplyAsync("test", [.. files.Select(f => ChangeRequest.EditTags(f, new TagFields { Title = "New" }))],
            new ChangeOptions { Progress = progress, Cancel = stop.Token });

        Assert.Equal(2, result.Count(ResultStatus.Done));
        Assert.Equal(3, result.Count(ResultStatus.Cancelled));
        Assert.Equal([0, 1, 2], seen.Take(3));
        Assert.True(result.CanUndo);
        await _f.Service.UndoAsync(result.BatchId!);
        Assert.All(files, f => Assert.Equal("Old", new TagEditor().Read(f, "a.mp3").Title));
    }

    private sealed class Watcher(Action<(int Done, int Total)> action) : IProgress<(int Done, int Total)>
    {
        public void Report((int Done, int Total) value) => action(value);
    }

    [Fact]
    public async Task A_few_hundred_songs_are_scanned_and_changed_in_reasonable_time()
    {
        var first = _f.Make("0000.mp3", "Melody | site.com");
        var template = File.ReadAllBytes(first);
        for (var i = 1; i < 300; i++)
            File.WriteAllBytes(Path.Combine(_f.Music, $"{i:0000}.mp3"), template);
        var watch = Stopwatch.StartNew();
        await _f.ScanAsync();
        var scanner = Scanner();
        await scanner.ScanAsync();
        var scanned = watch.Elapsed;

        watch.Restart();
        var plan = await CleanupPlanner.PlanAsync(_f.Service,
            [.. scanner.Pending().Select(p => new CleanupChoice(p.Source, [.. p.Suggestions.Where(s => s.Selected)], null))],
            cancel: TestContext.Current.CancellationToken);
        var result = await _f.Service.ApplyAsync("test", plan.Requests);
        var applied = watch.Elapsed;

        Assert.Equal(300, result.Count(ResultStatus.Done));
        TestContext.Current.TestOutputHelper?.WriteLine($"scan {scanned.TotalSeconds:0.0} s, plan and apply {applied.TotalSeconds:0.0} s");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "pardeh-cleanup-timing.txt"), $"300 files: scan {scanned.TotalSeconds:0.0} s, plan and apply {applied.TotalSeconds:0.0} s");
        Assert.True(applied < TimeSpan.FromMinutes(2), applied.ToString());
    }
}

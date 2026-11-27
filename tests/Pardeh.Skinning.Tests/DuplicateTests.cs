using System.Globalization;
using System.Text;
using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;
using Pardeh.Core.Duplicates;
using Pardeh.Core.Library;
using Pardeh.Nava;

namespace Pardeh.Skinning.Tests;

/// <summary>What counts as a duplicate, from made-up details. A wrong "duplicate" is the worst fault.</summary>
public sealed class DuplicateMatchTests
{
    private static SongCopy Copy(string file, string title, string artist, double seconds = 200, string album = "", string? sound = null, string? bytes = null,
        int kbps = 128, bool lossless = false, string codec = "mp3", TagFields? tags = null, double? declared = null)
    {
        var read = tags ?? new TagFields { Title = title, Artists = artist.Length > 0 ? [artist] : [], Album = album };
        var item = new LibraryItem(@"C:\music\" + file, title, artist, album, "", "", 0, seconds, false, 1000, DateTime.UnixEpoch);
        return new SongCopy(item, new AudioFacts(file, read, null, 0, seconds, kbps, "start"), SongName.Read(file, read, JunkRules.None))
        {
            ContentHash = bytes,
            Scan = sound is null ? null : new AudioScan(sound, seconds, declared ?? seconds, codec, lossless, 44100),
        };
    }

    private static IReadOnlyList<DuplicateGroup> Groups(params SongCopy[] copies) => DuplicateFinder.Groups(copies, new DuplicateStore());

    [Fact]
    public void The_same_title_artist_and_length_is_probably_one_recording()
    {
        var group = Assert.Single(Groups(
            Copy("a.mp3", "Blue Night", "The Singers", 200, sound: "1"),
            Copy("128 site.com Singers - Blue Night.mp3", "Blue Night [site.com]", "Singers - site.com", 201.5, sound: "2")));

        Assert.Equal(DuplicateKind.Probable, group.Kind);
        Assert.False(group.IsExact);
    }

    [Theory]
    // Another version, by the title.
    [InlineData("Blue Night (Remix)", "The Singers", 200, "")]
    [InlineData("Blue Night - Live", "The Singers", 200, "")]
    [InlineData("Blue Night (Instrumental)", "The Singers", 200, "")]
    [InlineData("Blue Night بی کلام", "The Singers", 200, "")]
    [InlineData("Blue Night (Acoustic Version)", "The Singers", 200, "")]
    [InlineData("Blue Night [Sped Up]", "The Singers", 200, "")]
    [InlineData("Blue Night (Karaoke)", "The Singers", 200, "")]
    [InlineData("Blue Night (Remastered 2011)", "The Singers", 200, "")]
    // By the album.
    [InlineData("Blue Night", "The Singers", 200, "Live at the Hall")]
    // Another recording, or not shown to be this one.
    [InlineData("Blue Night", "The Singers", 213, "")]
    [InlineData("Blue Night", "The Singers", 0, "")]
    [InlineData("Blue Night (feat. A Guest)", "The Singers", 200, "")]
    [InlineData("Blue Night", "The Singers & A Guest", 200, "")]
    [InlineData("Blue Night (Part 2)", "The Singers", 200, "")]
    [InlineData("Blue Night 2", "The Singers", 200, "")]
    [InlineData("Blue Nights", "The Singers", 200, "")]
    [InlineData("Blue Night", "Someone Else", 200, "")]
    [InlineData("Blue Night", "", 200, "")]
    public void Other_versions_and_other_songs_are_not_duplicates(string title, string artist, double seconds, string album)
    {
        Assert.Empty(Groups(Copy("a.mp3", "Blue Night", "The Singers", 200, sound: "1"), Copy("b.mp3", title, artist, seconds, album, sound: "2")));
    }

    [Fact]
    public void Two_different_remixes_are_two_versions()
    {
        Assert.Empty(Groups(Copy("a.mp3", "Blue Night (First DJ Remix)", "S", sound: "1"), Copy("b.mp3", "Blue Night (Second DJ Remix)", "S", sound: "2")));
        Assert.Single(Groups(Copy("a.mp3", "Blue Night (First DJ Remix)", "S", sound: "1"), Copy("b.mp3", "Blue Night (First DJ Remix)", "S", sound: "2")));
    }

    [Fact]
    public void A_version_named_only_by_the_file_counts()
    {
        Assert.Empty(Groups(Copy("Blue Night.mp3", "Blue Night", "S", sound: "1"), Copy("Blue Night instrumental.mp3", "Blue Night", "S", sound: "2")));
    }

    [Fact]
    public void Tags_that_are_a_sites_words_on_different_songs_are_not_believed()
    {
        // The same "title" and "artist" on two files of the same length, while the file names say two different songs.
        Assert.Empty(Groups(Copy("Singer - Morning Rain.mp3", "Daily Music", "Daily Station", sound: "1"),
            Copy("Other Person - Winter Road.mp3", "Daily Music", "Daily Station", sound: "2")));
    }

    [Fact]
    public void A_title_many_albums_have_needs_the_same_album()
    {
        Assert.Empty(Groups(Copy("a.mp3", "Intro", "S", album: "One", sound: "1"), Copy("b.mp3", "Intro", "S", album: "Two", sound: "2")));
        Assert.Single(Groups(Copy("a.mp3", "Intro", "S", album: "One", sound: "1"), Copy("b.mp3", "Intro", "S", album: "One", sound: "2")));
    }

    [Fact]
    public void Lengths_do_not_chain()
    {
        // 200 and 202 are near, 202 and 204 are near, 200 and 204 are not: never all three.
        var groups = Groups(Copy("a.mp3", "Blue Night", "S", 200, sound: "1"), Copy("b.mp3", "Blue Night", "S", 202, sound: "2"),
            Copy("c.mp3", "Blue Night", "S", 204, sound: "3"));

        Assert.Equal(2, Assert.Single(groups).Copies.Count);
    }

    [Fact]
    public void Kinds_follow_the_least_alike_pair()
    {
        var first = Copy("a.mp3", "One", "S", sound: "1", bytes: "x");
        Assert.Equal(DuplicateKind.Identical, Assert.Single(Groups(first, Copy("b.mp3", "One", "S", sound: "1", bytes: "x"))).Kind);
        Assert.Equal(DuplicateKind.SameAudio, Assert.Single(Groups(first, Copy("b.mp3", "Quite another name", "T", sound: "1"))).Kind);
        var mixed = Assert.Single(Groups(first, Copy("b.mp3", "One", "S", sound: "1", bytes: "x"), Copy("c.mp3", "One", "S", sound: "2")));
        Assert.Equal((DuplicateKind.Probable, 3), (mixed.Kind, mixed.Copies.Count));
    }

    [Fact]
    public void Not_duplicates_is_remembered_by_the_sound_and_by_the_files()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pardeh-apart-{Guid.NewGuid():N}.json");
        try
        {
            var a = Copy("a.mp3", "Blue Night", "S", sound: "1");
            var b = Copy("b.mp3", "Blue Night", "S", sound: "2");
            var store = new DuplicateStore(path);
            var added = store.KeepApart([a, b]);

            var again = new DuplicateStore(path);
            Assert.Empty(DuplicateFinder.Groups([a, b], again));
            // Renamed and retagged, the sound the same.
            Assert.Empty(DuplicateFinder.Groups([Copy("new.mp3", "Blue Night", "S", sound: "1"), b], again));
            again.Rejoin(added);
            Assert.Single(DuplicateFinder.Groups([a, b], again));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Identical_files_the_user_keeps_apart_stay_apart_even_through_a_third()
    {
        var a = Copy("a.mp3", "One", "S", sound: "1", bytes: "x");
        var b = Copy("b.mp3", "One", "S", sound: "2");
        var c = Copy("c.mp3", "One", "S", sound: "3");
        var store = new DuplicateStore();
        store.KeepApart([a, b]);

        Assert.All(DuplicateFinder.Groups([a, b, c], store), g => Assert.False(g.Copies.Contains(a) && g.Copies.Contains(b)));
    }

    [Fact]
    public void Related_songs_are_listed_in_three_kinds_with_reasons()
    {
        var song = Copy("a.mp3", "Blue Night", "S", 200, sound: "1");
        var copy = Copy("b.mp3", "Blue Night", "S", 201, sound: "2");
        var remix = Copy("c.mp3", "Blue Night (Remix)", "S", 200, sound: "3");
        var longer = Copy("d.mp3", "Blue Night", "S", 230, sound: "4");
        var cover = Copy("e.mp3", "Blue Night", "Another", 190, sound: "5");
        var unrelated = Copy("f.mp3", "Red Morning", "S", 200, sound: "6");
        SongCopy[] all = [song, copy, remix, longer, cover, unrelated];
        var store = new DuplicateStore();

        var related = DuplicateFinder.Related(song, all, DuplicateFinder.Groups(all, store), store);

        Assert.Equal(RelatedList.Copies, related.Single(r => r.Copy == copy).List);
        Assert.Equal((RelatedList.OtherVersions, "Another version (remix)"), (related.Single(r => r.Copy == remix).List, related.Single(r => r.Copy == remix).Reason));
        Assert.Equal((RelatedList.OtherVersions, "Length differs by 30 s"), (related.Single(r => r.Copy == longer).List, related.Single(r => r.Copy == longer).Reason));
        Assert.Equal((RelatedList.Similar, "Same title, another artist"), (related.Single(r => r.Copy == cover).List, related.Single(r => r.Copy == cover).Reason));
        Assert.DoesNotContain(related, r => r.Copy == unrelated);

        store.Unrelate(song, cover);
        Assert.DoesNotContain(DuplicateFinder.Related(song, all, DuplicateFinder.Groups(all, store), store), r => r.Copy == cover);
    }

    private static DuplicateGroup Group(params SongCopy[] copies) => Assert.Single(Groups(copies));

    [Fact]
    public void A_much_higher_stated_bitrate_is_suggested_and_said_to_prove_nothing()
    {
        var advice = CopyAdvisor.Advise(Group(Copy("a.mp3", "T", "S", sound: "1", kbps: 128), Copy("b.mp3", "T", "S", sound: "2", kbps: 320)));

        Assert.Equal(1, advice.Keep);
        Assert.Contains("no proof", advice.Why);
    }

    [Theory]
    [InlineData(192, 256, "mp3", false, 200)] // about the same
    [InlineData(128, 320, "aac", false, 200)] // other formats
    [InlineData(128, 320, "mp3", false, 201.8)] // lengths differ
    [InlineData(0, 320, "mp3", false, 200)] // a bitrate unknown
    public void Nothing_is_suggested_when_it_is_not_clear(int first, int second, string codec, bool lossless, double seconds)
    {
        var advice = CopyAdvisor.Advise(Group(Copy("a.mp3", "T", "S", sound: "1", kbps: first),
            Copy("b.mp3", "T", "S", seconds, sound: "2", kbps: second, codec: codec, lossless: lossless)));

        Assert.Null(advice.Keep);
        Assert.NotEmpty(advice.Why);
    }

    [Fact]
    public void A_copy_cut_short_is_never_suggested()
    {
        var advice = CopyAdvisor.Advise(Group(Copy("a.mp3", "T", "S", sound: "1", kbps: 128), Copy("b.mp3", "T", "S", sound: "2", kbps: 320, declared: 300)));

        Assert.Equal(0, advice.Keep);
        Assert.Contains(advice.Notes[1], n => n.StartsWith("Cut short", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_the_same_sound_the_cleanest_information_is_suggested_and_merging_fills_only_what_is_missing()
    {
        var junk = Copy("a.mp3", "", "", sound: "1", tags: new TagFields { Title = "Song | www.site.com", Artists = ["Singer"], Album = "" });
        var clean = Copy("b.mp3", "", "", sound: "1", tags: new TagFields { Title = "Song", Artists = ["Singer"], Album = "Record", Year = 1999 });
        var group = Group(junk, clean);

        var advice = CopyAdvisor.Advise(group);
        Assert.Equal("b.mp3", group.Copies[advice.Keep!.Value].Facts.FileName);

        // Keeping the junk one anyway: its title is replaced and the gaps filled; its good artist stays.
        var sources = CopyAdvisor.DefaultSources(group.Copies, 0);
        Assert.Equal([MergeField.Title, MergeField.Album, MergeField.Year], sources.Keys.Order());
        Assert.All(sources.Values, v => Assert.Equal(1, v));
        Assert.Empty(CopyAdvisor.DefaultSources(group.Copies, 1));
    }

    [Fact]
    public void A_scan_that_reads_less_than_the_file_says_is_cut_short()
    {
        Assert.True(new AudioScan("d", 120, 200, "mp3", false, 44100).CutShort);
        Assert.False(new AudioScan("d", 199, 200, "mp3", false, 44100).CutShort);
        Assert.False(new AudioScan("d", 0, 200, "mp3", false, 44100).CutShort);
    }
}

/// <summary>The check over real, generated files.</summary>
[Collection("MediaSources statics")]
public sealed class DuplicateScanTests : IDisposable
{
    private readonly ChangeFixture _f = new();

    public void Dispose()
    {
        foreach (var link in Directory.GetDirectories(_f.Root).Where(d => new DirectoryInfo(d).LinkTarget is not null))
            Directory.Delete(link);
        _f.Dispose();
    }

    private sealed class Counting : IDuplicateAnalyzer
    {
        private readonly DuplicateAnalyzer _real = new();
        public int Reads, Scans, Hashes;
        public Action? OnRead;

        public AudioFacts? Read(string source)
        {
            Interlocked.Increment(ref Reads);
            OnRead?.Invoke();
            return _real.Read(source);
        }

        public AudioScan? Scan(string source, CancellationToken cancel)
        {
            Interlocked.Increment(ref Scans);
            return _real.Scan(source, cancel);
        }

        public string? ContentHash(string source, CancellationToken cancel)
        {
            Interlocked.Increment(ref Hashes);
            return _real.ContentHash(source, cancel);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DuplicateScanner Scanner(IDuplicateAnalyzer? analyzer = null) =>
        new(_f.Library, analyzer ?? new DuplicateAnalyzer(), new DuplicateStore(Path.Combine(_f.Data, "duplicates.json")), Path.Combine(_f.Data, "duplicates-cache.json"));

    // A tone of its own for each song, so that different songs have different sound.
    private string Tone(string name, int hertz, string title, string artist = "Singer", int kbps = 128, double seconds = 6)
    {
        var path = Path.Combine(_f.Music, name);
        FfmpegTool.Run("-f", "lavfi", "-i", string.Create(CultureInfo.InvariantCulture, $"sine=frequency={hertz}:duration={seconds}"),
            "-c:a", "libmp3lame", "-b:a", kbps + "k", "-metadata", "title=" + title, "-metadata", "artist=" + artist, path);
        return path;
    }

    private string Retagged(string from, string name, string title)
    {
        var path = Path.Combine(_f.Music, name);
        FfmpegTool.Run("-i", from, "-c", "copy", "-map_metadata", "-1", "-metadata", "title=" + title, "-metadata", "artist=Singer", "-metadata", "album=Elsewhere", path);
        return path;
    }

    private static string[] Names(DuplicateGroup group) => [.. group.Copies.Select(c => c.Facts.FileName).Order(StringComparer.Ordinal)];

    [Fact]
    public async Task Each_kind_is_found_and_other_versions_are_not_grouped()
    {
        var first = Tone("first.mp3", 300, "Morning");
        File.Copy(first, Path.Combine(_f.Music, "first (1).mp3"));
        var other = Tone("evening.mp3", 500, "Evening");
        Retagged(other, "evening from a site.mp3", "Evening [site.com]");
        Tone("night 128.mp3", 700, "Night");
        Tone("night 320.mp3", 700, "Night", kbps: 320);
        Tone("night remix.mp3", 900, "Night (Remix)");
        Tone("night long.mp3", 710, "Night", seconds: 12);
        Tone("alone.mp3", 1100, "Alone");
        await _f.ScanAsync();
        var scanner = Scanner();

        await scanner.Start();
        var report = scanner.Report(Ct);

        Assert.Equal(DuplicateScanState.Done, scanner.Progress.State);
        Assert.Equal((0, 0), (report.NotRead, report.Unreadable));
        Assert.Equal([DuplicateKind.Identical, DuplicateKind.SameAudio, DuplicateKind.Probable], report.Groups.Select(g => g.Kind));
        Assert.Equal(["first (1).mp3", "first.mp3"], Names(report.Groups[0]));
        Assert.Equal(["evening from a site.mp3", "evening.mp3"], Names(report.Groups[1]));
        Assert.Equal(["night 128.mp3", "night 320.mp3"], Names(report.Groups[2]));

        // Suggestions: the plain name, the clean details, and the much higher stated bitrate.
        Assert.Equal("first.mp3", report.Groups[0].Copies[CopyAdvisor.Advise(report.Groups[0]).Keep!.Value].Facts.FileName);
        Assert.Equal("night 320.mp3", report.Groups[2].Copies[CopyAdvisor.Advise(report.Groups[2]).Keep!.Value].Facts.FileName);
        var night = report.CopyOf(Path.Combine(_f.Music, "night 128.mp3"))!;
        var related = DuplicateFinder.Related(night, report.Copies, report.Groups, scanner.Store);
        Assert.Equal(["night long.mp3", "night remix.mp3"], related.Where(r => r.List == RelatedList.OtherVersions).Select(r => r.Copy.Facts.FileName).Order());
    }

    [Fact]
    public void Old_style_tags_do_not_change_what_the_sound_is()
    {
        var plain = Tone("a.mp3", 300, "Song");
        var tagged = Path.Combine(_f.Music, "b.mp3");
        var old = new byte[128];
        Encoding.ASCII.GetBytes("TAGAn old title").CopyTo(old, 0);
        File.WriteAllBytes(tagged, [.. File.ReadAllBytes(plain), .. old]);

        Assert.Equal(AudioDigest.Compute(plain), AudioDigest.Compute(tagged));
    }

    [Fact]
    public void A_file_cut_short_is_told_from_a_whole_one()
    {
        var whole = Tone("whole.mp3", 300, "Song", seconds: 30);
        var cut = Path.Combine(_f.Music, "cut.mp3");
        var bytes = File.ReadAllBytes(whole);
        File.WriteAllBytes(cut, bytes[..(bytes.Length / 2)]);

        Assert.False(AudioDigest.Scan(whole, 0, Ct)!.CutShort);
        Assert.True(AudioDigest.Scan(cut, 0, Ct)!.CutShort);
    }

    [Fact]
    public async Task One_file_listed_through_two_folders_is_one_copy_and_no_duplicate()
    {
        var song = Tone("song.mp3", 300, "Song");
        TestLinks.Junction(Path.Combine(_f.Root, "again"), _f.Music);
        _f.Library.Folders.Add(Path.Combine(_f.Root, "again"));
        await _f.ScanAsync();
        Assert.Equal(2, _f.Library.Items.Count);
        var scanner = Scanner();

        await scanner.Start();
        var report = scanner.Report(Ct);

        var copy = Assert.Single(report.Copies);
        Assert.Equal(song, copy.Source); // the plain path stands for it
        Assert.Single(copy.Aliases);
        Assert.Equal(1, report.Folded);
        Assert.Empty(report.Groups);
    }

    [Fact]
    public async Task Keep_the_best_of_each_takes_only_the_same_sound_and_not_what_was_skipped()
    {
        var morning = Tone("morning.mp3", 300, "Morning");
        File.Copy(morning, Path.Combine(_f.Music, "morning (1).mp3"));
        var evening = Tone("evening.mp3", 500, "Evening");
        Retagged(evening, "evening site.mp3", "Evening [site.com]");
        Tone("night 128.mp3", 700, "Night");
        Tone("night 320.mp3", 700, "Night", kbps: 320); // clearly suggested, but only probably the same
        await _f.ScanAsync();
        var scanner = Scanner();
        await scanner.Start();
        var report = scanner.Report(Ct);
        var context = new Pardeh.UI.DuplicateViews.DuplicatesContext(scanner);

        var best = context.Best(report);

        Assert.Equal([DuplicateKind.Identical, DuplicateKind.SameAudio], best.Select(c => c.Group.Kind));
        Assert.All(best, c => Assert.DoesNotContain(c.Keep, c.Remove));
        context.Skipped.Add(report.Groups[0].Id);
        Assert.Equal(DuplicateKind.SameAudio, Assert.Single(context.Best(report)).Group.Kind);
    }

    [Fact]
    public async Task Files_Android_has_trashed_or_not_finished_are_not_songs()
    {
        var song = Tone("song.mp3", 300, "Song");
        File.Copy(song, Path.Combine(_f.Music, ".trashed-1798386999-song.mp3"));
        File.Copy(song, Path.Combine(_f.Music, ".pending-1798386999-new.mp3"));

        await _f.ScanAsync();

        Assert.Equal(song, Assert.Single(_f.Library.Items).Path);
    }

    [Fact]
    public async Task Hidden_extras_come_back_when_unhidden()
    {
        var song = Tone("song.mp3", 300, "Song");
        var extra = Path.Combine(_f.Music, "extra.mp3");
        File.Copy(song, extra);
        TestLinks.Junction(Path.Combine(_f.Root, "again"), _f.Music);
        _f.Library.Folders.Add(Path.Combine(_f.Root, "again"));
        await _f.ScanAsync();
        var paths = new[] { extra, Path.Combine(_f.Root, "again", "extra.mp3") };

        _f.Library.Remove(paths);
        Assert.Equal(2, _f.Library.Items.Count);
        _f.Library.Unhide(paths);
        await _f.ScanAsync();

        Assert.Equal(4, _f.Library.Items.Count);
    }

    [Fact]
    public async Task What_was_read_is_kept_and_only_new_or_changed_files_are_read_again()
    {
        var song = Tone("a.mp3", 300, "Song");
        File.Copy(song, Path.Combine(_f.Music, "b.mp3"));
        await _f.ScanAsync();
        var first = new Counting();
        await Scanner(first).Start();
        Assert.Equal(2, first.Reads);
        Assert.Equal(2, first.Hashes);
        Assert.Equal(1, first.Scans); // the same bytes are not read twice

        var second = new Counting();
        var scanner = Scanner(second);
        Assert.Single(scanner.Report(Ct).Groups); // known at once, without a run
        await scanner.Start();
        Assert.Equal((0, 0, 0), (second.Reads, second.Hashes, second.Scans));

        Tone("c.mp3", 500, "Other");
        await _f.ScanAsync();
        Assert.Equal(1, scanner.Report(Ct).NotRead);
        await scanner.Start();
        Assert.Equal(1, second.Reads);
    }

    [Fact]
    public async Task A_run_can_be_paused_stopped_and_taken_up_again()
    {
        for (var i = 0; i < 4; i++)
            Tone($"{i}.mp3", 300 + 100 * i, "Song " + i);
        await _f.ScanAsync();
        var counting = new Counting();
        var scanner = Scanner(counting);
        counting.OnRead = () =>
        {
            if (counting.Reads == 2)
                scanner.Pause();
        };

        var run = scanner.Start();
        while (scanner.Progress.State != DuplicateScanState.Paused)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.Equal(2, counting.Reads);
        scanner.Stop();
        await run;
        Assert.Equal(DuplicateScanState.Stopped, scanner.Progress.State);

        counting.OnRead = null;
        await scanner.Start();
        Assert.Equal(4, counting.Reads);
        Assert.Equal(DuplicateScanState.Done, scanner.Progress.State);
    }

    private async Task<(DuplicateScanner Scanner, DuplicateGroup Group)> Scanned()
    {
        await _f.ScanAsync();
        var scanner = Scanner();
        await scanner.Start();
        return (scanner, Assert.Single(scanner.Report(Ct).Groups));
    }

    [Fact]
    public async Task Keep_and_merge_gives_the_kept_file_the_chosen_details_then_removes_and_undo_returns_both()
    {
        var poor = Tone("poor.mp3", 300, "Song [site.com]");
        Retagged(poor, "good.mp3", "Song");
        var (scanner, group) = await Scanned();
        var keep = group.Copies.ToList().FindIndex(c => c.Facts.FileName == "poor.mp3");

        var plan = await DuplicatePlanner.PlanAsync(_f.Service, scanner.Analyzer, [new GroupChoice(group, keep, [1 - keep], CopyAdvisor.DefaultSources(group.Copies, keep))], cancel: Ct);

        Assert.Empty(plan.Problems);
        Assert.Equal((1, 1), (plan.Merged, plan.Removals));
        Assert.Equal(ChangeKind.Tags, plan.Requests[0].Kind);
        var result = await _f.Service.ApplyAsync("merge", plan.Requests);
        ChangeFixture.AssertAll(ResultStatus.Done, result);
        var tags = await _f.Service.ReadTagsAsync(poor);
        Assert.Equal(("Song", "Elsewhere"), (tags.Title, tags.Album));
        Assert.False(File.Exists(Path.Combine(_f.Music, "good.mp3")));

        ChangeFixture.AssertAll(ResultStatus.Done, await _f.Service.UndoAsync(result.BatchId!));
        Assert.True(File.Exists(Path.Combine(_f.Music, "good.mp3")));
        Assert.Equal("Song [site.com]", (await _f.Service.ReadTagsAsync(poor)).Title);    }

    [Fact]
    public async Task A_file_changed_since_the_check_is_left_alone()
    {
        var first = Tone("a.mp3", 300, "Song");
        File.Copy(first, Path.Combine(_f.Music, "b.mp3"));
        var (scanner, group) = await Scanned();

        // One byte of the sound is changed; size and time stay as they were.
        var second = Path.Combine(_f.Music, "b.mp3");
        var time = File.GetLastWriteTimeUtc(second);
        var data = File.ReadAllBytes(second);
        data[data.Length / 2] ^= 1;
        File.WriteAllBytes(second, data);
        File.SetLastWriteTimeUtc(second, time);

        var plan = await DuplicatePlanner.PlanAsync(_f.Service, scanner.Analyzer, [new GroupChoice(group, 0, [1], new Dictionary<MergeField, int>())], cancel: Ct);

        Assert.Equal(0, plan.Removals);
        Assert.Contains("no longer identical", Assert.Single(plan.Problems));
    }

    [Fact]
    public async Task A_copy_that_may_not_be_a_file_of_its_own_is_never_planned_for_removal()
    {
        var first = Tone("a.mp3", 300, "Song");
        File.Copy(first, Path.Combine(_f.Music, "b.mp3"));
        var (scanner, group) = await Scanned();
        var unsure = group with { Copies = [group.Copies[0], group.Copies[1] with { KnownFile = false }] };

        var plan = await DuplicatePlanner.PlanAsync(_f.Service, scanner.Analyzer, [new GroupChoice(unsure, 0, [1], new Dictionary<MergeField, int>())], cancel: Ct);

        Assert.Equal(0, plan.Removals);
        Assert.Single(plan.Problems);
    }

    [Fact]
    public async Task The_kept_copy_is_never_in_the_removals()
    {
        var first = Tone("a.mp3", 300, "Song");
        File.Copy(first, Path.Combine(_f.Music, "b.mp3"));
        var (scanner, group) = await Scanned();

        var plan = await DuplicatePlanner.PlanAsync(_f.Service, scanner.Analyzer, [new GroupChoice(group, 0, [0, 1], new Dictionary<MergeField, int>())], cancel: Ct);

        Assert.Equal(group.Copies[1].Source, Assert.Single(plan.Requests).Source);
        Assert.Single(plan.Problems);
    }
}

using System.Security.Cryptography;
using Pardeh.Core.Cleanup;
using Pardeh.Core.Downloads;
using Pardeh.Nava;

namespace Pardeh.Skinning.Tests;

/// <summary>Downloads from a server on this machine, with real audio and the real decoder.</summary>
public sealed class DownloadTests : IDisposable
{
    private readonly TinyServer _server = new();
    private readonly DownloadFixture _f;

    public DownloadTests()
    {
        Assert.SkipWhen(FfmpegTool.Tool is null, "ffmpeg is not in tools/ffmpeg (scripts/get-ffmpeg.ps1)");
        _f = new DownloadFixture(_server);
    }

    public void Dispose()
    {
        _f?.Dispose();
        _server.Dispose();
    }

    private void Serve(byte[] file, string? etag = "\"v1\"", string type = "audio/mpeg", int? holdAfter = null, int? cutAfter = null) =>
        _server.Answer = request => ServerReply.File(request, file, etag, type, holdAfter, cutAfter);

    [Theory]
    [InlineData(".mp3", "libmp3lame")]
    [InlineData(".flac", "flac")]
    [InlineData(".ogg", "libvorbis")]
    [InlineData(".opus", "libopus")]
    [InlineData(".m4a", "aac")]
    [InlineData(".wav", "pcm_s16le")]
    public async Task A_file_is_looked_at_downloaded_checked_and_added_byte_for_byte(string extension, string codec)
    {
        var file = _f.Audio(extension, codec, seconds: 4);
        Serve(file, type: "application/octet-stream");

        var offer = await _f.Look(_server.Address("/files/My%20Song" + extension));

        Assert.Equal("My Song" + extension, offer.FileName);
        Assert.Equal(file.Length, offer.Size);
        Assert.False(offer.HostChanged);
        Assert.Empty(_f.Added);

        var end = await _f.Run(offer.ToJob());

        Assert.True(end.Stage == DownloadStage.Done, end.Message);
        var added = Assert.Single(_f.Added);
        Assert.Equal("My Song" + extension, Path.GetFileName(added));
        Assert.Equal(file, File.ReadAllBytes(added)); // a pasted link's file is left exactly as it came
        var record = Assert.Single(_f.NewManager().Store.Finished);
        Assert.Equal((DownloadKind.PastedLink, added, offer.Address.AbsoluteUri), (record.Kind, record.Source, record.Address));
        Assert.Null(record.Credit);
        Assert.Empty(_f.Left);
    }

    [Fact]
    public async Task The_servers_name_gets_no_say_in_folder_or_extension()
    {
        var file = _f.Audio(seconds: 3);
        _server.Answer = _ => new ServerReply { Body = file, Headers = { ("Content-Disposition", "attachment; filename=\"..\\\\..\\\\evil.exe\"") } };

        var end = await _f.Run((await _f.Look(_server.Address())).ToJob());

        Assert.Equal(DownloadStage.Done, end.Stage);
        Assert.Equal(Path.Combine(_f.Target.Folder, "evil.mp3"), Assert.Single(_f.Added));
    }

    [Fact]
    public async Task A_taken_name_gets_a_number_and_nothing_is_overwritten()
    {
        Serve(_f.Audio(seconds: 3));
        Directory.CreateDirectory(_f.Target.Folder);
        var mine = Path.Combine(_f.Target.Folder, "song.mp3");
        File.WriteAllText(mine, "mine");

        await _f.Run((await _f.Look(_server.Address())).ToJob());

        Assert.Equal("mine", File.ReadAllText(mine));
        Assert.True(File.Exists(Path.Combine(_f.Target.Folder, "song (2).mp3")));
        Assert.Equal(2, _f.Added.Length);
        // The list names the file that is there, not the one that was asked for.
        Assert.Equal("song (2).mp3", Assert.Single(_f.Manager.Store.Finished).FileName);
    }

    [Theory]
    [InlineData("text/html", "<html><body><a href='song.mp3'>get it</a></body></html>")]
    [InlineData("application/octet-stream", "<!doctype html><html></html>")]
    public async Task A_web_page_is_called_one_and_never_read_for_files(string type, string body)
    {
        _server.Answer = _ => ServerReply.Text(type, body);

        var page = await Assert.ThrowsAsync<DownloadIsPageException>(() => _f.Look(_server.Address("/page")));

        Assert.Equal(_server.Address("/page"), page.Page);
        Assert.Single(_server.Requests);
    }

    [Theory]
    [InlineData("/live", "application/vnd.apple.mpegurl", "#EXTM3U\n")]
    [InlineData("/live", "application/dash+xml", "<MPD/>")]
    [InlineData("/list.m3u8", "application/octet-stream", "x")]
    [InlineData("/list", "audio/mpeg", "#EXTM3U\nhttp://elsewhere/a.ts\n")]
    [InlineData("/list.pls", "text/plain", "[playlist]\n")]
    public async Task A_playlist_or_stream_list_is_refused(string path, string type, string body)
    {
        _server.Answer = _ => ServerReply.Text(type, body);

        var refused = await Assert.ThrowsAsync<DownloadException>(() => _f.Look(_server.Address(path)));

        Assert.Contains("playlist or a stream", refused.Message);
        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task Something_that_only_calls_itself_audio_is_refused()
    {
        _server.Answer = _ => new ServerReply { Body = [(byte)'M', (byte)'Z', .. new byte[5000]] };

        var refused = await Assert.ThrowsAsync<DownloadException>(() => _f.Look(_server.Address()));

        Assert.Contains("not an audio file", refused.Message);
    }

    [Fact]
    public async Task A_good_start_with_rubbish_behind_it_is_not_added()
    {
        Serve([0xFF, 0xFB, 0x90, 0x64, .. new byte[20_000]]);

        var end = await _f.Run((await _f.Look(_server.Address())).ToJob());

        Assert.Equal(DownloadStage.Failed, end.Stage);
        Assert.False(end.CanResume);
        Assert.Empty(_f.Added);
        Assert.Empty(_f.Left);
    }

    [Theory]
    [InlineData(".flac", "flac")]
    [InlineData(".wav", "pcm_s16le")]
    public async Task A_file_cut_off_on_the_server_is_not_added(string extension, string codec)
    {
        var file = _f.Audio(extension, codec, seconds: 30);
        Serve(file[..(file.Length / 2)]);

        var end = await _f.Run((await _f.Look(_server.Address("/a" + extension))).ToJob());

        Assert.Equal(DownloadStage.Failed, end.Stage);
        Assert.Empty(_f.Added);
        Assert.Empty(_f.Left);
    }

    [Fact]
    public async Task A_film_in_an_audio_wrapper_is_not_added()
    {
        var path = Path.Combine(_f.Root, "film.mp4");
        FfmpegTool.Run("-f", "lavfi", "-i", "color=c=red:s=64x64:d=2", "-f", "lavfi", "-i", "sine=duration=2", "-c:v", "mpeg4", "-c:a", "aac", "-shortest", path);
        Serve(File.ReadAllBytes(path), type: "video/mp4");

        var end = await _f.Run((await _f.Look(_server.Address("/a.m4a"))).ToJob());

        Assert.Equal(DownloadStage.Failed, end.Stage);
        Assert.Contains("moving pictures", end.Message);
        Assert.Empty(_f.Added);
    }

    [Fact]
    public async Task A_song_with_a_large_cover_in_front_is_still_a_song()
    {
        var picture = Path.Combine(_f.Root, "big.png");
        FfmpegTool.Run("-f", "lavfi", "-i", "nullsrc=s=600x600:d=1,geq=random(1)*255:128:128", "-frames:v", "1", picture);
        var plain = Path.Combine(_f.Root, "plain.mp3");
        File.WriteAllBytes(plain, _f.Audio(seconds: 4));
        var covered = Path.Combine(_f.Root, "covered.mp3");
        FfmpegTool.Run("-i", plain, "-i", picture, "-map", "0", "-map", "1", "-c", "copy", "-id3v2_version", "3", "-disposition:v", "attached_pic", covered);
        var file = File.ReadAllBytes(covered);
        Assert.True(file.Length > 3 * AudioSniffer.Enough);
        Serve(file);

        var end = await _f.Run((await _f.Look(_server.Address())).ToJob());

        Assert.True(end.Stage == DownloadStage.Done, end.Message);
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused_before_and_while_downloading()
    {
        var file = _f.Audio(seconds: 4);
        var small = new DownloadEngine(_f.Rules.CreateClient(), _f.Rules, new AudioCheck(() => true, (p, c) => AudioDigest.Scan(p, 0, c)), new TagEditor(), "x")
        {
            MaxSize = 10_000,
        };
        Serve(file);
        var stated = await Assert.ThrowsAsync<DownloadException>(() => small.LookAsync(_server.Address(), DownloadKind.PastedLink, TestContext.Current.CancellationToken));
        Assert.Contains("larger than", stated.Message);

        _server.Answer = _ => new ServerReply { Body = file, NoLength = true };
        var offer = await small.LookAsync(_server.Address(), DownloadKind.PastedLink, TestContext.Current.CancellationToken);
        Assert.Null(offer.Size);
        var partial = Path.Combine(_f.Root, "part");
        var streamed = await Assert.ThrowsAsync<DownloadException>(
            () => small.FetchAsync(offer.ToJob(), partial, null, null, TestContext.Current.CancellationToken));
        Assert.Contains("larger than", streamed.Message);
        Assert.False(streamed.CanResume);
        Assert.True(new FileInfo(partial).Length <= 10_000);
    }

    [Fact]
    public async Task Without_a_decoder_nothing_is_asked_for()
    {
        var engine = new DownloadEngine(_f.Rules.CreateClient(), _f.Rules, new AudioCheck(() => false, (_, _) => null), new TagEditor(), "x");

        await Assert.ThrowsAsync<DownloadException>(() => engine.LookAsync(_server.Address(), DownloadKind.PastedLink, TestContext.Current.CancellationToken));

        Assert.Empty(_server.Requests);
    }

    // ---- redirects ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("http://example.org/a.mp3", "https")]
    [InlineData("https://192.168.1.1/a.mp3", "this device")]
    [InlineData("https://localhost/a.mp3", "this device")]
    [InlineData("https://www.youtube.com/a.mp3", "streaming service")]
    [InlineData("file:///C:/Windows/win.ini", "https")]
    public async Task A_redirect_is_held_to_the_same_rules(string to, string reason)
    {
        _server.Answer = _ => ServerReply.Redirect(to);

        var refused = await Assert.ThrowsAsync<DownloadException>(() => _f.Look(_server.Address()));

        Assert.Contains(reason, refused.Message);
        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task A_redirect_that_never_ends_is_given_up()
    {
        _server.Answer = _ => ServerReply.Redirect("/again");

        await Assert.ThrowsAsync<DownloadException>(() => _f.Look(_server.Address()));

        Assert.Equal(6, _server.Requests.Count);
    }

    [Fact]
    public async Task A_new_host_is_shown_and_one_that_changes_after_agreeing_stops_the_download()
    {
        var file = _f.Audio(seconds: 3);
        _server.Answer = request => request.Path == "/start" ? ServerReply.Redirect(_server.Address("/real.mp3", "localhost").AbsoluteUri) : ServerReply.File(request, file);

        var offer = await _f.Look(_server.Address("/start"));

        Assert.True(offer.HostChanged);
        Assert.Equal("localhost", offer.Final.Host);
        Assert.Equal("real.mp3", offer.FileName);

        _server.Answer = request => ServerReply.File(request, file); // no redirect any more: another host than agreed
        var end = await _f.Run(offer.ToJob());
        Assert.Equal(DownloadStage.Failed, end.Stage);
        Assert.Contains("instead of localhost", end.Message);
        Assert.Empty(_f.Added);
    }

    [Fact]
    public async Task No_cookie_or_referrer_is_ever_sent_back()
    {
        var file = _f.Audio(seconds: 3);
        _server.Answer = request =>
        {
            if (request.Path == "/start")
                return new ServerReply { Status = 302, Type = null, Headers = { ("Location", "/real.mp3"), ("Set-Cookie", "id=1; Path=/") } };
            var reply = ServerReply.File(request, file);
            reply.Headers.Add(("Set-Cookie", "more=2; Path=/"));
            return reply;
        };

        await _f.Run((await _f.Look(_server.Address("/start"))).ToJob());

        Assert.Single(_f.Added);
        Assert.All(_server.Requests, request =>
        {
            Assert.Null(request.Header("Cookie"));
            Assert.Null(request.Header("Referer"));
            Assert.Equal(DownloadEngine.PlainAgent, request.Header("User-Agent"));
            Assert.Equal("identity", request.Header("Accept-Encoding"));
        });
    }

    // ---- pause, cancel, resume ---------------------------------------------------------------------

    private async Task<(byte[] File, DownloadJob Job)> StartHeld(string? etag = "\"v1\"", string path = "/song.mp3")
    {
        var file = _f.Audio(seconds: 20);
        Serve(file, etag);
        var job = (await _f.Look(_server.Address(path))).ToJob();
        Serve(file, etag, holdAfter: 100_000);
        Assert.True(_f.Manager.Start(job));
        await _f.Until(100_000);
        return (file, job);
    }

    [Fact]
    public async Task Cancel_deletes_what_had_come()
    {
        var (_, job) = await StartHeld();
        Assert.False(_f.Manager.Start(job)); // one at a time

        _f.Manager.Cancel(job.Id);
        await _f.Manager.Completion;

        Assert.Empty(_f.Left);
        Assert.Empty(_f.Added);
        Assert.Empty(_f.Manager.Store.Unfinished);
        Assert.Null(_f.Manager.Status);
    }

    [Fact]
    public async Task Pause_keeps_the_part_and_resume_asks_only_for_the_rest()
    {
        var (file, job) = await StartHeld();

        _f.Manager.Pause();
        await _f.Manager.Completion;

        Assert.Equal(DownloadStage.Paused, _f.Manager.Status!.Stage);
        Assert.Empty(_f.Added);
        var had = new FileInfo(_f.Manager.Store.PartialPath(job.Id)).Length;
        Assert.InRange(had, 100_000, file.Length - 1);

        Serve(file);
        var end = await _f.Run(_f.Manager.Store.Job(job.Id)!);

        Assert.True(end.Stage == DownloadStage.Done, end.Message);
        Assert.Null(end.Message);
        Assert.Equal(had, _server.Requests[^1].From);
        Assert.Equal("\"v1\"", _server.Requests[^1].Header("If-Range"));
        Assert.Equal(file, File.ReadAllBytes(Assert.Single(_f.Added)));
        Assert.Empty(_f.Left);
    }

    [Fact]
    public async Task A_lost_connection_can_be_resumed_after_a_restart()
    {
        var file = _f.Audio(seconds: 20);
        Serve(file);
        var job = (await _f.Look(_server.Address())).ToJob();
        Serve(file, cutAfter: 90_000);

        var lost = await _f.Run(job);

        Assert.Equal(DownloadStage.Failed, lost.Stage);
        Assert.True(lost.CanResume, lost.Message);

        var after = _f.NewManager(); // the app is closed and opened again
        var waiting = Assert.Single(after.Store.Unfinished);
        Serve(file);
        var end = await _f.Run(waiting);

        Assert.True(end.Stage == DownloadStage.Done, end.Message);
        Assert.True(_server.Requests[^1].From > 0);
        Assert.Equal(file, File.ReadAllBytes(Assert.Single(_f.Added)));
    }

    [Fact]
    public async Task A_file_that_changed_on_the_server_starts_again_and_says_so()
    {
        var (_, job) = await StartHeld();
        _f.Manager.Pause();
        await _f.Manager.Completion;
        var changed = _f.Audio(seconds: 15);

        Serve(changed, "\"v2\""); // the old If-Range no longer matches: the whole new file comes
        var end = await _f.Run(_f.Manager.Store.Job(job.Id)!);

        Assert.True(end.Stage == DownloadStage.Done, end.Message);
        Assert.Contains("started again", end.Message);
        Assert.Equal(changed, File.ReadAllBytes(Assert.Single(_f.Added)));
    }

    [Fact]
    public async Task A_part_of_another_version_is_never_put_on_the_end()
    {
        var (file, job) = await StartHeld();
        _f.Manager.Pause();
        await _f.Manager.Completion;

        // A server that ignores If-Range and sends the middle of a newer file.
        _server.Answer = request => new ServerReply
        {
            Status = 206, ETag = "\"v2\"", Body = new byte[file.Length - (int)request.From!.Value],
            Headers = { ("Content-Range", $"bytes {request.From}-{file.Length - 1}/{file.Length}") },
        };
        var end = await _f.Run(_f.Manager.Store.Job(job.Id)!);

        Assert.Equal(DownloadStage.Failed, end.Stage);
        Assert.False(File.Exists(_f.Manager.Store.PartialPath(job.Id)));
        Assert.Empty(_f.Added);

        Serve(file, "\"v2\""); // resuming then starts from the beginning
        var again = await _f.Run(_f.Manager.Store.Job(job.Id)!);
        Assert.True(again.Stage == DownloadStage.Done, again.Message);
        Assert.Null(_server.Requests[^1].From);
    }

    [Fact]
    public async Task Without_a_way_to_tell_versions_apart_it_starts_again()
    {
        var (file, job) = await StartHeld(etag: null);
        _f.Manager.Pause();
        await _f.Manager.Completion;

        Serve(file, etag: null);
        var end = await _f.Run(_f.Manager.Store.Job(job.Id)!);

        Assert.True(end.Stage == DownloadStage.Done, end.Message);
        Assert.Null(_server.Requests[^1].From);
        Assert.Contains("started again", end.Message);
        Assert.Equal(file, File.ReadAllBytes(Assert.Single(_f.Added)));
    }

    [Fact]
    public async Task A_range_the_server_cannot_give_starts_again()
    {
        var (file, job) = await StartHeld();
        _f.Manager.Pause();
        await _f.Manager.Completion;

        _server.Answer = request => request.From is not null ? new ServerReply { Status = 416, Type = null } : ServerReply.File(request, file);
        var end = await _f.Run(_f.Manager.Store.Job(job.Id)!);

        Assert.True(end.Stage == DownloadStage.Done, end.Message);
        Assert.Equal(file, File.ReadAllBytes(Assert.Single(_f.Added)));
    }

    [Fact]
    public async Task An_address_with_a_token_is_not_written_to_disk_or_into_the_record()
    {
        var (file, job) = await StartHeld(path: "/song.mp3?token=very-secret");
        Assert.DoesNotContain(_f.Left, f => f.EndsWith(".job.json", StringComparison.Ordinal));
        _f.Manager.Pause();
        await _f.Manager.Completion;
        Serve(file);
        await _f.Run(_f.Manager.Store.Job(job.Id)!);

        Assert.DoesNotContain("very-secret", File.ReadAllText(Path.Combine(_f.Root, "downloads.json")));
        Assert.EndsWith("/song.mp3?...", _f.Manager.Store.Finished[0].Address);
    }

    [Fact]
    public void Clearing_up_after_a_stopped_app_touches_only_the_stores_own_files()
    {
        Directory.CreateDirectory(_f.Work);
        var id = new string('a', 32);
        string[] orphans = [id + ".pardehpart", id + ".check.mp3", id + ".tag.mp3"];
        string[] others = ["notes.txt", "song.mp3", id + ".mp3", "holiday.pardehpart"];
        foreach (var name in orphans.Concat(others))
            File.WriteAllText(Path.Combine(_f.Work, name), "x");

        _f.NewManager();

        Assert.Equal(others.Order(), _f.Left.Select(Path.GetFileName).Order());
    }

    // ---- catalog files -----------------------------------------------------------------------------

    private static readonly DownloadCredit Credit = new("Wikimedia Commons", "Test tone", "Some Maker", "CC BY-SA 4.0",
        "https://creativecommons.org/licenses/by-sa/4.0", "https://commons.wikimedia.org/wiki/File:Test_(tone).mp3");

    [Theory]
    [InlineData(".mp3", "libmp3lame", "id3v2:WOAS")]
    [InlineData(".ogg", "libvorbis", "xiph:SOURCEURL")]
    [InlineData(".flac", "flac", "xiph:SOURCEURL")]
    public async Task A_catalog_files_licence_and_page_go_into_it_and_clean_up_keeps_them(string extension, string codec, string field)
    {
        var file = _f.Audio(extension, codec, seconds: 4);
        Serve(file);
        var offer = (await _f.Look(_server.Address("/t" + extension))) with
        {
            Kind = DownloadKind.PastedLink, // the test server is not the catalog's host
            Credit = Credit,
            Sha1 = Convert.ToHexString(SHA1.HashData(file)),
        };

        var end = await _f.Run(offer.ToJob());

        Assert.True(end.Stage == DownloadStage.Done, end.Message);
        var added = Assert.Single(_f.Added);
        var tags = new TagEditor().Read(added, Path.GetFileName(added));
        Assert.Equal(Credit.CopyrightLine, tags.Copyright);
        Assert.Equal(Credit.SourcePage, tags.TextOf(field));
        Assert.Equal("Test tone", tags.Title);
        Assert.Equal(AudioDigest.Compute(Path.Combine(_f.Root, $"source-{codec}-4{extension}")), AudioDigest.Compute(added));
        Assert.Equal(Credit, _f.Manager.Store.Finished[0].Credit);
        var proposal = CleanupAnalyzer.Analyze(new TrackFacts(added, Path.GetFileName(added), tags, null));
        Assert.DoesNotContain(proposal.Suggestions, s => s.Field is CleanupField.Copyright or CleanupField.Text);
    }

    [Fact]
    public async Task A_no_derivatives_file_is_left_exactly_as_it_came()
    {
        var file = _f.Audio(seconds: 3);
        Serve(file);
        var offer = (await _f.Look(_server.Address())) with { Credit = Credit with { NoDerivatives = true } };

        await _f.Run(offer.ToJob());

        Assert.Equal(file, File.ReadAllBytes(Assert.Single(_f.Added)));
    }

    [Fact]
    public async Task A_file_that_does_not_match_the_catalogs_checksum_is_deleted()
    {
        Serve(_f.Audio(seconds: 3));
        var offer = (await _f.Look(_server.Address())) with { Sha1 = new string('0', 40) };

        var end = await _f.Run(offer.ToJob());

        Assert.Equal(DownloadStage.Failed, end.Stage);
        Assert.Contains("checksum", end.Message);
        Assert.Empty(_f.Added);
        Assert.Empty(_f.Left);
    }

    [Fact]
    public async Task A_folder_that_cannot_take_the_file_keeps_it_for_another_try()
    {
        Serve(_f.Audio(seconds: 3));
        File.WriteAllText(_f.Target.Folder, "a file where the folder should be");

        var end = await _f.Run((await _f.Look(_server.Address())).ToJob());

        Assert.Equal(DownloadStage.Failed, end.Stage);
        Assert.True(end.CanResume, end.Message);

        File.Delete(_f.Target.Folder);
        var requests = _server.Requests.Count;
        var again = await _f.Run(_f.Manager.Store.Job(end.Job.Id)!);
        Assert.True(again.Stage == DownloadStage.Done, again.Message);
        Assert.Equal(requests, _server.Requests.Count); // all of it was there: nothing is fetched twice
    }
}

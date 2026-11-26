using System.Net;
using Pardeh.Core.Updates;
using Pardeh.UI;

namespace Pardeh.Skinning.Tests;

public sealed class UpdateCheckTests
{
    private sealed class GitHub(HttpStatusCode status, string body = "", Exception? failure = null) : HttpMessageHandler
    {
        public HttpRequestMessage? Seen { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request;
            return failure is not null
                ? Task.FromException<HttpResponseMessage>(failure)
                : Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static Task<UpdateResult> Check(GitHub site, string current) =>
        UpdateCheck.Run(new HttpClient(site), current, TestContext.Current.CancellationToken);

    private static string Release(string tag, string page) => $$"""{"tag_name":"{{tag}}","html_url":"{{page}}"}""";

    [Fact]
    public async Task A_newer_release_is_offered_with_its_page()
    {
        var page = UpdateCheck.ReleasesPage + "/tag/v1.10.0";
        var site = new GitHub(HttpStatusCode.OK, Release("v1.10.0", page));
        var result = await Check(site, "1.9.3");
        Assert.Equal(UpdateState.Available, result.State);
        Assert.Equal("1.10.0", result.Latest); // compared as numbers, not as text
        Assert.Equal(page, result.Page);
        Assert.Equal("api.github.com", site.Seen!.RequestUri!.Host);
        Assert.Equal("Pardeh", site.Seen.Headers.UserAgent.ToString()); // no version, system or anything about the user
    }

    [Theory]
    [InlineData("1.2.0")]
    [InlineData("1.3.0+0123456789")] // a build ahead of the latest release
    public async Task The_same_or_a_newer_copy_is_up_to_date(string current)
    {
        var result = await Check(new GitHub(HttpStatusCode.OK, Release("v1.2.0", UpdateCheck.ReleasesPage + "/tag/v1.2.0")), current);
        Assert.Equal(UpdateState.UpToDate, result.State);
    }

    [Theory]
    [InlineData("https://example.com/Pardeh.apk")]
    [InlineData("https://github.com/someone-else/Pardeh/releases/tag/v9.0.0")]
    [InlineData("https://github.com/behradbhrmi/Pardeh/releases.example.com/x")]
    public async Task Only_a_page_of_the_project_is_ever_offered(string page)
    {
        var result = await Check(new GitHub(HttpStatusCode.OK, Release("v9.0.0", page)), "1.0.0");
        Assert.Equal(UpdateState.Available, result.State);
        Assert.Equal(UpdateCheck.ReleasesPage, result.Page);
    }

    [Fact]
    public async Task No_release_yet_is_said_so()
    {
        var result = await Check(new GitHub(HttpStatusCode.NotFound), "1.0.0");
        Assert.Equal(UpdateState.NoRelease, result.State);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "")]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    [InlineData(HttpStatusCode.OK, "<html>not json</html>")]
    [InlineData(HttpStatusCode.OK, "[]")]
    [InlineData(HttpStatusCode.OK, """{"tag_name":"nightly"}""")]
    public async Task A_bad_answer_is_a_failure_with_a_reason(HttpStatusCode status, string body)
    {
        var result = await Check(new GitHub(status, body), "1.0.0");
        Assert.Equal(UpdateState.Failed, result.State);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public async Task No_connection_is_a_failure_not_a_crash()
    {
        var result = await Check(new GitHub(HttpStatusCode.OK, failure: new HttpRequestException("offline")), "1.0.0");
        Assert.Equal(UpdateState.Failed, result.State);
    }

    [Theory]
    [InlineData("1.2.0+8dda55a1c2f30b", "1.2.0", "8dda55a")]
    [InlineData("1.2.0", "1.2.0", null)]
    [InlineData("", "unknown", null)]
    public void The_build_stamp_gives_the_version_and_commit(string stamp, string version, string? commit)
    {
        Assert.Equal(version, AppInfo.VersionOf(stamp));
        Assert.Equal(commit, AppInfo.CommitOf(stamp));
    }

    private sealed class Installed : IPlatformServices
    {
        public string? AppVersion => "7.4.2";

        public string? AppBuild => "version code 7004002";
    }

    /// <summary>An Android release stamps the package, not the libraries: About showed 1.0.0 for every release.</summary>
    [Fact]
    public void The_platforms_installed_version_wins_over_the_assemblies()
    {
        var before = PlatformServices.Current;
        try
        {
            PlatformServices.Current = new Installed();
            Assert.Equal("7.4.2", AppInfo.Version);
            Assert.Contains(("Version", "7.4.2"), AppInfo.Facts());
            Assert.Contains(AppInfo.Facts(), f => f.Label == "Build" && f.Value.StartsWith("version code 7004002", StringComparison.Ordinal));
            Assert.Contains("Version: 7.4.2", AppInfo.Details());
        }
        finally
        {
            PlatformServices.Current = before;
        }
    }

    [Fact]
    public void Every_link_is_a_page_of_the_project()
    {
        Assert.All(AppInfo.Links, link => Assert.StartsWith(UpdateCheck.Repository, link.Url, StringComparison.Ordinal));
        Assert.Contains(AppInfo.Links, link => link.Url == AppInfo.SourcePage);
    }
}

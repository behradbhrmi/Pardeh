using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pardeh.Core.Updates;

public enum UpdateState
{
    UpToDate,

    /// <summary>A newer release is published; <see cref="UpdateResult.Page"/> is its web page.</summary>
    Available,

    /// <summary>The project has published no release yet.</summary>
    NoRelease,

    Failed,
}

/// <param name="State">What was found.</param>
/// <param name="Latest">The latest release's version, when there is one.</param>
/// <param name="Page">A web page of the project: the newer release, or else the list of releases.</param>
/// <param name="Message">One sentence to show.</param>
public sealed record UpdateResult(UpdateState State, string? Latest, string Page, string Message);

/// <summary>
/// Asks GitHub for the latest release. It runs only when the user asks, sends nothing but the request itself, and downloads
/// and installs nothing: the most it does is name a web page of the project for the user to open.
/// </summary>
public static partial class UpdateCheck
{
    public const string Repository = "https://github.com/behradbhrmi/Pardeh";
    public const string ReleasesPage = Repository + "/releases";
    private const string Api = "https://api.github.com/repos/behradbhrmi/Pardeh/releases/latest";

    [GeneratedRegex(@"^v?(\d{1,4})\.(\d{1,4})\.(\d{1,4})")]
    private static partial Regex VersionPattern();

    /// <summary>The three numbers of a version or tag ("v1.2.0", "1.2.0+abc"), or null.</summary>
    public static Version? Parse(string? text) =>
        text is not null && VersionPattern().Match(text.Trim()) is { Success: true } m
            ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value))
            : null;

    public static async Task<UpdateResult> Run(HttpClient http, string currentVersion, CancellationToken cancel = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Api);
            request.Headers.TryAddWithoutValidation("User-Agent", "Pardeh"); // GitHub refuses requests without one
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new(UpdateState.NoRelease, null, ReleasesPage, "No release has been published yet.");
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                return Failed("GitHub is not answering more checks right now. Try again later.");
            if (!response.IsSuccessStatusCode)
                return Failed($"GitHub answered with error {(int)response.StatusCode}.");

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false));
            var tag = Text(json.RootElement, "tag_name");
            if (Parse(tag) is not { } latest)
                return Failed("The latest release has no version number.");
            var name = latest.ToString(3);
            if (Parse(currentVersion) is not { } current)
                return Failed($"The latest release is {name}, but this copy's version is unknown.");
            if (latest <= current)
                return new(UpdateState.UpToDate, name, ReleasesPage, $"You have the latest version ({current.ToString(3)}).");

            // Only a page of this project is ever offered, whatever the answer says.
            var page = Text(json.RootElement, "html_url") is { } url && url.StartsWith(ReleasesPage + "/", StringComparison.Ordinal) ? url : ReleasesPage;
            return new(UpdateState.Available, name, page, $"Version {name} is available. You have {current.ToString(3)}.");
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return Failed("GitHub did not answer in time.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
        {
            return Failed("Could not check: no connection to GitHub, or its answer could not be read.");
        }
    }

    private static UpdateResult Failed(string message) => new(UpdateState.Failed, null, ReleasesPage, message);

    private static string? Text(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

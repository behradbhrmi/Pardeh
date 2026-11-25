namespace Pardeh.Core.Playback;

/// <summary>How network media is read.</summary>
/// <param name="BufferSeconds">Sound read ahead of what is playing.</param>
/// <param name="Reconnect">Connect again when a connection drops.</param>
public sealed record NetworkOptions(int BufferSeconds = 10, bool Reconnect = true)
{
    public static readonly int[] BufferChoices = [3, 10, 30, 60];

    /// <summary>How long one attempt to connect may take.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(25);

    /// <summary>The client name sent to web servers.</summary>
    /// <remarks>PARDEH_USER_AGENT is a development aid: test runs against outside servers send a generic name.</remarks>
    public string UserAgent { get; init; } = Environment.GetEnvironmentVariable("PARDEH_USER_AGENT") is { Length: > 0 } name ? name : "Pardeh/1.0";

    public const int DefaultVideoHeight = 720;

    public static readonly int[] VideoHeightChoices = [360, 480, 720, 1080];

    /// <summary>The tallest picture to take from a stream that offers several sizes.</summary>
    public int MaxVideoHeight { get; init; } = DefaultVideoHeight;
}

/// <summary>What a network item is doing, for the interface.</summary>
public enum NetworkPhase
{
    /// <summary>Nothing to report (a local file, or a stream playing normally).</summary>
    None,
    Connecting,
    Buffering,
    Reconnecting,
    Failed,
}

/// <summary>Addresses of media on the network, and care for the secrets some of them carry.</summary>
public static class NetworkMedia
{
    private static readonly string[] Schemes = ["http", "https", "rtsp", "rtsps", "rtmp", "rtmps", "srt", "mms", "mmsh", "rtp", "udp"];

    private static readonly string[] SecretNames = ["token", "key", "apikey", "api_key", "auth", "password", "pass", "pwd", "secret", "sig", "signature", "x-amz-signature", "x-amz-credential", "access_token", "u", "p", "t", "s", "session", "jwt"];

    public static bool IsNetwork(string? source)
    {
        if (string.IsNullOrEmpty(source))
            return false;
        var at = source.IndexOf("://", StringComparison.Ordinal);
        return at > 1 && Schemes.Contains(source[..at].ToLowerInvariant());
    }

    /// <summary>The first network address in some text (a dropped link, or what another app shares: "A title https://..."), or null.</summary>
    public static string? FindAddress(string? text)
    {
        if (text is null)
            return null;
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            // Quotes and brackets around a link, and the full stop that ends a sentence, are not part of it.
            var candidate = word.TrimStart('<', '(', '[', '"', '\'');
            // A closing bracket stays when the address itself opened it.
            while (candidate.Length > 0 && (candidate[^1] is '>' or ']' or '"' or '\'' or '.' or ',' or ';' or '!' or '?'
                || candidate[^1] == ')' && candidate.Count(c => c == ')') > candidate.Count(c => c == '(')))
            {
                candidate = candidate[..^1];
            }

            if (IsNetwork(candidate) && candidate.Length > candidate.IndexOf("://", StringComparison.Ordinal) + 3)
                return candidate;
        }

        return null;
    }

    /// <summary>True when the address holds a login or what looks like an access token: it must not be written to disk or to the log.</summary>
    public static bool HasSecret(string source)
    {
        if (!IsNetwork(source) || !Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return false;
        if (uri.UserInfo.Length > 0)
            return true;
        return uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=')[0].ToLowerInvariant())
            .Any(name => SecretNames.Contains(name));
    }

    /// <summary>The address as it may appear in a log or a message: no login, no query.</summary>
    public static string Redact(string source)
    {
        if (!IsNetwork(source))
            return source;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return source[..(source.IndexOf("://", StringComparison.Ordinal) + 3)] + "...";
        var port = uri.IsDefaultPort || uri.Port < 0 ? string.Empty : ":" + uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"{uri.Scheme}://{uri.Host}{port}{uri.AbsolutePath}{(uri.Query.Length > 0 ? "?..." : string.Empty)}";
    }

    /// <summary>A redacted copy of any text that quotes the address (an error message from the decoder).</summary>
    public static string Redact(string text, string source) =>
        IsNetwork(source) ? text.Replace(source, Redact(source), StringComparison.OrdinalIgnoreCase) : text;

    /// <summary>Puts an address at the top of the recent list (addresses with secrets are never remembered).</summary>
    public static void Remember(List<string> recent, string address, int most = 15)
    {
        if (!IsNetwork(address) || HasSecret(address))
            return;
        recent.RemoveAll(a => a.Equals(address, StringComparison.Ordinal));
        recent.Insert(0, address);
        if (recent.Count > most)
            recent.RemoveRange(most, recent.Count - most);
    }

    /// <summary>True for an address of a playlist file that has to be downloaded and read (HLS lists are the decoder's job).</summary>
    public static bool LooksLikePlaylist(string address) =>
        IsNetwork(address) && Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && (Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() is ".m3u" or ".pls" or ".asx" or ".xspf" or ".wpl");
}

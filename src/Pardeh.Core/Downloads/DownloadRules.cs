using System.Net;
using System.Net.Sockets;

namespace Pardeh.Core.Downloads;

public enum DownloadKind
{
    /// <summary>An address the user pasted. Pardeh knows nothing about its licence.</summary>
    PastedLink,

    /// <summary>A file a catalog offers with its licence.</summary>
    Catalog,
}

/// <summary>A download that can't go on. The message is for the user.</summary>
public class DownloadException(string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>The partial file is still good: resuming may work.</summary>
    public bool CanResume { get; init; }
}

/// <summary>The address gave a web page. Pardeh doesn't read pages for files; the user may open it.</summary>
public sealed class DownloadIsPageException(Uri page) : DownloadException("This is a web page, not an audio file. Pardeh does not search pages for files.")
{
    public Uri Page => page;
}

/// <summary>
/// What may be downloaded from where. Every address is checked before each request, redirects included, and the connection
/// goes only to an address that passed: nothing here can be turned on this machine or its network.
/// </summary>
public class DownloadRules
{
    public const long MaxSize = 200L * 1024 * 1024;

    /// <summary>The hosts catalog files come from. A redirect may not leave them.</summary>
    private static readonly HashSet<string> CatalogHosts = new(StringComparer.OrdinalIgnoreCase) { "upload.wikimedia.org" };

    // Video and streaming services. Their audio is not offered as a file to download, and Pardeh takes nothing out of them.
    private static readonly string[] StreamingServices =
    [
        "youtube.com", "youtu.be", "youtube-nocookie.com", "googlevideo.com", "ytimg.com",
        "soundcloud.com", "sndcdn.com", "soundcloud.app.goo.gl",
        "spotify.com", "spotify.link", "scdn.co", "spotifycdn.com",
        "music.apple.com", "itunes.apple.com", "tv.apple.com",
        "deezer.com", "dzcdn.net", "deezer.page.link",
        "tidal.com", "qobuz.com", "pandora.com", "napster.com",
        "music.amazon.com", "primevideo.com", "media-amazon.com",
        "anghami.com", "boomplay.com", "boomplaymusic.com", "jiosaavn.com", "saavncdn.com", "gaana.com", "audiomack.com",
        "mixcloud.com", "bcbits.com", "music.yandex.ru", "music.yandex.com", "radiojavan.com",
        "tiktok.com", "tiktokcdn.com", "tiktokv.com",
        "instagram.com", "cdninstagram.com", "facebook.com", "fb.watch", "fbcdn.net", "twitter.com", "x.com", "twimg.com",
        "vimeo.com", "vimeocdn.com", "dailymotion.com", "dai.ly", "dmcdn.net", "twitch.tv", "ttvnw.net",
        "netflix.com", "nflxvideo.net", "aparat.com",
    ];

    /// <summary>Test hook: addresses let past the https, port and local-network rules. Only the test project can set it.</summary>
    internal Func<Uri, bool>? TestAllows { get; set; }

    /// <summary>Off in tests, so that they behave the same on a machine with a system proxy.</summary>
    internal bool UseProxy { get; set; } = true;

    /// <summary>Resolves a name. Replaced in tests.</summary>
    internal Func<string, CancellationToken, Task<IPAddress[]>> Resolve { get; set; } = Dns.GetHostAddressesAsync;

    public static bool IsStreamingService(string host)
    {
        var name = host.TrimEnd('.').ToLowerInvariant();
        return StreamingServices.Any(s => name == s || name.EndsWith("." + s, StringComparison.Ordinal));
    }

    public static bool IsWikimedia(string host)
    {
        var name = host.TrimEnd('.').ToLowerInvariant();
        return name == "wikimedia.org" || name.EndsWith(".wikimedia.org", StringComparison.Ordinal);
    }

    /// <summary>Why the address is refused by what it says, or null. The name is not looked up here.</summary>
    public string? WhyRefused(Uri address, DownloadKind kind)
    {
        if (TestAllows?.Invoke(address) == true)
            return null;
        if (!address.IsAbsoluteUri || address.Scheme != Uri.UriSchemeHttps)
            return "Only secure (https) addresses are downloaded.";
        if (address.UserInfo.Length > 0)
            return "Addresses with a user name or password in them are not used.";
        if (!address.IsDefaultPort)
            return "Addresses with their own port number are not downloaded.";
        if (address.HostNameType != UriHostNameType.Dns || !NetworkRules.IsPublicName(address.IdnHost))
            return "Only addresses of public web sites are downloaded, not of this device or its network.";
        if (IsStreamingService(address.IdnHost))
            return $"{address.IdnHost} is a video or streaming service. Pardeh does not take files out of those services.";
        if (kind == DownloadKind.Catalog && !CatalogHosts.Contains(address.IdnHost))
            return $"The catalog's file is not where it should be ({address.IdnHost}).";
        return null;
    }

    /// <summary>Throws unless the address passes, name look-up included.</summary>
    public async Task CheckAsync(Uri address, DownloadKind kind, CancellationToken cancel)
    {
        if (WhyRefused(address, kind) is { } why)
            throw new DownloadException(why);
        if (TestAllows?.Invoke(address) == true)
            return;
        await PublicAddressesAsync(address.IdnHost, cancel).ConfigureAwait(false);
    }

    private async Task<IPAddress[]> PublicAddressesAsync(string host, CancellationToken cancel)
    {
        IPAddress[] found;
        try
        {
            found = await Resolve(host, cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            throw new DownloadException($"The name {host} couldn't be looked up. Check the internet connection.", ex) { CanResume = true };
        }

        // One private address among them is enough: which one would be used is not ours to say.
        if (found.Length == 0 || !found.All(NetworkRules.IsPublicAddress))
            throw new DownloadException($"{host} leads to an address on this device or its network (or one a network filter uses), so nothing is downloaded from it.");
        return found;
    }

    /// <summary>
    /// A client for downloads: no redirects followed by itself, no cookies, no sign-in, nothing unpacked, and connections only
    /// to public addresses.
    /// </summary>
    public HttpClient CreateClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        Credentials = null,
        PreAuthenticate = false,
        UseProxy = UseProxy,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(15),
        ConnectCallback = ConnectAsync,
    })
    { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancel)
    {
        var end = context.DnsEndPoint;
        var target = context.InitialRequestMessage.RequestUri!;
        IPAddress[] addresses;
        // Through a system proxy the connection is to the proxy, which looks the name up itself: only the check made before
        // the request applies then.
        if (IsProxy(end, target) || TestAllows?.Invoke(target) == true)
            addresses = await Dns.GetHostAddressesAsync(end.Host, cancel).ConfigureAwait(false);
        else
            addresses = await PublicAddressesAsync(end.Host, cancel).ConfigureAwait(false); // checked here and used: no second look-up

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, end.Port, cancel).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private bool IsProxy(DnsEndPoint end, Uri target)
    {
        if (!UseProxy)
            return false;
        var proxy = HttpClient.DefaultProxy;
        return !proxy.IsBypassed(target) && proxy.GetProxy(target) is { } through && through != target
            && string.Equals(through.IdnHost, end.Host, StringComparison.OrdinalIgnoreCase) && through.Port == end.Port;
    }
}

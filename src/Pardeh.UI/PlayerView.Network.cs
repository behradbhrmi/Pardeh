using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using Pardeh.Core.Playback;
using Pardeh.UI.Rendering;

namespace Pardeh.UI;

/// <summary>Opening addresses (with the recent ones), downloaded playlists, and the banner that shows what the network is doing.</summary>
internal sealed partial class PlayerView
{
    private const int MaxPlaylistBytes = 1024 * 1024;
    private static readonly TimeSpan FailureShownFor = TimeSpan.FromSeconds(12);

    private NetworkBanner? _banner;
    private long _failedSince;
    private bool _failureDismissed;
    private int _addressGeneration;

    private async void OpenUrl()
    {
        var recent = AppSettings.Current.RecentAddresses;
        // A phone has no menu bar to hold the recent addresses: they are offered first.
        if (Shell is null && recent.Count > 0)
        {
            ShowRecentAddresses();
            return;
        }

        await AskForAddress();
    }

    private async Task AskForAddress()
    {
        if (await AskAsync("Open an address (a stream, internet radio, or a media file on a server)", "https://") is { } typed
            && typed.Trim() is { Length: > 8 } address)
        {
            await OpenAddress(address);
        }
    }

    private void ShowRecentAddresses()
    {
        if (_session?.CurrentView is not { } view)
            return;
        var recent = AppSettings.Current.RecentAddresses.ToList();
        ShowPopup(view, ["New address...", .. recent.Select(Short), "Clear this list"], -1, choice =>
        {
            Dispatcher.UIThread.Post(async () =>
            {
                if (choice == 0)
                    await AskForAddress();
                else if (choice > 0 && choice <= recent.Count)
                    await OpenAddress(recent[choice - 1]);
                else if (choice == recent.Count + 1)
                    ForgetAddresses();
            });
        });
    }

    private static string Short(string address) => address.Length <= 60 ? address : address[..28] + "..." + address[^28..];

    private void ForgetAddresses()
    {
        AppSettings.Current.RecentAddresses.Clear();
        AppSettings.Current.Save();
    }

    /// <summary>Media handed over from outside (command line, another app, a drop): a single address goes the way of Open URL.</summary>
    private void OpenGiven(IReadOnlyList<string> media)
    {
        if (media.Count == 1 && NetworkMedia.IsNetwork(media[0]))
            _ = OpenAddress(media[0]);
        else
            _player.OpenFiles(media);
    }

    private async Task OpenAddress(string address)
    {
        var generation = ++_addressGeneration;
        NetworkMedia.Remember(AppSettings.Current.RecentAddresses, address);
        AppSettings.Current.Save();
        if (NetworkMedia.LooksLikePlaylist(address))
        {
            ShowStatusBanner("Reading the playlist...");
            var entries = await ReadRemotePlaylist(address);
            ShowStatusBanner(null);
            if (generation != _addressGeneration)
                return; // another address was asked for meanwhile
            if (entries is not null)
            {
                _player.OpenFiles(entries);
                return;
            }
        }

        _player.OpenFiles([address]);
    }

    private static async Task<IReadOnlyList<string>?> ReadRemotePlaylist(string address)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Pardeh/1.0");
            using var response = await http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return null; // the player's own attempt reports why
            await using var stream = await response.Content.ReadAsStreamAsync();
            var bytes = new byte[MaxPlaylistBytes];
            var total = 0;
            int read;
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (total < bytes.Length && (read = await stream.ReadAsync(bytes.AsMemory(total), limit.Token)) > 0)
                total += read;
            return total >= bytes.Length ? null : Pardeh.Core.Library.PlaylistFormats.ParseRemote(address, Encoding.UTF8.GetString(bytes, 0, total));
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or InvalidOperationException or UriFormatException)
        {
            return null;
        }
    }

    private string? _bannerNote;

    private void ShowStatusBanner(string? note) => _bannerNote = note;

    /// <summary>File > Open Recent Address.</summary>
    private MenuItem RecentAddressMenu()
    {
        var recent = AppSettings.Current.RecentAddresses;
        var items = recent.Select(address => (object)Item(Short(address).Replace("_", "__"), () => _ = OpenAddress(address))).ToList();
        if (items.Count > 0)
        {
            items.Add(new Separator());
            items.Add(Item("Clear This List", ForgetAddresses));
        }

        return new MenuItem { Header = "Open Recent Address", ItemsSource = items, IsEnabled = items.Count > 0 };
    }

    private void ApplyNetworkSettings() =>
        _player.Network = AppSettings.Current.ToNetworkOptions();

    /// <summary>Per tick: the banner follows the player's network state.</summary>
    private void UpdateNetworkBanner()
    {
        var phase = _player.NetworkPhase;
        var message = _player.NetworkMessage;
        if (phase == NetworkPhase.None && _bannerNote is { } note)
            (phase, message) = (NetworkPhase.Buffering, note);

        if (phase == NetworkPhase.Failed)
        {
            if (_failedSince == 0)
                _failedSince = _clock.ElapsedMilliseconds;
        }
        else
        {
            _failedSince = 0;
            _failureDismissed = false;
        }

        var shown = phase != NetworkPhase.None && !_fullScreen
            && (phase != NetworkPhase.Failed || !_failureDismissed && _clock.ElapsedMilliseconds - _failedSince < FailureShownFor.TotalMilliseconds);
        if (!shown || _session?.CurrentView is not { } view)
        {
            if (_banner is not null)
                _banner.IsVisible = false;
            return;
        }

        if (_banner is null)
        {
            _banner = new NetworkBanner(() =>
            {
                if (_banner?.Phase == NetworkPhase.Failed)
                    _failureDismissed = true;
                else
                    _player.CancelConnect();
            });
        }

        if (!_overlay.Children.Contains(_banner))
            _overlay.Children.Add(_banner);
        _banner.Show(phase, message);
        var width = Math.Max(120, Math.Min(380, view.Root.Width - 20));
        _banner.Width = width;
        Canvas.SetLeft(_banner, Math.Round((view.Root.Width - width) / 2));
        Canvas.SetTop(_banner, Math.Min(46, Math.Max(4, view.Root.Height - 60)));
        _banner.IsVisible = true;
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Pardeh.Core.Downloads;
using Pardeh.UI.CleanupViews;

namespace Pardeh.UI.OnlineViews;

/// <summary>
/// "Download from a link": one address, typed or pasted by the user. Nothing else ever fills the box. The address itself
/// has to be an audio file, as for a plain download in a browser.
/// </summary>
internal sealed class PasteLinkPanel : UserControl
{
    internal const string NotKnown = "Pardeh can't tell whether this file may be downloaded. That is for you to be sure of.";

    private readonly CleanupInk _ink;
    private readonly DownloadsContext _context;
    private readonly Action _started;
    private readonly TextBox _address;
    private readonly StackPanel _answer = new() { Spacing = 6 };
    private CancellationTokenSource? _looking;

    public PasteLinkPanel(CleanupInk ink, DownloadsContext context, Action started)
    {
        _ink = ink;
        _context = context;
        _started = started;
        _address = new TextBox
        {
            PlaceholderText = "https://... the address of an audio file", MinHeight = ink.Narrow ? 42 : 24, VerticalContentAlignment = VerticalAlignment.Center,
            MaxLength = 4000,
        };
        _address.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            e.Handled = true;
            _ = Look();
        };
        // What was found belongs to the address it was asked about.
        _address.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty)
                return;
            _looking?.Cancel();
            _answer.Children.Clear();
        };

        var paste = ink.Button("Paste", async () =>
        {
            if (await context.ReadClipboard() is { Length: > 0 } text)
                _address.Text = text.Trim();
        });
        var look = ink.Button("Look at it", () => _ = Look());
        var buttons = new WrapPanel { Children = { paste, look } };
        paste.Margin = new Thickness(0, 0, 8, 0);
        Content = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                ink.Label("Download from a link", 14, bold: true),
                ink.Label("For an address that is itself an audio file. Pardeh doesn't search web pages for files, and takes nothing from video or streaming services.",
                    12, colour: ink.Sub),
                _address, buttons, _answer,
            },
        };
        DetachedFromVisualTree += (_, _) => _looking?.Cancel();
    }

    internal TextBox AddressBox => _address;

    private void Put(params Control[] controls)
    {
        _answer.Children.Clear();
        _answer.Children.AddRange(controls);
    }

    private static Control Row(params Button[] buttons)
    {
        var row = new WrapPanel();
        foreach (var button in buttons)
        {
            button.Margin = new Thickness(0, 0, 8, 4);
            row.Children.Add(button);
        }

        return row;
    }

    /// <summary>Asks the host in the link what it has there, and shows it. Nothing is saved yet.</summary>
    internal async Task Look()
    {
        var text = _address.Text?.Trim() ?? string.Empty;
        _looking?.Cancel();
        var mine = _looking = new CancellationTokenSource();
        if (text.Length == 0)
        {
            Put(_ink.Label("Paste an address first.", colour: _ink.Sub));
            return;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var address) || text.Any(char.IsWhiteSpace))
        {
            Put(_ink.Label("That is not one whole address. It starts with https://", colour: _ink.Bad));
            return;
        }

        if (_context.Manager.Engine.Rules.WhyRefused(address, DownloadKind.PastedLink) is { } why)
        {
            Put(_ink.Label(why, colour: _ink.Bad));
            return;
        }

        if (!await _context.Allow() || mine.IsCancellationRequested)
            return;
        Put(_ink.Label($"Asking {address.IdnHost}..."), new ProgressBar { IsIndeterminate = true, Height = 4, MinWidth = 0 }, Row(_ink.Button("Stop", () =>
        {
            mine.Cancel();
            Put();
        })));
        try
        {
            var offer = await _context.Manager.Engine.LookAsync(address, DownloadKind.PastedLink, mine.Token);
            if (!mine.IsCancellationRequested)
                Show(offer);
        }
        catch (OperationCanceledException)
        {
        }
        catch (DownloadIsPageException page)
        {
            if (mine.IsCancellationRequested)
                return;
            var open = Core.OnlineInfo.SafeLinks.Check(page.Page.AbsoluteUri);
            var shown = new List<Control> { _ink.Label(page.Message, colour: _ink.Bad) };
            if (open is not null)
                shown.Add(Row(_ink.Button("Open in browser", () => _ = _context.OpenPage(open, "The page at the address you pasted."))));
            Put([.. shown]);
        }
        catch (DownloadException ex)
        {
            if (!mine.IsCancellationRequested)
                Put(_ink.Label(ex.Message, colour: _ink.Bad));
        }
    }

    private void Show(DownloadOffer offer)
    {
        var inside = new StackPanel { Spacing = 4 };
        inside.Children.Add(_ink.Label(offer.FileName, bold: true));
        inside.Children.Add(_ink.Label("From: " + offer.Final.IdnHost));
        if (offer.HostChanged)
            inside.Children.Add(_ink.Label($"Your link is on {offer.Address.IdnHost}, which sends you on to {offer.Final.IdnHost}.", bold: true));
        inside.Children.Add(_ink.Label($"Type: {offer.TypeName}"));
        inside.Children.Add(_ink.Label("Size: " + DownloadText.Size(offer.Size)
            + (_ink.Narrow ? ". It counts on mobile data too." : string.Empty)));
        inside.Children.Add(_ink.Label(NotKnown, bold: true));
        var download = _ink.Button("Download", () =>
        {
            if (!_context.Manager.Start(offer.ToJob()))
            {
                Put(_ink.Label("Wait for the current download to end, or cancel it, then look at the address again.", colour: _ink.Bad));
                return;
            }

            _address.Text = string.Empty;
            _started();
        });
        inside.Children.Add(Row(download, _ink.Button("Not now", () => Put())));
        Put(_ink.Boxed(inside));
    }
}

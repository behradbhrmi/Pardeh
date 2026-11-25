using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Windows.Widgets.Providers;

namespace Pardeh.Widgets;

/// <summary>
/// The Pardeh widget of the Windows 11 Widgets board: cover, title, artist, a progress line and previous / play-pause / next / shuffle
/// (and a heart). It asks the running player over a local pipe (see WidgetBridge in Pardeh.App) every two seconds while a widget is
/// visible, and sends the card's buttons back the same way. When Pardeh is not running the card says so.
/// </summary>
internal sealed class WidgetProvider : IWidgetProvider
{
    private const string PipeName = "PardehWidgetPipe";

    public static readonly ManualResetEvent Exit = new(false);

    private static readonly object Gate = new();
    private static readonly Dictionary<string, bool> Widgets = []; // id -> visible
    private static Timer? _timer;

    public WidgetProvider()
    {
        // After a restart of this process the platform knows the pinned widgets; paint them again.
        try
        {
            foreach (var info in WidgetManager.GetDefault().GetWidgetInfos())
                lock (Gate)
                    Widgets[info.WidgetContext.Id] = false;
        }
        catch (Exception)
        {
            // No widget platform (a plain start): nothing to restore.
        }

        EnsureTimer();
    }

    public void CreateWidget(WidgetContext widgetContext)
    {
        lock (Gate)
            Widgets[widgetContext.Id] = true;
        EnsureTimer();
        Push(widgetContext.Id);
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        bool empty;
        lock (Gate)
        {
            Widgets.Remove(widgetId);
            empty = Widgets.Count == 0;
        }

        if (empty)
            Exit.Set();
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        // The card's buttons: the verb is the command (see Template).
        var verb = actionInvokedArgs.Verb;
        if (verb is "prev" or "next" or "playpause" or "shuffle" or "repeat" or "fav" or "back10" or "fwd10")
            Ask("cmd " + verb);
        Push(actionInvokedArgs.WidgetContext.Id);
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs) => Push(contextChangedArgs.WidgetContext.Id);

    public void Activate(WidgetContext widgetContext)
    {
        lock (Gate)
            Widgets[widgetContext.Id] = true;
        Push(widgetContext.Id);
    }

    public void Deactivate(string widgetId)
    {
        lock (Gate)
            if (Widgets.ContainsKey(widgetId))
                Widgets[widgetId] = false;
    }

    // ---- State from the player ----

    private static void EnsureTimer()
    {
        lock (Gate)
            _timer ??= new Timer(_ => Tick(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    private static void Tick()
    {
        string[] visible;
        lock (Gate)
            visible = Widgets.Where(w => w.Value).Select(w => w.Key).ToArray();
        if (visible.Length == 0)
            return;
        var data = BuildData(Ask("state"));
        foreach (var id in visible)
            Send(id, data);
    }

    private static void Push(string id) => Send(id, BuildData(Ask("state")));

    private static string? Ask(string line)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(400);
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
            using var reader = new StreamReader(pipe, Encoding.UTF8);
            return reader.ReadLine();
        }
        catch (Exception)
        {
            return null; // Pardeh is not running
        }
    }

    private static void Send(string id, string data)
    {
        try
        {
            WidgetManager.GetDefault().UpdateWidget(new WidgetUpdateRequestOptions(id) { Template = Template, Data = data, CustomState = string.Empty });
        }
        catch (Exception)
        {
            // The widget was removed meanwhile.
        }
    }

    private static string Time(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// <summary>The card's data from the player's JSON answer (or the "not running" / "nothing playing" texts).</summary>
    private static string BuildData(string? json)
    {
        string title = "Pardeh", artist = "Pardeh is not running", cover = Logo, time = string.Empty, bar = string.Empty;
        var playing = false;
        var shuffle = false;
        var repeat = false;
        var fav = false;
        if (json is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var r = doc.RootElement;
                if (r.GetProperty("active").GetBoolean())
                {
                    title = r.GetProperty("title").GetString() ?? string.Empty;
                    artist = r.GetProperty("artist").GetString() ?? string.Empty;
                    playing = r.GetProperty("playing").GetBoolean();
                    shuffle = r.GetProperty("shuffle").GetBoolean();
                    repeat = r.GetProperty("repeat").GetBoolean();
                    fav = r.GetProperty("favorite").GetBoolean();
                    if (r.GetProperty("cover").GetString() is { Length: > 0 } c)
                        cover = c;
                    var pos = r.GetProperty("position").GetDouble();
                    var dur = r.GetProperty("duration").GetDouble();
                    time = Time(pos) + " / " + Time(dur);
                    var filled = dur > 0 ? (int)Math.Round(Math.Clamp(pos / dur, 0, 1) * 20) : 0;
                    bar = new string('▰', filled) + new string('▱', 20 - filled);
                }
                else
                {
                    artist = "Nothing playing";
                }
            }
            catch (Exception)
            {
                artist = "Pardeh is not running";
            }
        }

        return JsonSerializer.Serialize(new
        {
            title, artist, cover, time, bar,
            playLabel = playing ? "Pause" : "Play",
            shuffleLabel = shuffle ? "Shuffle: on" : "Shuffle",
            repeatLabel = repeat ? "Repeat: on" : "Repeat",
            favLabel = fav ? "♥" : "♡",
        });
    }

    /// <summary>A 1x1 transparent picture: the card keeps its layout when there is no cover.</summary>
    private const string Logo = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNgYAAAAAMAASsJTYQAAAAASUVORK5CYII=";

    private const string Template = """
    {
      "type": "AdaptiveCard",
      "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
      "version": "1.5",
      "body": [
        {
          "type": "ColumnSet",
          "columns": [
            { "type": "Column", "width": "auto", "items": [ { "type": "Image", "url": "${cover}", "size": "Medium", "style": "default" } ] },
            { "type": "Column", "width": "stretch", "verticalContentAlignment": "Center", "items": [
              { "type": "TextBlock", "text": "${title}", "weight": "Bolder", "size": "Medium", "maxLines": 1, "wrap": false },
              { "type": "TextBlock", "text": "${artist}", "isSubtle": true, "maxLines": 1, "wrap": false, "spacing": "None" }
            ] }
          ]
        },
        { "type": "TextBlock", "text": "${bar}", "fontType": "Monospace", "size": "Small", "spacing": "Small", "$when": "${$host.widgetSize != 'small'}" },
        { "type": "TextBlock", "text": "${time}", "isSubtle": true, "size": "Small", "spacing": "None", "$when": "${$host.widgetSize != 'small'}" }
      ],
      "actions": [
        { "type": "Action.Execute", "title": "⏮", "verb": "prev" },
        { "type": "Action.Execute", "title": "${playLabel}", "verb": "playpause" },
        { "type": "Action.Execute", "title": "⏭", "verb": "next" },
        { "type": "Action.Execute", "title": "${shuffleLabel}", "verb": "shuffle", "$when": "${$host.widgetSize != 'small'}" },
        { "type": "Action.Execute", "title": "${favLabel}", "verb": "fav", "$when": "${$host.widgetSize == 'large'}" }
      ]
    }
    """;
}

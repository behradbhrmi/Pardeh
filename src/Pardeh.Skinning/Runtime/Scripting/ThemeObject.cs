namespace Pardeh.Skinning.Runtime.Scripting;

/// <summary>The <c>theme</c> object: preferences, strings, dialogs and view switching.</summary>
public sealed class ThemeObject : ScriptableObject
{
    public ThemeObject(SkinSession session)
    {
        var package = session.Package;
        var themeId = package.Id;

        Property("title", () => package.Title);
        Property("author", () => package.Theme.GetAttribute("author") ?? string.Empty);
        Property("copyright", () => package.Theme.GetAttribute("copyright") ?? string.Empty);
        Property("currentViewID", () => session.CurrentView?.Id ?? string.Empty, v => session.RequestViewSwitch(SkinValue.ToText(v)));

        Method("loadString", args =>
        {
            var reference = SkinValue.ToText(Arg(args, 0));
            return session.Resources?.GetString(reference) ?? WmpStrings.Load(reference);
        });
        Method("loadPreference", args => session.Preferences.Load(themeId, SkinValue.ToText(Arg(args, 0))) ?? string.Empty);
        Method("savePreference", args =>
        {
            var value = Arg(args, 1) switch
            {
                bool b => b ? "True" : "False",
                var other => SkinValue.ToText(other),
            };
            session.Preferences.Save(themeId, SkinValue.ToText(Arg(args, 0)), value);
            return null;
        });
        Method("openDialog", args =>
            session.RunModal(() => session.Host.ShowOpenFileDialog(SkinValue.ToText(Arg(args, 0)), SkinValue.ToText(Arg(args, 1)))) ?? string.Empty);
        Method("openView", args =>
        {
            session.RequestViewSwitch(SkinValue.ToText(Arg(args, 0)));
            return null;
        });
        Method("closeView", _ => null);
        Method("openViewRelative", args =>
        {
            session.RequestViewSwitch(SkinValue.ToText(Arg(args, 0)));
            return null;
        });
        Method("logString", args =>
        {
            session.Log(SkinValue.ToText(Arg(args, 0)));
            return null;
        });
        Method("messageBox", args =>
        {
            session.RunModal(() => { session.Host.ShowMessage(SkinValue.ToText(Arg(args, 0))); return true; });
            return 1.0;
        });
        Method("playSound", _ => null);
    }
}

/// <summary>Replacement text for the WMP resource strings skins ask for (res://wmploc.dll/RT_STRING/#id).</summary>
public static class WmpStrings
{
    private static readonly Dictionary<int, string> Strings = new()
    {
        [2063] = "Disconnected",
        [2070] = "Album: %s",
        [2071] = "Title: %s",
        [2072] = "Artist: %s",
        [2073] = "Genre: %s",
        [2076] = "Copyright: %s",
        [2081] = "Network congestion",
        [2099] = "Buffering: %s%",
    };

    /// <summary>Resolves "res://…/#id" to text; any other string is returned unchanged.</summary>
    public static string Load(string reference)
    {
        if (!reference.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            return reference;
        var hash = reference.LastIndexOf('#');
        return hash >= 0 && int.TryParse(reference.AsSpan(hash + 1), out var id) && Strings.TryGetValue(id, out var text)
            ? text
            : string.Empty;
    }
}

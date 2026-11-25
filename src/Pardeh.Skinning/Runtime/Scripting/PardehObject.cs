namespace Pardeh.Skinning.Runtime.Scripting;

/// <summary>
/// The <c>pardeh</c> object: app-level commands for Pardeh's own skins (full mode). Classic skins never see
/// anything they didn't have in the original player; this is an extension namespace.
/// </summary>
public sealed class PardehObject : ScriptableObject
{
    public PardehObject(SkinSession session)
    {
        Property("version", () => typeof(PardehObject).Assembly.GetName().Version?.ToString() ?? "0");
        Property("isFullMode", () => session.Package.Theme.GetAttribute("pardehMode") == "full");
        Method("command", args =>
        {
            var name = SkinValue.ToText(Arg(args, 0));
            var rest = args.Skip(1).ToArray();
            return session.RunModal(() => session.Host.RunCommand(name, rest));
        });
    }
}

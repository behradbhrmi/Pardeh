using Android.Content;

namespace Pardeh.Android;

/// <summary>The look of one placed widget; kept per widget id in SharedPreferences.</summary>
internal sealed class WidgetConfig
{
    public const int ThemeNavy = 0, ThemeAccent = 1, ThemeCover = 2, ThemeLight = 3, ThemeClear = 4;
    public static readonly string[] ThemeNames = ["Navy", "Skin colour", "Blurred cover", "Light", "Transparent"];
    public static readonly string[] TextNames = ["Small", "Medium", "Large"];

    public int Theme { get; set; } = ThemeNavy;

    /// <summary>Background opacity, 20..100 percent.</summary>
    public int Opacity { get; set; } = 100;

    /// <summary>0 small, 1 medium, 2 large.</summary>
    public int TextSize { get; set; } = 1;

    public bool Cover { get; set; } = true;
    public bool Seekbar { get; set; } = true;
    public bool Skip { get; set; } = true;
    public bool Shuffle { get; set; } = true;
    public bool Repeat { get; set; } = true;
    public bool Favorite { get; set; } = true;

    private static ISharedPreferences Prefs(Context c) => c.GetSharedPreferences("pardeh_widgets", FileCreationMode.Private)!;

    public static WidgetConfig Load(Context c, int id)
    {
        var p = Prefs(c);
        string k(string n) => $"{id}.{n}";
        return new WidgetConfig
        {
            Theme = p.GetInt(k("theme"), ThemeNavy), Opacity = p.GetInt(k("opacity"), 100), TextSize = p.GetInt(k("text"), 1),
            Cover = p.GetBoolean(k("cover"), true), Seekbar = p.GetBoolean(k("seek"), true), Skip = p.GetBoolean(k("skip"), true),
            Shuffle = p.GetBoolean(k("shuffle"), true), Repeat = p.GetBoolean(k("repeat"), true), Favorite = p.GetBoolean(k("fav"), true),
        };
    }

    public void Save(Context c, int id)
    {
        string k(string n) => $"{id}.{n}";
        var edit = Prefs(c).Edit()!;
        edit.PutInt(k("theme"), Theme);
        edit.PutInt(k("opacity"), Opacity);
        edit.PutInt(k("text"), TextSize);
        edit.PutBoolean(k("cover"), Cover);
        edit.PutBoolean(k("seek"), Seekbar);
        edit.PutBoolean(k("skip"), Skip);
        edit.PutBoolean(k("shuffle"), Shuffle);
        edit.PutBoolean(k("repeat"), Repeat);
        edit.PutBoolean(k("fav"), Favorite);
        edit.Apply();
    }

    public static void Delete(Context c, int id)
    {
        var e = Prefs(c).Edit()!;
        foreach (var n in new[] { "theme", "opacity", "text", "cover", "seek", "skip", "shuffle", "repeat", "fav" })
            e.Remove($"{id}.{n}");
        e.Apply();
    }
}

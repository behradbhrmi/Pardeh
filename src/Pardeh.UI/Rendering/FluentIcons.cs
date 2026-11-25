// The icon shapes in FluentIcons.Data.cs come from Microsoft's "Fluent UI System Icons"
// (https://github.com/microsoft/fluentui-system-icons, npm @fluentui/svg-icons), 24 px grid, regular and filled variants.
//
// MIT License
// Copyright (c) 2020 Microsoft Corporation
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy,
// modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice
// shall be included in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY
// OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR
// THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace Pardeh.UI.Rendering;

/// <summary>
/// The icons of the modern phone looks (Fluent, Ember, Horizon): Fluent UI System Icons drawn as filled geometry (the "regular"
/// icons are outlines expressed as fills), in the ink of the row; the "filled" variant marks a selected / active state.
/// The pictorial looks (Glass, Royal) never use these. Path strings are literals (no number formatting is involved).
/// </summary>
internal static partial class FluentIcons
{
    private static readonly Dictionary<(string, bool), Geometry> Cache = [];

    /// <summary>The line glyphs the mobile pages were drawn with before, mapped to the Fluent icon that replaces them.</summary>
    private static readonly Dictionary<string, string> LegacyGlyphs = new(StringComparer.Ordinal)
    {
        // Settings home.
        ["M 3,17 L 3,13 L 13,3 L 17,7 L 7,17 Z M 11,5 L 15,9"] = "paint_brush",
        ["M 3,3 L 9,3 L 9,9 L 3,9 Z M 11,3 L 17,3 L 17,9 L 11,9 Z M 3,11 L 9,11 L 9,17 L 3,17 Z M 11,11 L 17,11 L 17,17 L 11,17 Z"] = "apps",
        ["M 4,4 L 16,4 L 16,16 L 4,16 Z M 8.5,7.5 L 12.5,10 L 8.5,12.5 Z"] = "play_circle",
        ["M 10,3 A 7,7 0 1 0 10,17 Q 12,17 12,15 Q 12,13 14,13 L 15,13 A 7,7 0 0 0 10,3 Z"] = "color",
        ["M 3.5,8 L 6.5,8 L 10,5 L 10,15 L 6.5,12 L 3.5,12 Z M 13,8 Q 14.5,10 13,12 M 15,6.5 Q 17.5,10 15,13.5"] = "speaker_2",
        ["M 3,5 L 13,5 L 13,15 L 3,15 Z M 13,9 L 17,6 L 17,14 L 13,11"] = "video",
        ["M 3,5 L 8,5 L 9.5,7 L 17,7 L 17,15 L 3,15 Z"] = "folder",
        ["M 10,3 A 7,7 0 1 0 10.01,3 Z M 10,9 L 10,14 M 10,6 L 10,6.4"] = "info",

        // Library.
        ["M 8,15 L 8,5 L 15,3 L 15,13 M 5.5,15 a 2.5,2 0 1,0 5,0 a 2.5,2 0 1,0 -5,0 M 12.5,13 a 2.5,2 0 1,0 5,0 a 2.5,2 0 1,0 -5,0"] = "music_note_2",
        ["M 3,4 L 17,4 L 17,16 L 3,16 Z M 3,13 L 8,9 L 12,13 L 14,11 L 17,14"] = "image",
        ["M 10,7 m -3,0 a 3,3 0 1,0 6,0 a 3,3 0 1,0 -6,0 M 4,17 Q 4,12 10,12 Q 16,12 16,17"] = "person",
        ["M 10,10 m -7,0 a 7,7 0 1,0 14,0 a 7,7 0 1,0 -14,0 M 10,10 m -2,0 a 2,2 0 1,0 4,0 a 2,2 0 1,0 -4,0"] = "album",
        ["M 4,6 L 16,6 M 4,10 L 16,10 M 4,14 L 11,14"] = "tag",
        ["M 3,6 L 13,6 M 3,10 L 13,10 M 3,14 L 9,14 M 12,12 L 17,14.5 L 12,17 Z"] = "text_bullet_list_ltr",
        ["M 3,6 L 13,6 M 3,10 L 13,10 M 3,14 L 9,14"] = "text_bullet_list_ltr",
        ["M 10,3 L 12,8 L 17,8 L 13,11.5 L 14.5,17 L 10,13.5 L 5.5,17 L 7,11.5 L 3,8 L 8,8 Z"] = "star",
        ["M 10,4 L 10,10 L 14,12 M 10,10 m -7,0 a 7,7 0 1,0 14,0 a 7,7 0 1,0 -14,0"] = "add_circle",
        ["M 7,4 L 17,10 L 7,16 Z"] = "history",
        ["M 10,10 m -1.2,0 a 1.2,1.2 0 1,0 2.4,0 a 1.2,1.2 0 1,0 -2.4,0 M 6.5,6.5 Q 3.5,10 6.5,13.5 M 13.5,6.5 Q 16.5,10 13.5,13.5"] = "live",
        ["M 3,5 L 8,5 L 9.5,7 L 17,7 L 17,15 L 3,15 Z M 10,9 L 10,13 M 8,11 L 12,11"] = "folder_add",
        ["M 3,5 L 3.6,5 M 7,5 L 17,5 M 3,10 L 3.6,10 M 7,10 L 17,10 M 3,15 L 3.6,15 M 7,15 L 17,15"] = "text_bullet_list_ltr",
        ["M 3,3 L 8,3 L 8,8 L 3,8 Z M 12,3 L 17,3 L 17,8 L 12,8 Z M 3,12 L 8,12 L 8,17 L 3,17 Z M 12,12 L 17,12 L 17,17 L 12,17 Z"] = "grid",

        // Header and row chrome.
        ["M 15,4 L 7,12 L 15,20"] = "arrow_left",
        ["M 9,9 m -5.5,0 a 5.5,5.5 0 1,0 11,0 a 5.5,5.5 0 1,0 -11,0 M 13,13 L 18,18"] = "search",
        ["M 7,4 L 13,10 L 7,16"] = "chevron_right",
        ["M 4,10 L 8,14 L 16,5"] = "checkmark",
        ["M 5,5 L 15,15 M 15,5 L 5,15"] = "dismiss",
        ["M 10,4 m -1.4,0 a 1.4,1.4 0 1,0 2.8,0 a 1.4,1.4 0 1,0 -2.8,0 M 10,10 m -1.4,0 a 1.4,1.4 0 1,0 2.8,0 a 1.4,1.4 0 1,0 -2.8,0 M 10,16 m -1.4,0 a 1.4,1.4 0 1,0 2.8,0 a 1.4,1.4 0 1,0 -2.8,0"] = "more_vertical",
        ["M 7,4 L 17,12 L 7,20 Z"] = "play",
        ["M 6,4 L 10,4 L 10,20 L 6,20 Z M 14,4 L 18,4 L 18,20 L 14,20 Z"] = "pause",

        // Keys the library passes for its group pages ("@person", "@tag" ...).
        ["@phone"] = "phone", ["@note"] = "music_note_2", ["@person"] = "person", ["@tag"] = "tag", ["@albums"] = "album", ["@search"] = "search",
        ["@player"] = "play", ["@playnext"] = "text_bullet_list_add", ["@queue"] = "add", ["@list"] = "text_bullet_list_ltr", ["@pencil"] = "edit", ["@remove"] = "delete",
        ["@heart"] = "heart", ["@heartoff"] = "heart", ["@sort"] = "arrow_sort", ["@filter"] = "filter", ["@lyrics"] = "mic",
    };

    /// <summary>The Fluent icon name that replaces a legacy line glyph or "@key" (null when none is mapped).</summary>
    public static string? ForGlyph(string glyphOrKey) => LegacyGlyphs.GetValueOrDefault(glyphOrKey);

    public static bool Has(string name) => RegularData.ContainsKey(name) || LibraryRegular.ContainsKey(name) || LyricsRegular.ContainsKey(name);

    private static string? DataOf(string name, bool filled) =>
        (filled ? FilledData.GetValueOrDefault(name) ?? LibraryFilled.GetValueOrDefault(name) ?? LyricsFilled.GetValueOrDefault(name) : null) ?? RegularData.GetValueOrDefault(name) ?? LibraryRegular.GetValueOrDefault(name) ?? LyricsRegular.GetValueOrDefault(name);

    /// <summary>The icon's geometry on the 24 px grid; the filled variant when asked and present, else the regular one.</summary>
    public static Geometry Geo(string name, bool filled = false)
    {
        var useFilled = filled && (FilledData.ContainsKey(name) || LibraryFilled.ContainsKey(name) || LyricsFilled.ContainsKey(name));
        if (!Cache.TryGetValue((name, useFilled), out var geometry))
            Cache[(name, useFilled)] = geometry = Geometry.Parse("F1 " + (DataOf(name, useFilled) ?? throw new KeyNotFoundException(name))); // non-zero fill, as in the SVGs
        return geometry;
    }

    /// <summary>An icon control of <paramref name="size"/> dp in a single ink colour.</summary>
    public static Path Create(string name, Color ink, double size = 24, bool filled = false) => new()
    {
        // Not stretched to the shape's bounds: every icon keeps its place on the 24 px grid, scaled from it to the requested size.
        Data = Geo(name, filled), Fill = new SolidColorBrush(ink), Stretch = Stretch.None, Width = size, Height = size,
        RenderTransform = new ScaleTransform(size / 24, size / 24), RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
    };
}

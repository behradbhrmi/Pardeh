namespace Pardeh.Skinning.Runtime;

/// <summary>One visualization: the family ("Battery") and the preset within it ("Randomization").</summary>
public sealed record Visualization(string Type, string Preset)
{
    public string Title => Type + " : " + Preset;
}

/// <summary>
/// The visualization families the classic player shipped, with Pardeh's own look-alike drawing for each preset
/// (src/Pardeh.App/Rendering/VisualizationRenderer.cs). Order is the player's: families alphabetically.
/// </summary>
public static class VisualizationCatalog
{
    public static readonly IReadOnlyList<(string Type, string[] Presets)> Types =
    [
        ("Alchemy", ["Random"]),
        ("Ambience", ["Water", "Swirl", "Warp"]),
        ("Bars and Waves", ["Bars", "Ocean Mist", "Fire Storm", "Scope"]),
        ("Battery", ["Randomization", "Event Horizon", "Cottonstar"]),
        ("Musical Colors", ["Blend"]),
        ("Particle", ["Fountain"]),
        ("Plenoptic", ["Rings"]),
        ("Spikes", ["Bursts"]),
    ];

    public static readonly IReadOnlyList<Visualization> All =
        Types.SelectMany(t => t.Presets.Select(p => new Visualization(t.Type, p))).ToList();

    /// <summary>What the player shows first ("Ambience : Water", as in 2003).</summary>
    public static readonly int Default = IndexOf("Ambience", 0);

    /// <summary>Index into <see cref="All"/>; an unknown family gives the default.</summary>
    public static int IndexOf(string? type, int preset)
    {
        var first = -1;
        for (var i = 0; i < All.Count; i++)
        {
            if (!string.Equals(All[i].Type, type, StringComparison.OrdinalIgnoreCase))
                continue;
            if (first < 0)
                first = i;
            if (i - first == preset)
                return i;
        }

        return first >= 0 ? first : Default;
    }

    /// <summary>The preset's number within its family.</summary>
    public static int PresetOf(int index) => All.Take(index).Count(v => v.Type == All[index].Type);

    public static int Wrap(int index) => (index % All.Count + All.Count) % All.Count;

    /// <summary>The first preset of the next (+1) or previous (-1) family.</summary>
    public static int StepType(int index, int direction)
    {
        var types = Types.Select(t => t.Type).ToList();
        var current = types.IndexOf(All[Wrap(index)].Type);
        var next = types[(current + direction + types.Count) % types.Count];
        return IndexOf(next, 0);
    }
}

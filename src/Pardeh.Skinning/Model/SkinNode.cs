namespace Pardeh.Skinning.Model;

/// <summary>One element of a skin definition (e.g. a WMS &lt;BUTTON&gt;), with attributes in document order.</summary>
public sealed class SkinNode
{
    private readonly List<KeyValuePair<string, string>> _attributes = [];

    public SkinNode(string tag) => Tag = tag.ToUpperInvariant();

    /// <summary>Upper-case tag name.</summary>
    public string Tag { get; }

    public IReadOnlyList<KeyValuePair<string, string>> Attributes => _attributes;

    public List<SkinNode> Children { get; } = [];

    public SkinNode? Parent { get; private set; }

    /// <summary>1-based source line, for error messages.</summary>
    public int Line { get; init; }

    public string? Id => GetAttribute("id");

    public void AddChild(SkinNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    /// <summary>Adds an attribute; a repeated name replaces the earlier value (WMP behaviour).</summary>
    public void SetAttribute(string name, string value)
    {
        for (var i = 0; i < _attributes.Count; i++)
        {
            if (string.Equals(_attributes[i].Key, name, StringComparison.OrdinalIgnoreCase))
            {
                _attributes[i] = new(_attributes[i].Key, value);
                return;
            }
        }

        _attributes.Add(new(name, value));
    }

    public string? GetAttribute(string name)
    {
        foreach (var pair in _attributes)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }

        return null;
    }

    public IEnumerable<SkinNode> Descendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var d in child.Descendants())
                yield return d;
        }
    }

    public override string ToString() => Id is null ? $"<{Tag}>" : $"<{Tag} id=\"{Id}\">";
}

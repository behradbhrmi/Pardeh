using System.Globalization;
using System.Text;
using Pardeh.Skinning.Model;

namespace Pardeh.Skinning.Wmp;

/// <summary>
/// Reads compiled Windows Media Player skin definitions (".wsz" resources in the player's resource DLL) back
/// into a <see cref="SkinNode"/> tree, so Pardeh runs them like ordinary .wms skins.
/// </summary>
/// <remarks>
/// Format (reverse engineered): a depth-first list of elements.
/// Element: [u16 nodeSize (whole subtree; 0 = to the parent's end)][u16 blockSize (header+attributes; 0 = unknown)]
/// [u16 tag][u8 attributeCount][UTF-16 tag name, empty for built-in elements][16-byte CLSID if tag &amp; 0x80].
/// Attribute: [u16 length (0 = to the end)][u8 type][name][value]. The name is an int32 DISPID when
/// (type &amp; 0x0F) != 0, else UTF-16. Value by the low nibble: 1 → u16 bool, 4 → int32, 8 or D (numbers kept as text) → UTF-16;
/// type &amp; 0x10 → int32 string-resource id, else UTF-16. type &amp; 0x80 → "JScript:" expression,
/// type &amp; 0x40 → "wmpprop:" binding, type &amp; 0x20 → the text already carries its prefix.
/// </remarks>
public sealed class WszDecoder
{
    // Element classes by CLSID (from the player's type library) and built-in element kinds by tag low byte.
    private static readonly Dictionary<Guid, string> ClassTags = new()
    {
        [new Guid("87291b51-0c8e-11d3-bb2a-00a0c93ca73a")] = "BUTTON",
        [new Guid("ae3b6831-25a9-11d3-bd41-00c04f6ea5ae")] = "BUTTONGROUP",
        [new Guid("09aeff11-69ef-11d3-bd4d-00c04f6ea5ae")] = "BUTTONELEMENT",
        [new Guid("f2bf2c90-405f-11d3-bb39-00a0c93ca73a")] = "SLIDER",
        [new Guid("95f45aa3-ed0a-11d2-ba67-0000f80855e6")] = "CUSTOMSLIDER",
        [new Guid("ddda102e-0e17-11d3-a2e2-00c04f79f88e")] = "TEXT",
        [new Guid("5f9cfd93-8cad-11d3-9a7e-00c04f8efb70")] = "PLAYLIST",
        [new Guid("bab3768b-8883-4aec-9f9b-e14c947913ef")] = "POPUP",
        [new Guid("6b28f900-8d64-4b80-9963-cc52ddd1fbb4")] = "POPUP",
        [new Guid("61cecf11-fc3a-11d2-a1cd-005004602752")] = "VIDEO",
        [new Guid("47dea830-d619-4154-b8d8-6b74845d6a2d")] = "EFFECTS",
        [new Guid("93eb32f5-87b1-45ad-acc6-0f2483db83bb")] = "EQUALIZERSETTINGS",
        [new Guid("ae7bfafe-dcc8-4a73-92c8-cc300ca88859")] = "VIDEOSETTINGS",
        [new Guid("fc1880cf-83b9-43a7-a066-c44ce8c82583")] = "LISTBOX",
        [new Guid("6342fced-25ea-4033-bddb-d049a14382d3")] = "EDITBOX",
        [new Guid("d9de732a-aee9-4503-9d11-5605589977a8")] = "LIBRARYTREE",
        [new Guid("395bf287-6477-495f-8427-2c09a23c3248")] = "TASKCENTER",
        [new Guid("aa1ac37b-49a8-4b41-af69-b0176c5ffc33")] = "PLUGINHOST",
    };

    private static readonly Dictionary<int, string> BuiltInTags = new()
    {
        [0x0B] = "THEME",
        [0x0C] = "VIEW",
        [0x0D] = "SUBVIEW",
    };

    private readonly byte[] _data;
    private readonly Func<int, string?> _resolveString;
    private readonly StringBuilder? _trace;

    public WszDecoder(byte[] data, Func<int, string?>? resolveString = null, StringBuilder? trace = null)
    {
        _data = data;
        _resolveString = resolveString ?? (_ => null);
        _trace = trace;
    }

    /// <summary>Decodes the whole file into its THEME node.</summary>
    public SkinNode Decode()
    {
        var tree = ReadTree();
        var root = tree.Count == 1 ? tree[0] : new RawNode(new RawElement(0, 0, 0, 0x0B, "THEME", null, [], 0), tree);
        return ToSkinNode(root);
    }

    private SkinNode ToSkinNode(RawNode raw)
    {
        var node = new SkinNode(raw.Element.TagName);
        foreach (var attribute in raw.Element.Attributes)
            node.SetAttribute(attribute.Name, FormatValue(attribute));
        foreach (var child in raw.Children)
            node.AddChild(ToSkinNode(child));
        return node;
    }

    private string FormatValue(RawAttribute attribute)
    {
        var text = attribute.Value switch
        {
            bool b => b ? "true" : "false",
            int i when (attribute.Type & 0x10) != 0 => _resolveString(i) ?? $"res://wmploc/RT_STRING/#{i}",
            int i => i.ToString(CultureInfo.InvariantCulture),
            _ => attribute.Value.ToString() ?? string.Empty,
        };

        if ((attribute.Type & 0x80) != 0)
            return "JScriptLive:" + text; // compiled skins re-evaluate their expressions (see SkinView)
        if ((attribute.Type & 0x40) != 0)
            return "wmpprop:" + text;
        return text;
    }

    /// <summary>Raw elements in file order (for analysis and tests).</summary>
    public List<RawElement> ReadElements()
    {
        var elements = new List<RawElement>();
        var offset = 0;
        while (offset + 7 <= _data.Length)
        {
            var element = ReadElement(offset);
            elements.Add(element);
            _trace?.AppendLine(element.ToString());
            if (element.End <= offset)
                break;
            offset = element.End;
        }

        return elements;
    }

    /// <summary>Rebuilds the element tree: an element's subtree spans nodeSize bytes (0 = to its parent's end).</summary>
    public List<RawNode> ReadTree()
    {
        var elements = ReadElements();
        var index = 0;
        return ReadChildren(elements, ref index, _data.Length);
    }

    private static List<RawNode> ReadChildren(List<RawElement> elements, ref int index, int limit)
    {
        var nodes = new List<RawNode>();
        while (index < elements.Count && elements[index].Offset < limit)
        {
            var element = elements[index++];
            var end = element.NodeSize != 0 ? element.Offset + element.NodeSize : limit;
            var children = ReadChildren(elements, ref index, Math.Min(end, limit));
            nodes.Add(new RawNode(element, children));
        }

        return nodes;
    }

    private RawElement ReadElement(int offset)
    {
        var nodeSize = U16(offset);
        var blockSize = U16(offset + 2);
        var tag = U16(offset + 4);
        var count = _data[offset + 6];
        var (name, p) = ReadString(offset + 7);
        Guid? clsid = null;
        if ((tag & 0x80) != 0 && p + 16 <= _data.Length)
        {
            clsid = new Guid(_data.AsSpan(p, 16));
            p += 16;
        }

        var tagName = name.Length > 0 ? name.ToUpperInvariant()
            : clsid is { } c && ClassTags.TryGetValue(c, out var known) ? known
            : BuiltInTags.TryGetValue(tag & 0x7F, out var builtIn) ? builtIn
            : $"UNKNOWN{tag:X}_{clsid}";

        var size = blockSize != 0 ? blockSize : nodeSize;
        var end = offset + size;
        var attributes = new List<RawAttribute>();
        for (var i = 0; i < count && p + 3 <= _data.Length; i++)
        {
            var length = U16(p);
            var type = _data[p + 2];
            var q = p + 3;
            var recordEnd = length == 0 ? end : p + length;

            string attributeName;
            int? dispId = null;
            if ((type & 0x0F) != 0)
            {
                dispId = BitConverter.ToInt32(_data, q);
                q += 4;
                attributeName = WmpDispIds.Names.TryGetValue(dispId.Value, out var known2) ? known2 : "dispid" + dispId.Value;
            }
            else
            {
                (attributeName, q) = ReadString(q);
            }

            object value;
            int valueEnd;
            if ((type & 0x10) == 0 && (type & 0x0F) == 0x01)
            {
                value = U16(q) != 0;
                valueEnd = q + 2;
            }
            else if ((type & 0x10) != 0 || (type & 0x0F) == 0x04)
            {
                value = BitConverter.ToInt32(_data, q);
                valueEnd = q + 4;
            }
            else
            {
                (var text, valueEnd) = ReadString(q);
                value = text;
            }

            // Length 0 = "to the end of the element"; when the element size isn't recorded either,
            // the value itself tells where the record ends.
            if (length == 0 && size == 0)
                recordEnd = valueEnd;

            attributes.Add(new RawAttribute(attributeName, dispId, type, value));
            p = recordEnd;
        }

        return new RawElement(offset, nodeSize, blockSize, tag, tagName, clsid, attributes, p);
    }

    private ushort U16(int offset) => offset + 1 < _data.Length ? BitConverter.ToUInt16(_data, offset) : (ushort)0;

    private (string Text, int Next) ReadString(int offset)
    {
        var start = offset;
        while (offset + 1 < _data.Length && (_data[offset] != 0 || _data[offset + 1] != 0))
            offset += 2;
        return (Encoding.Unicode.GetString(_data, start, offset - start), offset + 2);
    }

    public sealed record RawAttribute(string Name, int? DispId, byte Type, object Value)
    {
        public override string ToString() => $"[{Type:X2}]{Name}={Value}";
    }

    public sealed record RawElement(int Offset, int NodeSize, int BlockSize, int Tag, string TagName, Guid? Clsid, List<RawAttribute> Attributes, int End)
    {
        public override string ToString() =>
            $"@{Offset:X4} node={NodeSize} block={BlockSize} tag=0x{Tag:X} {TagName}: {string.Join(" ", Attributes)}";
    }

    public sealed record RawNode(RawElement Element, List<RawNode> Children);
}

using System.Globalization;
using System.Text;

namespace Pardeh.Skinning.Model;

/// <summary>
/// Forgiving XML-ish parser for skin definitions. Old skin engines accepted things strict XML rejects
/// (bare '&amp;&amp;' in script attributes, unclosed tags at end of file, duplicate attributes), so we do too.
/// </summary>
public static class LenientXmlParser
{
    public static SkinNode Parse(string text)
    {
        var root = new SkinNode("#document");
        var stack = new Stack<SkinNode>();
        stack.Push(root);

        var i = 0;
        var line = 1;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\n')
                line++;

            if (c != '<')
            {
                i++;
                continue;
            }

            if (Matches(text, i, "<!--"))
            {
                var end = text.IndexOf("-->", i + 4, StringComparison.Ordinal);
                line += CountLines(text, i, end < 0 ? text.Length : end);
                i = end < 0 ? text.Length : end + 3;
                continue;
            }

            if (Matches(text, i, "<?") || Matches(text, i, "<!"))
            {
                var end = text.IndexOf('>', i);
                i = end < 0 ? text.Length : end + 1;
                continue;
            }

            if (Matches(text, i, "</"))
            {
                var end = text.IndexOf('>', i);
                var name = text[(i + 2)..(end < 0 ? text.Length : end)].Trim().ToUpperInvariant();
                // Pop up to the matching tag; ignore stray closing tags.
                if (stack.Any(n => n.Tag == name))
                {
                    while (stack.Count > 1 && stack.Peek().Tag != name)
                        stack.Pop();
                    if (stack.Count > 1)
                        stack.Pop();
                }

                i = end < 0 ? text.Length : end + 1;
                continue;
            }

            // Start tag.
            i++;
            var nameStart = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '>' && text[i] != '/')
                i++;
            var tag = text[nameStart..i];
            if (tag.Length == 0)
                continue;

            var node = new SkinNode(tag) { Line = line };
            var selfClosing = false;

            while (i < text.Length)
            {
                // Skip whitespace.
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                {
                    if (text[i] == '\n')
                        line++;
                    i++;
                }

                if (i >= text.Length)
                    break;
                if (text[i] == '>')
                {
                    i++;
                    break;
                }

                if (text[i] == '/')
                {
                    selfClosing = true;
                    i++;
                    continue;
                }

                var attrStart = i;
                while (i < text.Length && text[i] != '=' && !char.IsWhiteSpace(text[i]) && text[i] != '>' && text[i] != '/')
                    i++;
                var attrName = text[attrStart..i];
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                {
                    if (text[i] == '\n')
                        line++;
                    i++;
                }

                string value;
                if (i < text.Length && text[i] == '=')
                {
                    i++;
                    while (i < text.Length && char.IsWhiteSpace(text[i]))
                        i++;
                    if (i < text.Length && (text[i] == '"' || text[i] == '\''))
                    {
                        var quote = text[i];
                        var end = text.IndexOf(quote, i + 1);
                        if (end < 0)
                            end = text.Length;
                        var raw = text[(i + 1)..end];
                        line += CountLines(text, i, end);
                        value = DecodeEntities(raw);
                        i = Math.Min(end + 1, text.Length);
                    }
                    else
                    {
                        var vStart = i;
                        while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '>')
                            i++;
                        value = DecodeEntities(text[vStart..i]);
                    }
                }
                else
                {
                    value = "true";
                }

                if (attrName.Length > 0)
                    node.SetAttribute(attrName, value);
            }

            stack.Peek().AddChild(node);
            if (!selfClosing)
                stack.Push(node);
        }

        return root;
    }

    private static bool Matches(string text, int index, string token) =>
        string.CompareOrdinal(text, index, token, 0, token.Length) == 0;

    private static int CountLines(string text, int start, int end)
    {
        var count = 0;
        for (var k = start; k < end && k < text.Length; k++)
        {
            if (text[k] == '\n')
                count++;
        }

        return count;
    }

    /// <summary>Decodes standard entities; a bare '&amp;' that isn't an entity stays as-is.</summary>
    public static string DecodeEntities(string raw)
    {
        if (raw.IndexOf('&') < 0)
            return raw;

        var sb = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '&')
            {
                sb.Append(raw[i]);
                continue;
            }

            var semi = raw.IndexOf(';', i + 1);
            if (semi > i + 1 && semi - i <= 10)
            {
                var entity = raw[(i + 1)..semi];
                string? decoded = entity switch
                {
                    "amp" => "&",
                    "lt" => "<",
                    "gt" => ">",
                    "quot" => "\"",
                    "apos" => "'",
                    _ when entity.StartsWith("#x", StringComparison.OrdinalIgnoreCase)
                           && int.TryParse(entity[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) => char.ConvertFromUtf32(hex),
                    _ when entity.StartsWith('#')
                           && int.TryParse(entity[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec) => char.ConvertFromUtf32(dec),
                    _ => null,
                };

                if (decoded is not null)
                {
                    sb.Append(decoded);
                    i = semi;
                    continue;
                }
            }

            sb.Append('&');
        }

        return sb.ToString();
    }
}

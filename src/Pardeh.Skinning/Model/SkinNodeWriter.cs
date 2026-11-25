using System.Text;

namespace Pardeh.Skinning.Model;

/// <summary>Writes a skin tree as WMS-style XML (for inspection and for saving decoded skins).</summary>
public static class SkinNodeWriter
{
    public static string ToXml(SkinNode node)
    {
        var sb = new StringBuilder();
        Write(sb, node, 0);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, SkinNode node, int depth)
    {
        var indent = new string(' ', depth * 2);
        sb.Append(indent).Append('<').Append(node.Tag);
        foreach (var (name, value) in node.Attributes)
            sb.Append('\n').Append(indent).Append("    ").Append(name).Append("=\"").Append(Escape(value)).Append('"');

        if (node.Children.Count == 0)
        {
            sb.Append("/>\n");
            return;
        }

        sb.Append(">\n");
        foreach (var child in node.Children)
            Write(sb, child, depth + 1);
        sb.Append(indent).Append("</").Append(node.Tag).Append(">\n");
    }

    private static string Escape(string value) => value
        .Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\n", "&#10;").Replace("\r", "&#13;");
}

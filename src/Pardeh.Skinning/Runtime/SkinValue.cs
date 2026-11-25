using System.Globalization;
using System.Text.RegularExpressions;

namespace Pardeh.Skinning.Runtime;

/// <summary>How an attribute value should be interpreted.</summary>
public enum AttributeValueKind
{
    Literal,
    /// <summary>"JScript:expr" — evaluated once when the view loads.</summary>
    Script,
    /// <summary>"wmpprop:expr" — live binding, re-evaluated continuously.</summary>
    Binding,

    /// <summary>"JScriptLive:expr" (compiled classic skins): evaluated at load, then kept up to date like a binding.</summary>
    LiveScript,
}

/// <summary>Conversions between skin attribute strings, script values and typed values.</summary>
public static partial class SkinValue
{
    [GeneratedRegex(@"^\s*(?:[a-z]*script|jsript|jscipt)\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptPrefix();

    /// <summary>Removes a "JScript:" style prefix from event handler code.</summary>
    public static string StripScriptPrefix(string code)
    {
        var match = ScriptPrefix().Match(code);
        return match.Success ? code[match.Length..] : code;
    }

    /// <summary>Classifies an attribute value and returns the expression part.</summary>
    public static AttributeValueKind Classify(string value, out string expression)
    {
        var trimmed = value.TrimStart();
        if (trimmed.StartsWith("JScriptLive:", StringComparison.OrdinalIgnoreCase))
        {
            expression = TrimStatement(trimmed["JScriptLive:".Length..]);
            return AttributeValueKind.LiveScript;
        }

        var match = ScriptPrefix().Match(trimmed);
        if (match.Success)
        {
            expression = TrimStatement(trimmed[match.Length..]);
            return AttributeValueKind.Script;
        }

        if (trimmed.StartsWith("wmpprop:", StringComparison.OrdinalIgnoreCase))
        {
            expression = TrimStatement(trimmed["wmpprop:".Length..]);
            return AttributeValueKind.Binding;
        }

        if (trimmed.StartsWith("wmpenabled:", StringComparison.OrdinalIgnoreCase))
        {
            expression = AvailabilityExpression(trimmed["wmpenabled:".Length..], negate: false);
            return AttributeValueKind.Binding;
        }

        if (trimmed.StartsWith("wmpdisabled:", StringComparison.OrdinalIgnoreCase))
        {
            expression = AvailabilityExpression(trimmed["wmpdisabled:".Length..], negate: true);
            return AttributeValueKind.Binding;
        }

        expression = value;
        return AttributeValueKind.Literal;
    }

    /// <summary>"player.controls.play" → "player.controls.isAvailable('play')".</summary>
    private static string AvailabilityExpression(string path, bool negate)
    {
        path = TrimStatement(path);
        var dot = path.LastIndexOf('.');
        var expression = dot < 0
            ? $"player.controls.isAvailable('{path}')"
            : $"{path[..dot]}.isAvailable('{path[(dot + 1)..]}')";
        return negate ? $"!({expression})" : expression;
    }

    private static string TrimStatement(string expression) => expression.Trim().TrimEnd(';').Trim();

    /// <summary>Parses a literal attribute: number, boolean, or string.</summary>
    public static object ParseLiteral(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
            return false;
        if (trimmed.Length > 0 && (char.IsDigit(trimmed[0]) || trimmed[0] is '-' or '+' or '.')
            && double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return number;
        return value;
    }

    public static double ToDouble(object? value, double fallback = 0) => value switch
    {
        null => fallback,
        double d => double.IsNaN(d) ? fallback : d,
        int i => i,
        long l => l,
        float f => f,
        bool b => b ? 1 : 0,
        string s when double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => fallback,
    };

    public static bool ToBool(object? value, bool fallback = false) => value switch
    {
        null => fallback,
        bool b => b,
        double d => d != 0,
        int i => i != 0,
        string s when s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1" || s.Equals("yes", StringComparison.OrdinalIgnoreCase) => true,
        string s when s.Equals("false", StringComparison.OrdinalIgnoreCase) || s == "0" || s.Equals("no", StringComparison.OrdinalIgnoreCase) || s.Length == 0 => false,
        _ => fallback,
    };

    public static string ToText(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        bool b => b ? "true" : "false",
        double d when Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15 => ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    public static bool AreEqual(object? a, object? b)
    {
        if (a is null || b is null)
            return a is null && b is null;
        if (a is double or int or bool && b is double or int or bool)
            return Math.Abs(ToDouble(a) - ToDouble(b)) < 1e-9;
        return string.Equals(ToText(a), ToText(b), StringComparison.Ordinal);
    }

    private static readonly Dictionary<string, int> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = 0x000000, ["white"] = 0xFFFFFF, ["red"] = 0xFF0000, ["green"] = 0x008000, ["lime"] = 0x00FF00,
        ["blue"] = 0x0000FF, ["yellow"] = 0xFFFF00, ["gray"] = 0x808080, ["grey"] = 0x808080, ["silver"] = 0xC0C0C0,
        ["navy"] = 0x000080, ["maroon"] = 0x800000, ["purple"] = 0x800080, ["teal"] = 0x008080, ["olive"] = 0x808000,
        ["aqua"] = 0x00FFFF, ["fuchsia"] = 0xFF00FF, ["orange"] = 0xFFA500, ["darkgray"] = 0xA9A9A9, ["lightgray"] = 0xD3D3D3,
    };

    /// <summary>Parses "#RRGGBB", "#RGB" or a colour name to 0xRRGGBB. Null for "none", "transparent" or invalid.</summary>
    public static int? ParseColor(object? value)
    {
        var text = ToText(value).Trim();
        if (text.Length == 0 || text.Equals("none", StringComparison.OrdinalIgnoreCase) || text.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            return null;
        if (text.StartsWith('#'))
        {
            var hex = text[1..];
            if (hex.Length == 3)
                hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
            if (hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                return rgb;
            return null;
        }

        return NamedColors.TryGetValue(text, out var named) ? named : null;
    }
}

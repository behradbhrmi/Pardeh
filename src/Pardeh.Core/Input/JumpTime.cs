using System.Globalization;

namespace Pardeh.Core.Input;

/// <summary>The "Jump to time" box: "90", "1:30" and "1:02:03" all work.</summary>
public static class JumpTime
{
    public static bool TryParse(string? text, out double seconds)
    {
        seconds = 0;
        var parts = (text ?? string.Empty).Trim().Split(':');
        if (parts.Length > 3)
            return false;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                return false;
            seconds = seconds * 60 + value;
        }

        return true;
    }
}

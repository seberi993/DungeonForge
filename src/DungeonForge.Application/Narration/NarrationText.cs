using System.Text;

namespace DungeonForge.Application.Narration;

// Shared sanitizer for untrusted text that ends up in prompts or broadcasts.
public static class NarrationText
{
    public static string Clean(string? value, int maxLength)
    {
        var cleaned = Collapse(value);
        return cleaned.Length > maxLength ? cleaned[..maxLength].TrimEnd() : cleaned;
    }

    // Like Clean, but an over-long value is cut back to its last full sentence (when one fits) instead of mid-word.
    public static string CleanSentences(string? value, int maxLength)
    {
        var cleaned = Collapse(value);
        if (cleaned.Length <= maxLength)
        {
            return cleaned;
        }

        var cut = cleaned[..maxLength];
        var end = cut.LastIndexOfAny(['.', '!', '?']);
        return end >= maxLength / 3 ? cut[..(end + 1)] : cut.TrimEnd();
    }

    public static string Sanitize(string? value, int maxLength, string neutral)
    {
        var cleaned = Clean(value, maxLength);
        return cleaned.Length == 0 ? neutral : cleaned;
    }

    // Collapses whitespace/control characters into single spaces so untrusted text stays on one line.
    private static string Collapse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                if (sb.Length > 0 && sb[^1] != ' ')
                {
                    sb.Append(' ');
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Trim();
    }
}

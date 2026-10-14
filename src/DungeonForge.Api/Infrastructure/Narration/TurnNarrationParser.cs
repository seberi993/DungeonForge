using System.Text.Json;

namespace DungeonForge.Api.Infrastructure.Narration;

// Defensive parser for the model's {"outcome":"...","next":"..."} reply. Tolerates code fences and chatter around the
// object; each part is sanitized and size-capped independently, and a missing/invalid part becomes an empty string so the
// caller can fall back per part. Null when nothing usable is present.
public static class TurnNarrationParser
{
    public const int MaxOutcomeLength = 500;
    public const int MaxScenarioLength = 1400;

    private static readonly JsonDocumentOptions Lenient = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static TurnNarration? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)], Lenient);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var outcome = Part(doc.RootElement, "outcome", MaxOutcomeLength);
            var next = Part(doc.RootElement, "next", MaxScenarioLength);
            return outcome.Length == 0 && next.Length == 0 ? null : new TurnNarration(outcome, next);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Part(JsonElement root, string name, int maxLength)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String
                && string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return NarrationText.Clean(property.Value.GetString(), maxLength);
            }
        }

        return string.Empty;
    }
}

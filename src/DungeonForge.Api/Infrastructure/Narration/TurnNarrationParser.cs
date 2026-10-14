using System.Text.Json;
using System.Text.RegularExpressions;

namespace DungeonForge.Api.Infrastructure.Narration;

// Defensive parser for the model's {"outcome":"...","next":"..."} reply. Tolerates code fences and chatter around the
// object; each part is sanitized and size-capped independently, and a missing/invalid part becomes an empty string so the
// caller can fall back per part. Null when nothing usable is present.
public static partial class TurnNarrationParser
{
    public const int MaxOutcomeLength = 700;
    public const int MaxScenarioLength = 1400;
    public const int MaxWhisperLength = 160;
    public const int MaxWhispers = 3;
    public const int MaxWhisperTargetLength = 40;

    // The one format the prompts request and ParseWhisper reads; Whisper.Format() writes it back.
    public const string WhisperFormat = "Whisper to <Name>: '<text>'";

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
            var scene = SceneTags.Normalize(Part(doc.RootElement, "scene", 20));
            return outcome.Length == 0 && next.Length == 0
                ? null
                : new TurnNarration(outcome, next, scene) { Whispers = ReadWhispers(doc.RootElement) };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^\s*[-*•]?\s*whisper\s+to\s+(?<target>[^:]+?)\s*:\s*(?<text>.*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex WhisperLine();

    // "Whisper to Mage: 'text'" (single, double or no quotes). Null when the line is not a usable whisper.
    public static Whisper? ParseWhisper(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || WhisperLine().Match(line) is not { Success: true } match)
        {
            return null;
        }

        var target = NarrationText.Clean(match.Groups["target"].Value, MaxWhisperTargetLength);
        var text = NarrationText.Clean(StripQuotes(NarrationText.Clean(match.Groups["text"].Value, 1000)), MaxWhisperLength);
        return target.Length == 0 || text.Length == 0 ? null : new Whisper(target, text);
    }

    private static string StripQuotes(string text)
    {
        if (text.Length >= 2 && (text[0], text[^1]) is ('\'', '\'') or ('"', '"') or ('‘', '’') or ('“', '”'))
        {
            return text[1..^1].Trim();
        }

        return text;
    }

    // "whispers": ["Whisper to ...", {"to":"Mage","text":"..."}]; malformed entries are dropped, the count is capped.
    private static IReadOnlyList<Whisper> ReadWhispers(JsonElement root)
    {
        var whispers = new List<Whisper>();
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array
                || !string.Equals(property.Name, "whispers", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var item in property.Value.EnumerateArray())
            {
                var whisper = item.ValueKind switch
                {
                    JsonValueKind.String => ParseWhisper(item.GetString()),
                    JsonValueKind.Object => FromObject(item),
                    _ => null,
                };
                if (whisper is not null && whispers.Count < MaxWhispers)
                {
                    whispers.Add(whisper);
                }
            }
        }

        return whispers;
    }

    private static Whisper? FromObject(JsonElement item)
    {
        var target = NarrationText.Clean(Part(item, "to", MaxWhisperTargetLength) is { Length: > 0 } to ? to : Part(item, "target", MaxWhisperTargetLength), MaxWhisperTargetLength);
        var text = Part(item, "text", MaxWhisperLength);
        return target.Length == 0 || text.Length == 0 ? null : new Whisper(target, text);
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

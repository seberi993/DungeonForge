using System.Text.Json;
using DungeonForge.Api.Features.Social.Core;
using DungeonForge.Api.Features.Social.Seed;

namespace DungeonForge.Api.Features.Social.Supervision;

// Turns model text into a Decision the party can safely apply, or null when the text is unusable.
public static class JudgeDecisionParser
{
    public static Decision? TryParse(string text, string eventId, HashSet<string> allowedPlayers)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonText.Strip(text));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("verdict", out var v) || v.ValueKind != JsonValueKind.String
                || !Enum.TryParse<Verdict>(v.GetString(), true, out var verdict) || !Enum.IsDefined(verdict))
            {
                return null;
            }

            var severity = root.TryGetProperty("severity", out var s) && JsonText.TryInt(s, out var sv)
                ? Math.Clamp(sv, 0, NarratorJudge.MaxSeverity)
                : 0;
            var rationale = Clean(root.TryGetProperty("rationale", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null,
                NarratorJudge.MaxRationaleLength);

            var consequences = new List<Consequence>();
            if (verdict is Verdict.Celebrate or Verdict.Punish
                && root.TryGetProperty("consequences", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray().Take(NarratorJudge.MaxConsequences))
                {
                    if (item.ValueKind == JsonValueKind.Object && ReadConsequence(item, allowedPlayers) is { } c)
                    {
                        consequences.Add(c);
                    }
                }
            }

            return new Decision(eventId, verdict, severity, consequences, NarratorJudge.RuleId,
                rationale.Length == 0 ? "Judged by the narrator." : rationale, Llm: true);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Consequence? ReadConsequence(JsonElement item, HashSet<string> allowed)
    {
        var type = Str(item, "type")?.ToLowerInvariant();
        if (type == "notice")
        {
            var text = Clean(Str(item, "text"), ConsequenceApplier.MaxNoticeLength);
            var to = Str(item, "to");
            var party = string.IsNullOrWhiteSpace(to) || string.Equals(to, "null", StringComparison.OrdinalIgnoreCase);
            if (text.Length == 0 || (!party && !allowed.Contains(to!)))
            {
                return null;
            }

            return new Consequence.Notice(new SocialNotice(party ? null : allowed.TryGetValue(to!, out var target) ? target : null, "narrator", text));
        }

        var name = Str(item, "player");
        if (name is null || !allowed.TryGetValue(name, out var player))
        {
            return null;
        }

        return type switch
        {
            "axis" when Enum.TryParse<Axis>(Str(item, "axis"), true, out var axis) && Enum.IsDefined(axis)
                => new Consequence.AxisShift(player, axis, Int(item, "delta")),
            "reputation" => new Consequence.Reputation(player, Int(item, "trust"), Int(item, "menace")),
            "credibility" => new Consequence.Credibility(player, Int(item, "delta")),
            "tokens" => new Consequence.Tokens(player, Int(item, "delta")),
            _ => null,
        };
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var p) && JsonText.TryInt(p, out var v) ? v : 0;

    // Single line, no control characters, bounded length.
    private static string Clean(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var chars = text.Select(c => char.IsControl(c) ? ' ' : c).ToArray();
        var flat = string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return flat.Length > max ? flat[..max] : flat;
    }
}

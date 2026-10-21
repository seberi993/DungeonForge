using System.Text.RegularExpressions;
using DungeonForge.Application.Features.Social.Core;

namespace DungeonForge.Application.Features.Social.Seed;

// What an answer means: bounded axis deltas, tone words and abuse flags. Produced by any IAxisParser.
public sealed record AxisParse(
    IReadOnlyDictionary<Axis, int> Deltas, IReadOnlyList<string> Tone, IReadOnlyList<string> Flags, double Confidence)
{
    public const int MaxDelta = 20;
    public const int MaxAxes = 3;

    public static class FlagNames
    {
        public const string LowEffort = "low_effort";
        public const string Injection = "injection_attempt";
    }

    public bool IsLowEffort => Flags.Contains(FlagNames.LowEffort);

    public bool IsInjection => Flags.Contains(FlagNames.Injection);

    // Whatever produced the parse (a model included), C# decides what is allowed: clamp, cap the axis count, and
    // ignore the deltas entirely when the answer was flagged.
    public AxisParse Sanitized()
    {
        if (IsInjection || IsLowEffort)
        {
            return this with { Deltas = new Dictionary<Axis, int>() };
        }

        var kept = Deltas
            .Where(d => Enum.IsDefined(d.Key) && d.Value != 0)
            .OrderByDescending(d => Math.Abs(d.Value))
            .Take(MaxAxes)
            .ToDictionary(d => d.Key, d => Math.Clamp(d.Value, -MaxDelta, MaxDelta));
        return this with { Deltas = kept };
    }
}

public interface IAxisParser
{
    Task<AxisParse> ParseAsync(string question, string answer, CancellationToken ct = default);
}

public static class SeedQuestions
{
    public const int MinAnswerLength = 5;
    public const int MaxAnswerLength = 140;

    public static readonly IReadOnlyList<string> All =
    [
        "The door asks: what do you do when no one is watching?",
        "A stranger offers a bargain in the dark. How do you answer?",
        "The torch gutters. What do you reach for first?",
    ];

    // Same party, same question: stable for the whole run.
    public static string For(ulong runSeed) => All[SeededRng.For(runSeed, "seed-question").Next(All.Count)];
}

// Deterministic fallback parser (a model-backed parser can replace it by registering another IAxisParser).
public sealed partial class KeywordAxisParser : IAxisParser
{
    private const int HitWeight = 7;

    private static readonly (Axis Axis, int Sign, string[] Stems)[] Lexicon =
    [
        (Axis.Valor, 1, ["fight", "charge", "attack", "brave", "defend", "protect", "face", "hold the line"]),
        (Axis.Valor, -1, ["flee", "run", "hide", "afraid", "scared", "retreat"]),
        (Axis.Cunning, 1, ["trick", "lie", "sneak", "bluff", "plan", "scheme", "hide a", "steal", "deceive", "distract"]),
        (Axis.Empathy, 1, ["help", "share", "heal", "comfort", "friend", "care", "split", "together", "save"]),
        (Axis.Empathy, -1, ["ignore", "abandon", "leave them", "alone"]),
        (Axis.Discipline, 1, ["plan", "careful", "order", "rules", "map", "prepare", "scout", "wait"]),
        (Axis.Chaos, 1, ["chaos", "random", "burn", "wild", "gamble", "improvise", "break", "explode", "coin flip"]),
        (Axis.Greed, 1, ["gold", "loot", "coin", "treasure", "keep", "pocket", "rich", "take it"]),
        (Axis.Greed, -1, ["give away", "donate", "share the"]),
        (Axis.Lore, 1, ["read", "study", "rune", "book", "ancient", "research", "history", "scroll"]),
        (Axis.Resolve, 1, ["never give up", "endure", "persist", "stubborn", "keep going", "refuse", "stand"]),
    ];

    private static readonly (string Tone, string[] Stems)[] Tones =
    [
        ("wry", ["heh", "haha", "lol", "maybe", "obviously", "of course"]),
        ("earnest", ["i will", "i would", "i'd", "honestly", "truly"]),
        ("grim", ["die", "dead", "blood", "grim", "dark"]),
        ("reckless", ["yolo", "who cares", "burn", "wild", "gamble"]),
        ("cold", ["calculate", "efficient", "necessary", "cost"]),
    ];

    [GeneratedRegex(@"ignore (all |any )?(previous|prior|above)|system prompt|you are now|disregard (the|all|your)|pretend to be", RegexOptions.IgnoreCase)]
    private static partial Regex InjectionPattern();

    [GeneratedRegex(@"(.)\1{5,}")]
    private static partial Regex RepeatedCharPattern();

    public Task<AxisParse> ParseAsync(string question, string answer, CancellationToken ct = default) =>
        Task.FromResult(Parse(answer));

    public static AxisParse Parse(string answer)
    {
        var text = answer.Trim().ToLowerInvariant();
        var flags = new List<string>();
        if (InjectionPattern().IsMatch(text))
        {
            flags.Add(AxisParse.FlagNames.Injection);
        }

        var letters = text.Count(char.IsLetter);
        if (text.Length < SeedQuestions.MinAnswerLength || letters < 3 || RepeatedCharPattern().IsMatch(text)
            || text.Where(char.IsLetter).Distinct().Count() < 3)
        {
            flags.Add(AxisParse.FlagNames.LowEffort);
        }

        var deltas = new Dictionary<Axis, int>();
        foreach (var (axis, sign, stems) in Lexicon)
        {
            var hits = stems.Count(s => text.Contains(s, StringComparison.Ordinal));
            if (hits > 0)
            {
                deltas[axis] = deltas.GetValueOrDefault(axis) + sign * hits * HitWeight;
            }
        }

        var tone = Tones.Where(t => t.Stems.Any(s => text.Contains(s, StringComparison.Ordinal))).Select(t => t.Tone).Take(2).ToArray();
        var confidence = deltas.Count == 0 ? 0.2 : Math.Min(0.9, 0.4 + 0.15 * deltas.Count);
        return new AxisParse(deltas, tone, flags, confidence).Sanitized();
    }
}

namespace DungeonForge.Api.Features.Social.Core;

// ---- Abilities ----

// One typed effect. Types are resolved by AbilityEffects; an unknown type is inert, so a module can be removed safely.
public sealed record AbilityEffect(string Type, int Amount);

public sealed record AbilityStats(int Budget, int Cooldown, IReadOnlyList<AbilityEffect> Effects, ulong Seed);

public sealed record Ability(
    string Id,
    string TemplateId,
    string Name,
    string Archetype,
    string Rarity,
    IReadOnlyList<string> AffixIds,
    string Flavor,
    string Hint,
    AbilityStats Stats);

// ---- Profiles ----

public sealed class PlayerProfile(string name)
{
    public const int StartingTokens = 3;
    public const int StartingCredibility = 2;
    public const int MaxCredibility = 4;

    public string Name { get; } = name;

    public AxisVector Axes { get; } = new();

    // What teammates alone contributed per axis; capped separately so peers cannot rewrite who you are.
    public AxisVector PeerShift { get; } = new();

    public List<string> Tone { get; } = [];

    public int Trust { get; set; }

    public int Menace { get; set; }

    public int Credibility { get; set; } = StartingCredibility;

    public int AskTokens { get; set; } = StartingTokens;

    public bool Seeded { get; set; }

    public bool OptedOut { get; set; }

    public string? SeedAnswerHash { get; set; }

    public List<Ability> Abilities { get; } = [];

    // Turn on which each ability was last used (cooldown bookkeeping).
    public Dictionary<string, int> LastUsed { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Tags => TagCatalog.For(Axes, Trust, Menace);
}

public static class TagCatalog
{
    private const int Threshold = 8;

    private static readonly Dictionary<Axis, (string High, string Low)> Names = new()
    {
        [Axis.Valor] = ("bold", "timid"),
        [Axis.Cunning] = ("sly", "guileless"),
        [Axis.Empathy] = ("kind-hearted", "cold"),
        [Axis.Discipline] = ("orderly", "unruly"),
        [Axis.Chaos] = ("wild", "staid"),
        [Axis.Greed] = ("covetous", "open-handed"),
        [Axis.Lore] = ("learned", "unlettered"),
        [Axis.Resolve] = ("unyielding", "wavering"),
    };

    // Qualitative flavor only: the two strongest axes past the threshold, plus a reputation word. Never a number.
    public static IReadOnlyList<string> For(AxisVector axes, int trust = 0, int menace = 0)
    {
        var tags = new List<string>();
        foreach (var axis in axes.Strongest(2))
        {
            var value = axes[axis];
            if (Math.Abs(value) >= Threshold)
            {
                tags.Add(value > 0 ? Names[axis].High : Names[axis].Low);
            }
        }

        if (trust >= 2) tags.Add("trusted");
        else if (trust <= -2) tags.Add("distrusted");
        if (menace >= 2) tags.Add("feared");
        return tags;
    }
}

// ---- Journal and adjudication ----

public static class SocialEventTypes
{
    public const string AbilityUsed = "ability_used";
    public const string PeerUnanimous = "peer_unanimous";
    public const string AskReply = "ask_reply";
    public const string AskConsensus = "ask_consensus";
}

public sealed record SocialEvent(
    string Id,
    string Type,
    int Turn,
    IReadOnlyList<string> Actors,
    IReadOnlyDictionary<string, string> Data,
    IReadOnlyList<string> EvidenceRefs);

public enum Verdict
{
    Ignore,
    Celebrate,
    Punish,
    Unresolved,
}

// Null To = the whole party; otherwise one player.
public sealed record SocialNotice(string? To, string Kind, string Text);

public abstract record Consequence
{
    public sealed record AxisShift(string Player, Axis Axis, int Delta) : Consequence;

    public sealed record Reputation(string Player, int Trust, int Menace) : Consequence;

    public sealed record Credibility(string Player, int Delta) : Consequence;

    public sealed record Tokens(string Player, int Delta) : Consequence;

    // Game-state consequences. Only the C# rules emit these; the model-backed judge cannot (its parser has no such type).
    public sealed record Hp(string Player, int Delta) : Consequence;

    public sealed record Gold(int Delta) : Consequence;

    public sealed record Notice(SocialNotice Message) : Consequence;
}

public sealed record Decision(
    string EventId,
    Verdict Verdict,
    int Severity,
    IReadOnlyList<Consequence> Consequences,
    string Rule,
    string Rationale,
    bool Llm = false);

// Server-side only. Explains every narrator decision for debugging; never serialized to clients.
public sealed record AuditEntry(SocialEvent Event, Decision Decision, ulong Seed, DateTimeOffset At);

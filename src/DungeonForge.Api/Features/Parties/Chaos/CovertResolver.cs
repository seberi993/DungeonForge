using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;

namespace DungeonForge.Api.Features.Parties.Chaos;

public sealed record CovertOutcome(string Actor, string Target, CovertKind Kind, bool Success, int Amount, string Log);

// Hits are extra damage to apply, Heals credit the thief, Sabotaged names have their overt action forced to fail.
public sealed record CovertResolution(
    IReadOnlyList<CovertOutcome> Outcomes,
    IReadOnlyList<DamageResult> Hits,
    IReadOnlySet<string> Sabotaged,
    IReadOnlyList<(string Name, int Amount)> Heals)
{
    public static CovertResolution None { get; } = new([], [], new HashSet<string>(), []);

    public IReadOnlyList<string> Log => [.. Outcomes.Select(o => o.Log)];
}

// Pure C# resolution of secret plans (backstab, spell sabotage, stolen life force). The narrator only describes the result.
public static class CovertResolver
{
    public const int BasePercent = 40;
    public const int ClassBonusPercent = 20;
    public const int BackstabDamage = 5;
    public const int StealAmount = 3;

    public static int ChancePercent(CovertKind kind, CharacterClass actor)
    {
        var specialist = kind switch
        {
            CovertKind.Backstab or CovertKind.Steal => actor == CharacterClass.Bard,
            _ => actor is CharacterClass.Mage or CharacterClass.Necromancer,
        };
        return BasePercent + (specialist ? ClassBonusPercent : 0);
    }

    // One roll per valid intent, in action order. Unknown, defeated or self targets are ignored without consuming a roll.
    public static CovertResolution Resolve(IReadOnlyList<PlayerAction> actions, IEnumerable<PartyMember> members, IRoller roller)
    {
        var alive = members.Where(m => !m.IsDefeated).ToArray();
        var outcomes = new List<CovertOutcome>();
        var hits = new List<DamageResult>();
        var sabotaged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var heals = new List<(string, int)>();

        foreach (var action in actions)
        {
            if (action.Covert is not { } intent
                || alive.FirstOrDefault(m => Same(m.Name, action.Name)) is not { } actor
                || alive.FirstOrDefault(m => Same(m.Name, intent.Target)) is not { } target
                || actor == target)
            {
                continue;
            }

            var success = roller.Next(100) < ChancePercent(intent.Kind, actor.Class);
            var amount = !success ? 0 : intent.Kind switch
            {
                CovertKind.Backstab => BackstabDamage,
                CovertKind.Steal => StealAmount,
                _ => 0,
            };

            if (success)
            {
                switch (intent.Kind)
                {
                    case CovertKind.Backstab:
                        hits.Add(new DamageResult(target.Name, amount));
                        break;
                    case CovertKind.Steal:
                        hits.Add(new DamageResult(target.Name, amount));
                        heals.Add((actor.Name, amount));
                        break;
                    default:
                        sabotaged.Add(target.Name);
                        break;
                }
            }

            outcomes.Add(new CovertOutcome(
                actor.Name, target.Name, intent.Kind, success, amount, Describe(actor.Name, target.Name, intent.Kind, success, amount)));
        }

        return new CovertResolution(outcomes, hits, sabotaged, heals);
    }

    private static bool Same(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Describe(string actor, string target, CovertKind kind, bool success, int amount) => (kind, success) switch
    {
        (CovertKind.Backstab, true) => $"{actor} backstabbed {target} for {amount} damage.",
        (CovertKind.Backstab, false) => $"{actor} tried to backstab {target} and was caught.",
        (CovertKind.Sabotage, true) => $"{actor} sabotaged {target}'s spell and it fizzled.",
        (CovertKind.Sabotage, false) => $"{actor} tried to sabotage {target}'s spell but failed.",
        (_, true) => $"{actor} stole {amount} life force from {target}.",
        _ => $"{actor} tried to steal from {target} and was caught.",
    };
}

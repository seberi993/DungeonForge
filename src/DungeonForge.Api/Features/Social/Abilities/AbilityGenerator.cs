using DungeonForge.Api.Features.Social.Core;

namespace DungeonForge.Api.Features.Social.Abilities;

public sealed record AbilityParams
{
    public int AbilitiesPerPlayer { get; init; } = 2;

    public double Jitter { get; init; } = 0.35;

    // The second ability is deliberately less predictable.
    public double SurpriseJitter { get; init; } = 1.5;

    public double GapBonus { get; init; } = 0.5;

    public double CrowdPenalty { get; init; } = 0.5;

    public int CrowdAt { get; init; } = 3;

    public int MaxAttempts { get; init; } = 12;
}

// Seeded: same profile + same seed = same abilities; same profile + another run seed = different ones.
public sealed class AbilityGenerator(AbilityParams? parameters = null)
{
    private readonly AbilityParams _p = parameters ?? new AbilityParams();

    public IReadOnlyDictionary<string, IReadOnlyList<Ability>> GenerateForParty(
        ulong runSeed, IReadOnlyList<(string Name, AxisVector Axes)> members)
    {
        var result = new Dictionary<string, IReadOnlyList<Ability>>(StringComparer.OrdinalIgnoreCase);
        var archetypeCounts = AbilityCatalog.Archetypes.ToDictionary(a => a, _ => 0);

        for (var m = 0; m < members.Count; m++)
        {
            var (name, axes) = members[m];
            var chosen = new List<AbilityTemplate>();
            for (var k = 0; k < _p.AbilitiesPerPlayer; k++)
            {
                var surprise = k > 0;
                var rng = SeededRng.For(runSeed, $"pick:{name}", k);
                var template = Pick(axes, archetypeCounts, chosen, rng, surprise);
                chosen.Add(template);
                archetypeCounts[template.Archetype]++;
            }

            result[name] = [.. chosen.Select((t, k) => Build(runSeed, name, k, t))];
        }

        return result;
    }

    private AbilityTemplate Pick(
        AxisVector axes, Dictionary<string, int> counts, List<AbilityTemplate> already, SeededRng rng, bool surprise)
    {
        AbilityTemplate? best = null;
        var bestScore = double.MinValue;
        foreach (var template in AbilityCatalog.Templates)
        {
            if (already.Any(a => a.Id == template.Id || (surprise && a.Archetype == template.Archetype)))
            {
                continue;
            }

            var fit = template.Weights.Sum(w => w.Weight * axes[w.Axis] / 100.0 * 4);
            var gap = counts[template.Archetype] == 0 ? _p.GapBonus : 0;
            var crowd = counts[template.Archetype] >= _p.CrowdAt ? _p.CrowdPenalty : 0;
            var jitter = (rng.NextDouble() - 0.5) * 2 * (surprise ? _p.SurpriseJitter : _p.Jitter);
            var score = fit + gap - crowd + jitter;
            if (score > bestScore)
            {
                bestScore = score;
                best = template;
            }
        }

        return best ?? AbilityCatalog.Templates[0];
    }

    private Ability Build(ulong runSeed, string name, int index, AbilityTemplate template)
    {
        for (var attempt = 0; attempt < _p.MaxAttempts; attempt++)
        {
            var seed = SeededRng.Derive(runSeed, $"build:{name}:{template.Id}", index * 100 + attempt);
            var rng = new SeededRng(seed);
            var rarity = rng.Next(100) switch { < 60 => Rarity.Common, < 90 => Rarity.Uncommon, _ => Rarity.Rare };
            var affixes = PickAffixes(rarity, rng);
            var ability = Assemble(template, affixes, rarity, seed, $"ab_{name}_{index}");
            if (AbilityBudget.InWindow(ability.Stats.Budget))
            {
                return ability;
            }
        }

        // Always in window: every template's own budget is, and an affix-free build never rolls.
        return Assemble(template, [], Rarity.Common, SeededRng.Derive(runSeed, $"plain:{name}:{template.Id}"), $"ab_{name}_{index}");
    }

    private static List<Affix> PickAffixes(Rarity rarity, SeededRng rng)
    {
        Affix PickOf(Rarity r)
        {
            var pool = AbilityCatalog.Affixes.Where(a => a.Rarity == r).ToArray();
            return pool[rng.Next(pool.Length)];
        }

        return rarity switch
        {
            Rarity.Common => rng.Next(2) == 0 ? [] : [PickOf(Rarity.Common)],
            Rarity.Uncommon => [PickOf(Rarity.Uncommon)],
            _ => rng.Next(2) == 0 ? [PickOf(Rarity.Rare)] : [PickOf(Rarity.Rare), PickOf(Rarity.Common)],
        };
    }

    private static Ability Assemble(AbilityTemplate template, List<Affix> affixes, Rarity rarity, ulong seed, string id)
    {
        var effects = template.Effects
            .Concat(affixes.SelectMany(a => a.Adds))
            .GroupBy(e => e.Type)
            .Select(g => new AbilityEffect(g.Key, g.Sum(e => e.Amount)))
            .ToArray();
        var cooldown = Math.Clamp(template.Cooldown + affixes.Sum(a => a.CooldownDelta), 1, 6);
        var budget = AbilityBudget.Of(effects, cooldown);

        var prefix = string.Join(' ', affixes.OrderByDescending(a => a.Rarity).Select(a => a.Name));
        var name = prefix.Length == 0 ? template.Name : $"{prefix} {template.Name}";
        var flavor = affixes.Count == 0
            ? template.Flavor
            : $"{template.Flavor.TrimEnd('.')} — {string.Join(", ", affixes.Select(a => a.Flavor))}.";

        return new Ability(
            id, template.Id, name, template.Archetype, rarity.ToString().ToLowerInvariant(), [.. affixes.Select(a => a.Id)],
            flavor, AbilityHints.For(effects, cooldown), new AbilityStats(budget, cooldown, effects, seed));
    }
}

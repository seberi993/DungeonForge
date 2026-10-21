using DungeonForge.Application.Features.Social.Core;

namespace DungeonForge.Application.Features.Social.Abilities;

public static class EffectTypes
{
    public const string RollBonus = "roll_bonus";
    public const string TeamBonus = "team_bonus";
    public const string Shield = "shield";
    public const string GoldBonus = "gold_bonus";
    public const string GrantToken = "grant_token";
}

public sealed record AbilityTemplate(
    string Id,
    string Name,
    string Archetype,
    (Axis Axis, double Weight)[] Weights,
    AbilityEffect[] Effects,
    int Cooldown,
    string Flavor);

public enum Rarity
{
    Common,
    Uncommon,
    Rare,
}

public sealed record Affix(
    string Id, string Name, Rarity Rarity, AbilityEffect[] Adds, int CooldownDelta, string Flavor);

// Pure data. Adding or removing a template or affix is a one-line change; the generator and the budget need no edits.
public static class AbilityCatalog
{
    public static readonly IReadOnlyList<string> Archetypes = ["guardian", "striker", "support", "control", "utility"];

    private static AbilityEffect E(string type, int amount) => new(type, amount);

    private static readonly (Axis, double)[] Guard = [(Axis.Resolve, 1), (Axis.Discipline, 0.7), (Axis.Empathy, 0.4)];
    private static readonly (Axis, double)[] Strike = [(Axis.Valor, 1), (Axis.Chaos, 0.6), (Axis.Greed, 0.3)];
    private static readonly (Axis, double)[] Aid = [(Axis.Empathy, 1), (Axis.Lore, 0.5), (Axis.Discipline, 0.4)];
    private static readonly (Axis, double)[] Hinder = [(Axis.Cunning, 1), (Axis.Lore, 0.5), (Axis.Discipline, 0.3)];
    private static readonly (Axis, double)[] Odds = [(Axis.Greed, 1), (Axis.Cunning, 0.6), (Axis.Chaos, 0.5)];

    public static readonly IReadOnlyList<AbilityTemplate> Templates =
    [
        new("T01", "Bulwark", "guardian", Guard, [E(EffectTypes.Shield, 40)], 3, "A wall of will that remembers who stood behind it."),
        new("T02", "Taunting Roar", "guardian", Guard, [E(EffectTypes.Shield, 32), E(EffectTypes.TeamBonus, 5)], 2, "A bellow that drags every blow your way."),
        new("T03", "Oathbound Strike", "striker", Strike, [E(EffectTypes.RollBonus, 24)], 2, "A promise kept with an edge."),
        new("T04", "Reckless Cleave", "striker", Strike, [E(EffectTypes.RollBonus, 30)], 1, "No guard, no regret, one clean arc."),
        new("T05", "Shadow Step", "striker", Strike, [E(EffectTypes.RollBonus, 18), E(EffectTypes.Shield, 24)], 1, "You are somewhere else when it lands."),
        new("T06", "Executioner's Mark", "striker", Strike, [E(EffectTypes.RollBonus, 20), E(EffectTypes.GoldBonus, 6)], 2, "Chosen, counted, collected."),
        new("T07", "Mending Hymn", "support", Aid, [E(EffectTypes.TeamBonus, 8), E(EffectTypes.Shield, 24)], 2, "A tune that closes small wounds and large doubts."),
        new("T08", "Shared Burden", "support", Aid, [E(EffectTypes.Shield, 40), E(EffectTypes.TeamBonus, 4)], 1, "Many shoulders, one weight."),
        new("T09", "Rally Cry", "support", Aid, [E(EffectTypes.TeamBonus, 12)], 2, "A shout that tightens every grip."),
        new("T10", "Lull", "control", Hinder, [E(EffectTypes.Shield, 48)], 2, "The dungeon forgets, briefly, to be cruel."),
        new("T11", "Snare of Doubt", "control", Hinder, [E(EffectTypes.TeamBonus, 6), E(EffectTypes.RollBonus, 10), E(EffectTypes.Shield, 8)], 2, "A whisper in the enemy's ear that is never quite true."),
        new("T12", "Sunder Rune", "control", Hinder, [E(EffectTypes.RollBonus, 26)], 2, "A rune that unpicks armor stitch by stitch."),
        new("T13", "Pickpocket's Eye", "utility", Odds, [E(EffectTypes.GoldBonus, 12), E(EffectTypes.RollBonus, 10)], 3, "You see what's loose before it knows it is."),
        new("T14", "Lorekeeper's Glance", "utility", Odds, [E(EffectTypes.RollBonus, 16), E(EffectTypes.TeamBonus, 4)], 2, "An old page turns behind your eyes."),
        new("T15", "Lucky Gambit", "utility", Odds, [E(EffectTypes.RollBonus, 28)], 1, "Luck is just nerve with better manners."),
        new("T16", "Whisper Network", "utility", Odds, [E(EffectTypes.GrantToken, 1), E(EffectTypes.RollBonus, 8)], 3, "A thread of quiet words only you can follow."),
    ];

    public static readonly IReadOnlyList<Affix> Affixes =
    [
        new("A01", "Swift", Rarity.Common, [], -1, "quick to return"),
        new("A02", "Sturdy", Rarity.Common, [E(EffectTypes.RollBonus, 6)], 0, "steadier than it looks"),
        new("A03", "Thorned", Rarity.Common, [E(EffectTypes.Shield, 8)], 0, "bristling at the edges"),
        new("A04", "Greedy", Rarity.Uncommon, [E(EffectTypes.GoldBonus, 8)], 0, "it likes coin"),
        new("A05", "Echoing", Rarity.Uncommon, [E(EffectTypes.TeamBonus, 4)], 0, "it carries to the others"),
        new("A06", "Warded", Rarity.Uncommon, [E(EffectTypes.Shield, 16)], 0, "wrapped in old wards"),
        new("A07", "Oathbreaker", Rarity.Rare, [E(EffectTypes.RollBonus, 14)], 1, "powerful, and slow to forgive"),
        new("A08", "Voidtouched", Rarity.Rare, [E(EffectTypes.RollBonus, 10), E(EffectTypes.TeamBonus, 2)], 0, "it hums with something absent"),
        new("A09", "Kindled", Rarity.Rare, [E(EffectTypes.TeamBonus, 6)], 0, "warm to those nearby"),
        new("A10", "Bargain", Rarity.Rare, [E(EffectTypes.RollBonus, 8)], -2, "cheap, and you will wonder why"),
    ];

    public static AbilityTemplate? Template(string id) => Templates.FirstOrDefault(t => t.Id == id);

    public static Affix? Affix(string id) => Affixes.FirstOrDefault(a => a.Id == id);
}

// What an ability is worth: effect points plus a credit for a longer cooldown. Starter abilities stay in a narrow window.
public static class AbilityBudget
{
    public const int Min = 14;
    public const int Max = 24;

    public static int Points(AbilityEffect effect) => effect.Type switch
    {
        EffectTypes.RollBonus => effect.Amount / 2,
        EffectTypes.Shield => effect.Amount / 4,
        EffectTypes.TeamBonus => effect.Amount,
        EffectTypes.GoldBonus => effect.Amount / 2,
        EffectTypes.GrantToken => effect.Amount * 6,
        _ => 0,
    };

    public static int Of(IEnumerable<AbilityEffect> effects, int cooldown) => effects.Sum(Points) + cooldown * 2;

    public static bool InWindow(int budget) => budget is >= Min and <= Max;
}

// Opaque by design: players read a name, a line of flavor and a qualitative hint. Numbers stay on the server.
public static class AbilityHints
{
    public static string For(IEnumerable<AbilityEffect> effects, int cooldown)
    {
        var parts = new List<string>();
        foreach (var effect in effects.OrderByDescending(AbilityBudget.Points))
        {
            var phrase = effect.Type switch
            {
                EffectTypes.RollBonus => "Steadies your hand",
                EffectTypes.Shield => "Turns blows aside",
                EffectTypes.TeamBonus => "Lifts those beside you",
                EffectTypes.GoldBonus => "Finds coin where none seemed",
                EffectTypes.GrantToken => "Opens quiet channels",
                _ => null,
            };
            if (phrase is not null && !parts.Contains(phrase))
            {
                parts.Add(phrase);
            }
        }

        var speed = cooldown >= 3 ? "slow to return" : cooldown <= 1 ? "quick to return" : "steady to return";
        return $"{string.Join(", ", parts.Take(2))}; {speed}.";
    }
}

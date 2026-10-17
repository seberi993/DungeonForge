using System.Text.RegularExpressions;

namespace DungeonForge.Api.Domain.Rules;

// Game math for the per-player roll lives here, never in the narrator.
public static partial class ActionOdds
{
    public const int BasePercent = 50;
    public const int TeamworkPercent = 10;
    public const int ClashPercent = 10;
    public const int MaxModifier = 20;
    public const int MinPercent = 15;
    public const int MaxPercent = 85;

    // d100 extremes. CritBelow sits under MinPercent and FumbleFrom above MaxPercent, so they never contradict the odds.
    public const int CritBelow = 5;
    public const int FumbleFrom = 95;
    public const int CritGold = 5;

    // Boss HP each passed roll removes; a crit hits twice as hard.
    public const int BossHitDamage = 10;
    public const int BossCritDamage = 20;

    public static bool IsCrit(int roll) => roll < CritBelow;

    public static bool IsFumble(int roll) => roll >= FumbleFrom;

    // A fumble hits half again as hard, rounded up.
    public static int FumbleDamage(int damage) => (damage * 3 + 1) / 2;

    [GeneratedRegex(@"\b(attack|stab|strike|hit|slash|smash|punch|kill|charge|fight|shoot|fireball)", RegexOptions.IgnoreCase)]
    private static partial Regex AttackIdea();

    [GeneratedRegex(@"\b(flee|run|dodge|hide|evade|escape|retreat|sneak|duck|avoid|back\s?off)", RegexOptions.IgnoreCase)]
    private static partial Regex EvadeIdea();

    [GeneratedRegex(@"\b(grab|loot|chest|gold|treasure|coin|steal|take|open)", RegexOptions.IgnoreCase)]
    private static partial Regex LootIdea();

    public static ActionKind Classify(string text) =>
        AttackIdea().IsMatch(text) ? ActionKind.Attack
        : EvadeIdea().IsMatch(text) ? ActionKind.Evade
        : LootIdea().IsMatch(text) ? ActionKind.Loot
        : ActionKind.Other;

    // Teammates doing the same thing help; teammates working against you (fighting while others flee, grabbing loot
    // mid-fight) hurt. "Other" ideas neither help nor clash.
    public static int Percent(ActionKind mine, IEnumerable<ActionKind> others)
    {
        var same = 0;
        var clash = 0;
        foreach (var other in others)
        {
            if (mine == ActionKind.Other || other == ActionKind.Other) continue;
            if (other == mine) same++;
            else clash++;
        }

        var modifier = Math.Min(MaxModifier, same * TeamworkPercent) - Math.Min(MaxModifier, clash * ClashPercent);
        return Math.Clamp(BasePercent + modifier, MinPercent, MaxPercent);
    }

    // Chance for each action in the discussion, in order, given what the rest of the party locked in.
    public static IReadOnlyList<int> For(IReadOnlyList<PlayerAction> discussion)
    {
        var kinds = discussion.Select(a => Classify(a.Text)).ToArray();
        return [.. kinds.Select((kind, i) => Percent(kind, kinds.Where((_, j) => j != i)))];
    }
}

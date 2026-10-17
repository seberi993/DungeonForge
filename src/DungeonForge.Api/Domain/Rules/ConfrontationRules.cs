namespace DungeonForge.Api.Domain.Rules;

// Pure rules: majority/plurality with roller-broken ties, and what each verdict costs the accused.
public static class ConfrontationRules
{
    // A confrontation that has not heard from everyone by then resolves anyway; missing voters abstain.
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public const int AttackDamage = 8;

    public static IReadOnlyDictionary<ConfrontationVote, int> Tally(IEnumerable<ConfrontationVote> votes)
    {
        var counts = Enum.GetValues<ConfrontationVote>().ToDictionary(v => v, _ => 0);
        foreach (var vote in votes)
        {
            counts[vote]++;
        }

        return counts;
    }

    // Most votes wins; a tie for first is broken by the roller among the tied options in enum order.
    public static ConfrontationVote Decide(IReadOnlyCollection<ConfrontationVote> votes, IRoller roller)
    {
        var tally = Tally(votes);
        var top = tally.Values.Max();
        var tied = tally.Where(kv => kv.Value == top).Select(kv => kv.Key).OrderBy(v => v).ToArray();
        return tied.Length == 1 ? tied[0] : tied[roller.Next(tied.Length) % tied.Length];
    }

    public static ConfrontationEffect Outcome(ConfrontationVote vote, int gold) => vote switch
    {
        ConfrontationVote.Forgive => new(0, gold, false, 0, true),
        ConfrontationVote.DemandCut => new((gold + 1) / 2, gold - (gold + 1) / 2, false, 0, true),
        ConfrontationVote.Banish => new(gold, 0, true, 0, false),
        _ => new(gold, 0, false, AttackDamage, false),
    };
}

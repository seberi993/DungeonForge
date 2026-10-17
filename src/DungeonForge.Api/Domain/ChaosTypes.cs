
namespace DungeonForge.Api.Domain;

// Optional run modifiers chosen at party creation. Chaos = blind action phase + contested loot; TraitorPact = hidden traitor.
public sealed record PartyModifiers(bool Chaos = false, bool TraitorPact = false);

public enum VaultChoice
{
    Share,
    Pocket,
}

// Artifacts queue up (a later drop never overwrites an earlier one); Artifact is their display text.
public sealed record UnclaimedVault(int Gold, IReadOnlyList<string> Artifacts)
{
    public string? Artifact => Artifacts.Count == 0 ? null : string.Join(" and a ", Artifacts);
}

public sealed record PocketedLoot(int Gold, string? Artifact);

public enum VaultClaimStatus
{
    NoVault,
    UnknownPlayer,
    ConfrontationActive,
    Shared,
    PocketedHidden,
    PocketedCaught,
}

public sealed record VaultClaim(VaultClaimStatus Status, int Gold = 0, string? Artifact = null, IReadOnlyList<string>? Spotters = null);

public enum ConfrontationVote
{
    Forgive,
    DemandCut,
    Banish,
    Attack,
}

// Voters are everyone who may still vote (alive, not banished, not the accused); Votes hold the secret ballots.
public sealed record Confrontation(
    string Accused,
    int Gold,
    IReadOnlyList<string> Artifacts,
    IReadOnlyList<string> Spotters,
    IReadOnlyList<string> Voters,
    IReadOnlyDictionary<string, ConfrontationVote> Votes,
    DateTimeOffset Deadline);

public enum VoteStatus
{
    NoConfrontation,
    NotEligible,
    Recorded,
    Resolved,
}

public sealed record ConfrontationResolution(
    string Accused,
    ConfrontationVote Outcome,
    IReadOnlyDictionary<ConfrontationVote, int> Tally,
    int GoldToParty,
    int GoldKept,
    bool Banished,
    int Damage);

public sealed record VoteResult(VoteStatus Status, ConfrontationResolution? Resolution = null, int Cast = 0, int Required = 0);

public sealed record ConfrontationEffect(int GoldToParty, int GoldKept, bool Banish, int Damage, bool KeepsArtifact);

// Pure rules: majority/plurality with roller-broken ties, and what each verdict costs the accused.
public static class ConfrontationRules
{
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

// Passive perception when someone pockets loot: Rogue-like (Bard) and Ranger-like (Druid) classes notice far more.
public static class Perception
{
    public const int BasePercent = 15;
    public const int SpecialistBonus = 25;

    public static int ChancePercent(CharacterClass c) =>
        c is CharacterClass.Bard or CharacterClass.Druid ? BasePercent + SpecialistBonus : BasePercent;

    // One roll per observer, in roster order; the roll must land under the observer's chance.
    public static IReadOnlyList<string> Spotters(IEnumerable<PartyMember> observers, IRoller roller) =>
        [.. observers.Where(o => roller.Next(100) < ChancePercent(o.Class)).Select(o => o.Name)];
}

public enum TraitorOutcome
{
    None,
    TraitorWinsWipe,
    TraitorWinsSoloExtraction,
}

public sealed record TraitorState(
    int Floor, bool PartyWiped, bool TraitorAlive, int OthersAlive, bool TraitorHoldsIdol, bool Extracted);

// Traitor victory is tracked apart from the ordinary run: a wipe before floor 5, or a solo extraction carrying the Cursed Idol.
public static class TraitorPact
{
    public const string CursedIdol = "Cursed Idol";
    public const int WipeDeadlineFloor = 5;

    public static TraitorOutcome Evaluate(TraitorState s)
    {
        if (s.Extracted && s.TraitorAlive && s.TraitorHoldsIdol && s.OthersAlive == 0)
        {
            return TraitorOutcome.TraitorWinsSoloExtraction;
        }

        // A dead or banished traitor never wins the wipe: PartyWiped means every loyalist is down while the traitor still stands.
        return s.PartyWiped && s.TraitorAlive && s.Floor < WipeDeadlineFloor
            ? TraitorOutcome.TraitorWinsWipe
            : TraitorOutcome.None;
    }
}

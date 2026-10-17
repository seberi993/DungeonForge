
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

public enum TraitorOutcome
{
    None,
    TraitorWinsWipe,
    TraitorWinsSoloExtraction,
}

public sealed record TraitorState(
    int Floor, bool PartyWiped, bool TraitorAlive, int OthersAlive, bool TraitorHoldsIdol, bool Extracted);


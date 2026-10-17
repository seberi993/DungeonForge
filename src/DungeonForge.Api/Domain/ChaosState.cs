namespace DungeonForge.Api.Domain;

// The hidden and contested state of chaos mode: banishment, pockets, artifact holders, the unclaimed vault, an open
// confrontation and the traitor pact. Ephemeral cache state; none of it (pockets, ballots, traitor identity) ever reaches
// PartySnapshot. Part of a Party: no lock of its own, the caller holds the party's SyncRoot.
internal sealed class ChaosState
{
    public HashSet<string> Banished { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, PocketedLoot> Pockets { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> ArtifactHolders { get; } = new(StringComparer.OrdinalIgnoreCase);

    public UnclaimedVault? Vault { get; set; }

    public Confrontation? Confrontation { get; set; }

    public string? Traitor { get; set; }

    public string? ExtractedBy { get; set; }

    // The latched traitor victory; EvaluateTraitor reports each victory only once.
    public TraitorOutcome TraitorOutcome { get; set; }

    // Banished or extracted members are out of play without being defeated.
    public bool IsOut(string name) =>
        Banished.Contains(name) || string.Equals(ExtractedBy, name, StringComparison.OrdinalIgnoreCase);
}

using DungeonForge.Api.Features.Parties.ExploreDungeon;

namespace DungeonForge.Api.Domain;

// Contested loot, confrontations, banishment and the hidden traitor. All of it is ephemeral cache state, guarded by SyncRoot,
// and none of it (pockets, ballots, traitor identity) ever reaches PartySnapshot.
public sealed partial class Party
{
    private readonly HashSet<string> _banished = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PocketedLoot> _pockets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _artifactHolders = new(StringComparer.OrdinalIgnoreCase);
    private UnclaimedVault? _vault;
    private Confrontation? _confrontation;
    private string? _traitor;
    private string? _extractedBy;

    public PartyModifiers Modifiers { get; }

    public UnclaimedVault? Vault { get { lock (SyncRoot) { return _vault; } } }

    public Confrontation? Confrontation { get { lock (SyncRoot) { return _confrontation; } } }

    public string? TraitorName { get { lock (SyncRoot) { return _traitor; } } }

    public bool IsBanished(string name) { lock (SyncRoot) { return _banished.Contains(name); } }

    public int PocketedGold(string name) { lock (SyncRoot) { return _pockets.TryGetValue(name, out var p) ? p.Gold : 0; } }

    public string? ArtifactHolder(string artifact) { lock (SyncRoot) { return _artifactHolders.GetValueOrDefault(artifact); } }

    private PartyMember? Active(string name) =>
        Members.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) && !m.IsDefeated && !_banished.Contains(m.Name));

    // ---- Unclaimed Vault ----

    // Chaos parties do not bank drops; gold and artifacts wait here until someone claims them.
    public void DropVault(int gold, string? artifact)
    {
        if (gold <= 0 && artifact is null)
        {
            return;
        }

        lock (SyncRoot)
        {
            _vault = new UnclaimedVault((_vault?.Gold ?? 0) + Math.Max(0, gold), artifact ?? _vault?.Artifact);
        }
    }

    public VaultClaim ClaimVault(string claimant, VaultChoice choice, IRoller roller)
    {
        lock (SyncRoot)
        {
            if (_confrontation is not null)
            {
                return new VaultClaim(VaultClaimStatus.ConfrontationActive);
            }

            if (_vault is not { } vault)
            {
                return new VaultClaim(VaultClaimStatus.NoVault);
            }

            if (Active(claimant) is not { } member)
            {
                return new VaultClaim(VaultClaimStatus.UnknownPlayer);
            }

            _vault = null;
            if (vault.Artifact is { } artifact)
            {
                _artifactHolders[artifact] = member.Name;
            }

            if (choice == VaultChoice.Share)
            {
                _gold += vault.Gold;
                return new VaultClaim(VaultClaimStatus.Shared, vault.Gold, vault.Artifact);
            }

            _pockets[member.Name] = new PocketedLoot(PocketedGold(member.Name) + vault.Gold, vault.Artifact);
            var observers = Members.Where(m => !m.IsDefeated && !_banished.Contains(m.Name) && m != member).ToArray();
            var spotters = Perception.Spotters(observers, roller);
            if (spotters.Count == 0)
            {
                return new VaultClaim(VaultClaimStatus.PocketedHidden, vault.Gold, vault.Artifact);
            }

            _confrontation = new Confrontation(
                member.Name, vault.Gold, vault.Artifact, spotters, [.. observers.Select(o => o.Name)],
                new Dictionary<string, ConfrontationVote>(StringComparer.OrdinalIgnoreCase));
            return new VaultClaim(VaultClaimStatus.PocketedCaught, vault.Gold, vault.Artifact, spotters);
        }
    }

    // ---- Public Confrontation Phase ----

    public VoteResult CastVote(string voter, ConfrontationVote vote, IRoller roller)
    {
        lock (SyncRoot)
        {
            if (_confrontation is not { } c)
            {
                return new VoteResult(VoteStatus.NoConfrontation);
            }

            var eligible = c.Voters.FirstOrDefault(v => string.Equals(v, voter, StringComparison.OrdinalIgnoreCase));
            if (eligible is null || Active(eligible) is null)
            {
                return new VoteResult(VoteStatus.NotEligible);
            }

            var votes = new Dictionary<string, ConfrontationVote>(c.Votes, StringComparer.OrdinalIgnoreCase) { [eligible] = vote };
            var required = c.Voters.Where(v => Active(v) is not null).ToArray();
            if (!required.All(votes.ContainsKey))
            {
                _confrontation = c with { Votes = votes };
                return new VoteResult(VoteStatus.Recorded, null, required.Count(votes.ContainsKey), required.Length);
            }

            var ballots = required.Select(v => votes[v]).ToArray();
            return new VoteResult(VoteStatus.Resolved, Resolve(c, ballots, roller), ballots.Length, required.Length);
        }
    }

    private ConfrontationResolution Resolve(Confrontation c, ConfrontationVote[] ballots, IRoller roller)
    {
        var outcome = ConfrontationRules.Decide(ballots, roller);
        var effect = ConfrontationRules.Outcome(outcome, c.Gold);
        var accused = Members.First(m => m.Name == c.Accused);
        var pocket = PocketedGold(c.Accused);

        var toParty = Math.Min(effect.GoldToParty, pocket);
        if (effect.Banish)
        {
            toParty = pocket;
            _banished.Add(accused.Name);
        }

        _gold += toParty;
        _pockets[c.Accused] = new PocketedLoot(pocket - toParty, null);
        if (effect.Damage > 0 && !accused.IsDefeated)
        {
            accused.TakeDamage(effect.Damage);
        }

        if (!effect.KeepsArtifact && c.Artifact is { } artifact)
        {
            _artifactHolders.Remove(artifact);
        }

        _confrontation = null;
        return new ConfrontationResolution(
            c.Accused, outcome, ConfrontationRules.Tally(ballots), toParty, pocket - toParty, effect.Banish, effect.Damage);
    }

    // ---- Hidden Traitor Pact ----

    // Picks the traitor once, from the active members, as soon as there is someone to betray. False when already chosen.
    public bool TryAssignTraitor(IRoller roller)
    {
        lock (SyncRoot)
        {
            if (!Modifiers.TraitorPact || _traitor is not null || Members.Count < 2)
            {
                return false;
            }

            var pool = Members.Where(m => !m.IsDefeated && !_banished.Contains(m.Name)).ToArray();
            if (pool.Length == 0)
            {
                return false;
            }

            _traitor = pool[roller.Next(pool.Length) % pool.Length].Name;
            return true;
        }
    }

    public bool TryExtract(string name)
    {
        lock (SyncRoot)
        {
            if (Active(name) is not { } member)
            {
                return false;
            }

            _extractedBy = member.Name;
            return true;
        }
    }

    public TraitorOutcome EvaluateTraitor()
    {
        lock (SyncRoot)
        {
            if (!Modifiers.TraitorPact || _traitor is not { } traitor)
            {
                return TraitorOutcome.None;
            }

            return TraitorPact.Evaluate(new TraitorState(
                _floor,
                IsPartyDefeated,
                Active(traitor) is not null,
                Members.Count(m => m.Name != traitor && !m.IsDefeated && !_banished.Contains(m.Name)),
                string.Equals(ArtifactHolder(TraitorPact.CursedIdol), traitor, StringComparison.OrdinalIgnoreCase),
                string.Equals(_extractedBy, traitor, StringComparison.OrdinalIgnoreCase)));
        }
    }
}

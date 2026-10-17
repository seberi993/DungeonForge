using DungeonForge.Api.Domain.Rules;

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
    private TraitorOutcome _traitorOutcome;
    private readonly TimeProvider _time;

    public PartyModifiers Modifiers { get; }

    // The latched traitor victory (None until one is reached); EvaluateTraitor reports each victory only once.
    public TraitorOutcome TraitorResult { get { lock (SyncRoot) { return _traitorOutcome; } } }

    // Banished or extracted members are out of play without being defeated.
    private bool IsOut(string name) =>
        _banished.Contains(name) || string.Equals(_extractedBy, name, StringComparison.OrdinalIgnoreCase);

    // A member still in play: standing, and neither banished nor extracted. This is who can act, vote, be watched and hold the party up.
    private bool IsInPlay(PartyMember member) => !member.IsDefeated && !IsOut(member.Name);

    public UnclaimedVault? Vault { get { lock (SyncRoot) { return _vault; } } }

    public Confrontation? Confrontation { get { lock (SyncRoot) { return _confrontation; } } }

    public string? TraitorName { get { lock (SyncRoot) { return _traitor; } } }

    public bool IsBanished(string name) { lock (SyncRoot) { return _banished.Contains(name); } }

    public int PocketedGold(string name) { lock (SyncRoot) { return _pockets.TryGetValue(name, out var p) ? p.Gold : 0; } }

    public string? ArtifactHolder(string artifact) { lock (SyncRoot) { return _artifactHolders.GetValueOrDefault(artifact); } }

    private PartyMember? Active(string name) =>
        Members.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) && IsInPlay(m));

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
            IReadOnlyList<string> artifacts = artifact is null ? _vault?.Artifacts ?? [] : [.. _vault?.Artifacts ?? [], artifact];
            _vault = new UnclaimedVault((_vault?.Gold ?? 0) + Math.Max(0, gold), artifacts);
            SettleVaultIfWiped();
        }
    }

    // Policy: a wiped party can no longer claim, so unclaimed vault gold is banked into the purse (it counts for the score); artifacts are lost.
    private void SettleVaultIfWiped()
    {
        if (_vault is { } vault && IsPartyDefeated)
        {
            _gold += vault.Gold;
            _vault = null;
        }
    }

    public VaultClaim ClaimVault(string claimant, VaultChoice choice, IRoller roller)
    {
        lock (SyncRoot)
        {
            TryResolveConfrontation(roller);
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
            foreach (var artifact in vault.Artifacts)
            {
                _artifactHolders[artifact] = member.Name;
            }

            if (choice == VaultChoice.Share)
            {
                _gold += vault.Gold;
                return new VaultClaim(VaultClaimStatus.Shared, vault.Gold, vault.Artifact);
            }

            _pockets[member.Name] = new PocketedLoot(PocketedGold(member.Name) + vault.Gold, vault.Artifact);
            var observers = Members.Where(m => IsInPlay(m) && m != member).ToArray();
            var spotters = Perception.Spotters(observers, roller);
            if (spotters.Count == 0)
            {
                return new VaultClaim(VaultClaimStatus.PocketedHidden, vault.Gold, vault.Artifact);
            }

            _confrontation = new Confrontation(
                member.Name, vault.Gold, vault.Artifacts, spotters, [.. observers.Select(o => o.Name)],
                new Dictionary<string, ConfrontationVote>(StringComparer.OrdinalIgnoreCase),
                _time.GetUtcNow() + ConfrontationRules.Timeout);
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

            // Too late: the deadline passed, so the vote is not counted and the confrontation closes on the ballots already cast.
            if (TryResolveConfrontation(roller) is { } expired)
            {
                return new VoteResult(VoteStatus.Resolved, expired, expired.Tally.Values.Sum(), c.Voters.Count(v => Active(v) is not null));
            }

            var votes = new Dictionary<string, ConfrontationVote>(c.Votes, StringComparer.OrdinalIgnoreCase) { [eligible] = vote };
            _confrontation = c = c with { Votes = votes };
            var required = RequiredVoters(c);
            if (TryResolveConfrontation(roller) is { } resolution)
            {
                return new VoteResult(VoteStatus.Resolved, resolution, resolution.Tally.Values.Sum(), required.Length);
            }

            return new VoteResult(VoteStatus.Recorded, null, required.Count(votes.ContainsKey), required.Length);
        }
    }

    // Voters who can still be asked: alive, not banished and not extracted (defeated voters never hold the vote up).
    private string[] RequiredVoters(Confrontation c) => [.. c.Voters.Where(v => Active(v) is not null)];

    // Closes the confrontation when every remaining eligible voter has voted, nobody is left to ask, or the deadline passed.
    // Missing ballots abstain; no ballots at all is a Forgive no-op. Null while the vote is still open (or absent).
    public ConfrontationResolution? TryResolveConfrontation(IRoller roller)
    {
        lock (SyncRoot)
        {
            if (_confrontation is not { } c)
            {
                return null;
            }

            var required = RequiredVoters(c);
            var ballots = required.Where(c.Votes.ContainsKey).Select(v => c.Votes[v]).ToArray();
            var complete = ballots.Length == required.Length;
            return complete || _time.GetUtcNow() >= c.Deadline ? Resolve(c, ballots, roller) : null;
        }
    }

    private ConfrontationResolution Resolve(Confrontation c, ConfrontationVote[] ballots, IRoller roller)
    {
        var outcome = ballots.Length == 0 ? ConfrontationVote.Forgive : ConfrontationRules.Decide(ballots, roller);
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

        if (!effect.KeepsArtifact)
        {
            foreach (var artifact in c.Artifacts)
            {
                _artifactHolders.Remove(artifact);
            }
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

            var pool = Members.Where(IsInPlay).ToArray();
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
            // One-shot, and only for someone still in play; the extractor leaves the dungeon (see IsInPlay).
            if (_extractedBy is not null || Active(name) is not { } member)
            {
                return false;
            }

            _extractedBy = member.Name;
            SettleVaultIfWiped();
            return true;
        }
    }

    public TraitorOutcome EvaluateTraitor()
    {
        lock (SyncRoot)
        {
            // Latch: a victory is reported exactly once; later calls (votes, turns, extractions) return None.
            if (!Modifiers.TraitorPact || _traitor is not { } traitor || _traitorOutcome != TraitorOutcome.None)
            {
                return TraitorOutcome.None;
            }

            var traitorStanding = Members.Any(m => string.Equals(m.Name, traitor, StringComparison.OrdinalIgnoreCase)
                && !m.IsDefeated && !_banished.Contains(m.Name));
            var othersAlive = Members.Count(m => m.Name != traitor && IsInPlay(m));
            return _traitorOutcome = TraitorPact.Evaluate(new TraitorState(
                _floor,
                othersAlive == 0,
                traitorStanding,
                othersAlive,
                string.Equals(ArtifactHolder(TraitorPact.CursedIdol), traitor, StringComparison.OrdinalIgnoreCase),
                string.Equals(_extractedBy, traitor, StringComparison.OrdinalIgnoreCase)));
        }
    }
}

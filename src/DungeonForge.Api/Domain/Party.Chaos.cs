using DungeonForge.Api.Domain.Rules;

namespace DungeonForge.Api.Domain;

// Contested loot, confrontations and banishment (the traitor pact is in Party.Traitor.cs). All of it is ephemeral state in ChaosState, guarded by SyncRoot,
// and none of it (pockets, ballots, traitor identity) ever reaches PartySnapshot.
public sealed partial class Party
{
    private readonly ChaosState _chaos = new();
    private readonly TimeProvider _time;

    public PartyModifiers Modifiers { get; }

    // Banished or extracted members are out of play without being defeated.
    private bool IsOut(string name) => _chaos.IsOut(name);

    // A member still in play: standing, and neither banished nor extracted. This is who can act, vote, be watched and hold the party up.
    private bool IsInPlay(PartyMember member) => !member.IsDefeated && !IsOut(member.Name);

    public UnclaimedVault? Vault { get { lock (SyncRoot) { return _chaos.Vault; } } }

    public Confrontation? Confrontation { get { lock (SyncRoot) { return _chaos.Confrontation; } } }

    public bool IsBanished(string name) { lock (SyncRoot) { return _chaos.Banished.Contains(name); } }

    public int PocketedGold(string name) { lock (SyncRoot) { return _chaos.Pockets.TryGetValue(name, out var p) ? p.Gold : 0; } }

    public string? ArtifactHolder(string artifact) { lock (SyncRoot) { return _chaos.ArtifactHolders.GetValueOrDefault(artifact); } }

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
            IReadOnlyList<string> artifacts = artifact is null ? _chaos.Vault?.Artifacts ?? [] : [.. _chaos.Vault?.Artifacts ?? [], artifact];
            _chaos.Vault = new UnclaimedVault((_chaos.Vault?.Gold ?? 0) + Math.Max(0, gold), artifacts);
            SettleVaultIfWiped();
        }
    }

    // Policy: a wiped party can no longer claim, so unclaimed vault gold is banked into the purse (it counts for the score); artifacts are lost.
    private void SettleVaultIfWiped()
    {
        if (_chaos.Vault is { } vault && IsPartyDefeated)
        {
            _gold += vault.Gold;
            _chaos.Vault = null;
        }
    }

    public VaultClaim ClaimVault(string claimant, VaultChoice choice, IRoller roller)
    {
        lock (SyncRoot)
        {
            TryResolveConfrontation(roller);
            if (_chaos.Confrontation is not null)
            {
                return new VaultClaim(VaultClaimStatus.ConfrontationActive);
            }

            if (_chaos.Vault is not { } vault)
            {
                return new VaultClaim(VaultClaimStatus.NoVault);
            }

            if (Active(claimant) is not { } member)
            {
                return new VaultClaim(VaultClaimStatus.UnknownPlayer);
            }

            _chaos.Vault = null;
            foreach (var artifact in vault.Artifacts)
            {
                _chaos.ArtifactHolders[artifact] = member.Name;
            }

            if (choice == VaultChoice.Share)
            {
                _gold += vault.Gold;
                return new VaultClaim(VaultClaimStatus.Shared, vault.Gold, vault.Artifact);
            }

            _chaos.Pockets[member.Name] = new PocketedLoot(PocketedGold(member.Name) + vault.Gold, vault.Artifact);
            var observers = Members.Where(m => IsInPlay(m) && m != member).ToArray();
            var spotters = Perception.Spotters(observers, roller);
            if (spotters.Count == 0)
            {
                return new VaultClaim(VaultClaimStatus.PocketedHidden, vault.Gold, vault.Artifact);
            }

            _chaos.Confrontation = new Confrontation(
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
            if (_chaos.Confrontation is not { } c)
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
            _chaos.Confrontation = c = c with { Votes = votes };
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
            if (_chaos.Confrontation is not { } c)
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
            _chaos.Banished.Add(accused.Name);
        }

        _gold += toParty;
        _chaos.Pockets[c.Accused] = new PocketedLoot(pocket - toParty, null);
        if (effect.Damage > 0 && !accused.IsDefeated)
        {
            accused.TakeDamage(effect.Damage);
        }

        if (!effect.KeepsArtifact)
        {
            foreach (var artifact in c.Artifacts)
            {
                _chaos.ArtifactHolders.Remove(artifact);
            }
        }

        _chaos.Confrontation = null;
        return new ConfrontationResolution(
            c.Accused, outcome, ConfrontationRules.Tally(ballots), toParty, pocket - toParty, effect.Banish, effect.Damage);
    }

}

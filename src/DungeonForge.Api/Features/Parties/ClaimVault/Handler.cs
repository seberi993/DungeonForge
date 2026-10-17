using DungeonForge.Api.Domain;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.Realtime;
using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.ClaimVault;

// Resolves the claimant's choice in C#. Only a declared share (or a caught pocket) is announced; an unseen pocket is
// confirmed to the claimant privately and the public only learns that the vault is gone.
public sealed class ClaimVaultHandler(
    IMemoryCache cache, IRoller roller, IPartyNotifier notifier, ChaosDispatcher chaos, ILogger<ClaimVaultHandler> logger)
{
    public const string Operation = "Command:ClaimVault";

    public async Task<Result<ClaimVaultResponse>> HandleAsync(Guid id, string playerName, VaultChoice choice)
    {
        if (!cache.TryGetValue(PartyCache.Key(id), out Party? party) || party is null)
        {
            return PartyErrors.NotFound(Operation, id);
        }

        var name = playerName.Trim();
        var claim = party.ClaimVault(name, choice, roller);
        switch (claim.Status)
        {
            case VaultClaimStatus.NoVault:
                return ClaimVaultErrors.NoVault(Operation, id);
            case VaultClaimStatus.ConfrontationActive:
                return ClaimVaultErrors.ConfrontationActive(Operation, id);
            case VaultClaimStatus.UnknownPlayer:
                return PartyErrors.PlayerUnknown(Operation, id, name);
        }

        cache.Set(PartyCache.Key(id), party, PartyCache.EntryOptions());
        var who = party.Members.First(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)).Name;
        var loot = claim.Artifact is null ? $"{claim.Gold} gold" : $"{claim.Gold} gold and a {claim.Artifact}";

        string outcome;
        switch (claim.Status)
        {
            case VaultClaimStatus.Shared:
                outcome = "shared";
                await chaos.PublishAsync(id, new ChaosEvent(ChaosEvent.VaultClaimed, $"{who} declared the vault and shared {loot} with the party."));
                await BroadcastStateAsync(party);
                break;
            case VaultClaimStatus.PocketedHidden:
                outcome = "pocketed";
                await chaos.PublishAsync(id, new ChaosEvent(ChaosEvent.VaultClaimed, "The vault has been emptied. Nobody says a word."));
                await chaos.SendAsync(id, who, new PrivateMessage(PrivateKinds.Vault, $"You pocketed {loot} and nobody saw."));
                break;
            default:
                outcome = "caught";
                await chaos.PublishAsync(id, new ChaosEvent(
                    ChaosEvent.Confrontation,
                    $"{who} was caught pocketing {loot}, spotted by {string.Join(", ", claim.Spotters ?? [])}. " +
                    "Vote to forgive, demand a cut, banish or attack."));
                break;
        }

        return new ClaimVaultResponse(id, outcome, claim.Gold, claim.Artifact, claim.Status == VaultClaimStatus.PocketedCaught ? claim.Spotters : null);
    }

    private Task BroadcastStateAsync(Party party) => BestEffort.RunAsync(
        logger, Operation, party.Id, "PartyUpdated",
        () => notifier.PartyUpdatedAsync(PartyMapping.ToResponse(party.Snapshot()), CancellationToken.None));
}

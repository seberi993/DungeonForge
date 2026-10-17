using DungeonForge.Api.Domain;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.Realtime;

namespace DungeonForge.Api.Features.Parties.ClaimVault;

// Resolves the claimant's choice in C#. Only a declared share (or a caught pocket) is announced; an unseen pocket is
// confirmed to the claimant privately and the public only learns that the vault is gone.
public sealed class ClaimVaultHandler(
    IPartyStore parties, IRoller roller, IPartyNotifier notifier, ChaosDispatcher chaos, ILogger<ClaimVaultHandler> logger)
{
    public const string Operation = "Command:ClaimVault";

    public async Task<Result<ClaimVaultResponse>> HandleAsync(Guid id, string playerName, VaultChoice choice)
    {
        if (!parties.TryGet(id, out var party))
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

        parties.Save(party);
        var who = party.Members.First(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)).Name;
        var loot = ChaosText.Loot(claim.Gold, claim.Artifact);

        string outcome;
        switch (claim.Status)
        {
            case VaultClaimStatus.Shared:
                outcome = "shared";
                await chaos.PublishAsync(id, new ChaosEvent(ChaosEvent.VaultClaimed, ChaosText.VaultShared(who, loot)));
                await BroadcastStateAsync(party);
                break;
            case VaultClaimStatus.PocketedHidden:
                outcome = "pocketed";
                await chaos.PublishAsync(id, new ChaosEvent(ChaosEvent.VaultClaimed, ChaosText.VaultEmptied));
                await chaos.SendAsync(id, who, new PrivateMessage(PrivateKinds.Vault, ChaosText.PocketedUnseen(loot)));
                break;
            default:
                outcome = "caught";
                await chaos.PublishAsync(id, new ChaosEvent(
                    ChaosEvent.Confrontation, ChaosText.CaughtPocketing(who, loot, claim.Spotters ?? [])));
                break;
        }

        return new ClaimVaultResponse(id, outcome, claim.Gold, claim.Artifact, claim.Status == VaultClaimStatus.PocketedCaught ? claim.Spotters : null);
    }

    private Task BroadcastStateAsync(Party party) => BestEffort.RunAsync(
        logger, Operation, party.Id, "PartyUpdated",
        () => notifier.PartyUpdatedAsync(PartyMapping.ToResponse(party.Snapshot()), CancellationToken.None));
}

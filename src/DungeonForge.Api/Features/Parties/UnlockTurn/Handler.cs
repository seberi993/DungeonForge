using DungeonForge.Api.Domain;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.Realtime;

namespace DungeonForge.Api.Features.Parties.UnlockTurn;

// Command: releases one member's lock before the turn resolves. Idempotent; fails only when the party does not exist.
public sealed class UnlockTurnHandler(IPartyStore parties, IPartyNotifier notifier, ILogger<UnlockTurnHandler> logger)
{
    public const string Operation = "Command:UnlockTurn";

    public async Task<Result<UnlockTurnResponse>> HandleAsync(Guid id, Guid memberId)
    {
        if (!parties.TryGet(id, out var party))
        {
            return PartyErrors.NotFound(Operation, id);
        }

        bool removed;
        IReadOnlyList<LockStatusDto> status;
        lock (party.SyncRoot)
        {
            removed = party.Unlock(memberId);
            status = PartyMapping.ToLockDtos(party.LockStatus);
        }

        if (removed)
        {
            parties.Save(party);
            await BestEffort.RunAsync(
                logger, Operation, id, "TurnLockUpdated", () => notifier.TurnLockUpdatedAsync(id, status, CancellationToken.None));
        }

        return new UnlockTurnResponse(id, status);
    }
}

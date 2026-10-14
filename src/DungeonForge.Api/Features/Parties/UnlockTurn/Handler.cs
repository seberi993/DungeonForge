using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.Realtime;
using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.UnlockTurn;

// Releases one member's lock before the turn resolves. Idempotent; null when the party does not exist.
public sealed class UnlockTurnHandler(IMemoryCache cache, IPartyNotifier notifier, ILogger<UnlockTurnHandler> logger)
{
    public async Task<UnlockTurnResponse?> HandleAsync(Guid id, Guid memberId)
    {
        if (!cache.TryGetValue(PartyCache.Key(id), out Party? party) || party is null)
        {
            return null;
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
            cache.Set(PartyCache.Key(id), party, PartyCache.EntryOptions());
            try
            {
                await notifier.TurnLockUpdatedAsync(id, status, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to broadcast TurnLockUpdated for party {PartyId}", id);
            }
        }

        return new UnlockTurnResponse(id, status);
    }
}

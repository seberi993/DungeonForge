using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.Realtime;
using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.JoinParty;

// Adds the joiner's hero to an existing party and tells everyone already connected.
public sealed class JoinPartyHandler(IMemoryCache cache, IPartyNotifier notifier, ILogger<JoinPartyHandler> logger)
{
    public async Task<JoinPartyResult> HandleAsync(string? code, JoinPartyRequest request)
    {
        if (InviteCodes.Normalize(code) is not { } normalized
            || !cache.TryGetValue(PartyCache.InviteKey(normalized), out Guid id)
            || !cache.TryGetValue(PartyCache.Key(id), out Party? party)
            || party is null)
        {
            return new JoinPartyResult(JoinPartyStatus.NotFound);
        }

        // Request was validated by the endpoint filter.
        var name = request.Name!.Trim();
        GetPartyStateResponse state;
        lock (party.SyncRoot)
        {
            if (!party.TryAddMember(new PartyMember(name, request.Class!.Value)))
            {
                return new JoinPartyResult(JoinPartyStatus.Conflict);
            }

            state = PartyMapping.ToResponse(party.Snapshot());
        }

        cache.Set(PartyCache.Key(id), party, PartyCache.EntryOptions());
        try
        {
            await notifier.PartyUpdatedAsync(state, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast PartyUpdated for party {PartyId}", id);
        }

        return new JoinPartyResult(JoinPartyStatus.Joined, new JoinPartyResponse(name, state));
    }
}

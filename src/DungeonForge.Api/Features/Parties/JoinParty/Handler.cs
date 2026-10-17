using DungeonForge.Api.Domain;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.Realtime;
using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.JoinParty;

// Adds the joiner's hero to an existing party and tells everyone already connected.
public sealed class JoinPartyHandler(IMemoryCache cache, IPartyNotifier notifier, ILogger<JoinPartyHandler> logger)
{
    public const string Operation = "Command:JoinParty";

    public async Task<Result<JoinPartyResponse>> HandleAsync(string? code, JoinPartyRequest request)
    {
        if (InviteCodes.Normalize(code) is not { } normalized
            || !cache.TryGetValue(PartyCache.InviteKey(normalized), out Guid id)
            || !cache.TryGetValue(PartyCache.Key(id), out Party? party)
            || party is null)
        {
            return PartyErrors.InviteNotFound(Operation, code);
        }

        // Request was validated by the endpoint filter.
        var name = request.Name!.Trim();
        GetPartyStateResponse state;
        lock (party.SyncRoot)
        {
            if (!party.TryAddMember(new PartyMember(name, request.Class!.Value)))
            {
                // The domain only says "no"; both causes were a 409 before, and the roster size tells them apart.
                return party.Members.Count >= Party.MaxMembers
                    ? JoinPartyErrors.Full(Operation, id)
                    : JoinPartyErrors.NameTaken(Operation, id, name);
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

        return new JoinPartyResponse(name, state);
    }
}

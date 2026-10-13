using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.Realtime;

// TODO: Clerk JWT auth (and party membership checks on JoinParty) is a later slice.
public sealed class DungeonHub(IMemoryCache cache) : Hub
{
    public async Task JoinParty(Guid partyId)
    {
        if (!cache.TryGetValue(PartyCache.Key(partyId), out _))
        {
            throw new HubException("Party not found.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, partyId.ToString());
    }

    public Task LeaveParty(Guid partyId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, partyId.ToString());
}

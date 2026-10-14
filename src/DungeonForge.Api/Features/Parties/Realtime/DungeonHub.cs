using DungeonForge.Api.Features.Parties.Chaos;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using DungeonForge.Api.Domain;

namespace DungeonForge.Api.Features.Parties.Realtime;

// TODO: Clerk JWT auth (and party membership checks on JoinParty) is a later slice.
public sealed class DungeonHub(IMemoryCache cache, PlayerConnections connections, ChaosDispatcher chaos) : Hub
{
    public async Task JoinParty(Guid partyId)
    {
        if (!cache.TryGetValue(PartyCache.Key(partyId), out _))
        {
            throw new HubException("Party not found.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, partyId.ToString());
    }

    // Joins the party group AND binds this connection to a player, which is what private secrets/whispers are addressed to.
    public async Task RegisterPlayer(Guid partyId, string playerName)
    {
        if (!cache.TryGetValue(PartyCache.Key(partyId), out Party? party) || party is null)
        {
            throw new HubException("Party not found.");
        }

        var member = party.Members.FirstOrDefault(m => string.Equals(m.Name, playerName?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new HubException("Player not found.");

        await Groups.AddToGroupAsync(Context.ConnectionId, partyId.ToString());
        connections.Register(partyId, member.Name, Context.ConnectionId);
        await chaos.EnsureTraitorAsync(party, member.Name);
    }

    public Task LeaveParty(Guid partyId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, partyId.ToString());

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

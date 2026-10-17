using DungeonForge.Api.Domain;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.Chaos;
using Microsoft.AspNetCore.SignalR;

namespace DungeonForge.Api.Features.Parties.Realtime;

// TODO: Clerk JWT auth (and party membership checks on JoinParty) is a later slice.
// Refusals throw a HubException (its text is what the client sees, so it stays) and leave a coded event on the server.
public sealed class DungeonHub(
    IPartyStore parties, PlayerConnections connections, ChaosDispatcher chaos, ILogger<DungeonHub> logger) : Hub
{
    private const string JoinPartyOperation = "Hub:JoinParty";
    private const string RegisterPlayerOperation = "Hub:RegisterPlayer";

    public async Task JoinParty(Guid partyId)
    {
        if (!parties.TryGet(partyId, out _))
        {
            throw PartyNotFound(JoinPartyOperation, partyId);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, partyId.ToString());
    }

    // Joins the party group AND binds this connection to a player, which is what private secrets/whispers are addressed to.
    public async Task RegisterPlayer(Guid partyId, string playerName)
    {
        if (!parties.TryGet(partyId, out var party))
        {
            throw PartyNotFound(RegisterPlayerOperation, partyId);
        }

        var member = party.Members.FirstOrDefault(m => string.Equals(m.Name, playerName?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (member is null)
        {
            // The typed name is not logged: it is free text from a client.
            using var scope = LogScope.Begin(logger, RegisterPlayerOperation, partyId);
            logger.HubPlayerNotFound();
            throw new HubException("Player not found.");
        }

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

    private HubException PartyNotFound(string operation, Guid partyId)
    {
        using var scope = LogScope.Begin(logger, operation, partyId);
        logger.HubPartyNotFound();
        return new HubException("Party not found.");
    }
}

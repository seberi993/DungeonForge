using DungeonForge.Api.Features.Parties.ExploreDungeon;
using Microsoft.AspNetCore.SignalR;

namespace DungeonForge.Api.Features.Parties.Realtime;

public sealed class SignalRPartyNotifier(IHubContext<DungeonHub> hub) : IPartyNotifier
{
    public Task PartyUpdatedAsync(GetPartyStateResponse state, CancellationToken ct = default) =>
        hub.Clients.Group(state.PartyId.ToString()).SendAsync("PartyUpdated", state, ct);

    public Task TurnResolvedAsync(ExploreResponse turn, CancellationToken ct = default) =>
        hub.Clients.Group(turn.PartyId.ToString()).SendAsync("TurnResolved", turn, ct);
}

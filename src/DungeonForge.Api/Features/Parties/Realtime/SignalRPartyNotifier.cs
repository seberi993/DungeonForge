using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using Microsoft.AspNetCore.SignalR;

namespace DungeonForge.Api.Features.Parties.Realtime;

public sealed class SignalRPartyNotifier(IHubContext<DungeonHub> hub) : IPartyNotifier
{
    public Task PartyUpdatedAsync(GetPartyStateResponse state, CancellationToken ct = default) =>
        hub.Clients.Group(state.PartyId.ToString()).SendAsync("PartyUpdated", state, ct);

    public Task TurnResolvedAsync(ExploreResponse turn, CancellationToken ct = default) =>
        hub.Clients.Group(turn.PartyId.ToString()).SendAsync("TurnResolved", turn, ct);

    public Task NarrationReadyAsync(NarrationReady narration, CancellationToken ct = default) =>
        hub.Clients.Group(narration.PartyId.ToString()).SendAsync("NarrationReady", narration, ct);

    public Task DiscussionUpdatedAsync(Guid partyId, IReadOnlyList<PlayerAction> actions, CancellationToken ct = default) =>
        hub.Clients.Group(partyId.ToString()).SendAsync("DiscussionUpdated", partyId, actions, ct);

    public Task TurnLockUpdatedAsync(Guid partyId, IReadOnlyList<LockStatusDto> status, CancellationToken ct = default) =>
        hub.Clients.Group(partyId.ToString()).SendAsync("TurnLockUpdated", partyId, status, ct);
}

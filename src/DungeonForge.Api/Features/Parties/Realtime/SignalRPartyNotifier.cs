using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using Microsoft.AspNetCore.SignalR;

namespace DungeonForge.Api.Features.Parties.Realtime;

public sealed class SignalRPartyNotifier(IHubContext<DungeonHub> hub, PlayerConnections connections) : IPartyNotifier
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

    public Task ChaosEventAsync(Guid partyId, ChaosEvent chaosEvent, CancellationToken ct = default) =>
        hub.Clients.Group(partyId.ToString()).SendAsync("ChaosEvent", chaosEvent, ct);

    // Addressed by connection id, never by group: only the target's browser can receive it.
    public Task SendPrivateAsync(Guid partyId, string playerName, PrivateMessage message, CancellationToken ct = default) =>
        connections.TryGet(partyId, playerName, out var connectionId)
            ? hub.Clients.Client(connectionId).SendAsync("PrivateMessage", message, ct)
            : Task.CompletedTask;
}

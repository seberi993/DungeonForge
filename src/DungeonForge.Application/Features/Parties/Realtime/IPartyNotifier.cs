using DungeonForge.Domain;
using DungeonForge.Application.Features.Parties.Chaos;
using DungeonForge.Application.Features.Parties.ExploreDungeon;

namespace DungeonForge.Application.Features.Parties.Realtime;

public interface IPartyNotifier
{
    Task PartyUpdatedAsync(GetPartyStateResponse state, CancellationToken ct = default);

    Task TurnResolvedAsync(ExploreResponse turn, CancellationToken ct = default);

    // Phase two of a turn: the narration text, pushed once the (slow) LLM call completes.
    Task NarrationReadyAsync(NarrationReady narration, CancellationToken ct = default);

    // The pending encounter's submitted member actions changed.
    Task DiscussionUpdatedAsync(Guid partyId, IReadOnlyList<PlayerAction> actions, CancellationToken ct = default);

    // Who has locked in changed (lock, unlock, or cleared by the resolve).
    Task TurnLockUpdatedAsync(Guid partyId, IReadOnlyList<LockStatusDto> status, CancellationToken ct = default);

    // Public chaos-mode event (player ready, vault, confrontation, verdict, traitor reveal). Never carries hidden information.
    Task ChaosEventAsync(Guid partyId, ChaosEvent chaosEvent, CancellationToken ct = default) => Task.CompletedTask;

    // Delivered to ONE player's connection only (secrets, whispers, the traitor pact). Silently dropped when they are offline.
    Task SendPrivateAsync(Guid partyId, string playerName, PrivateMessage message, CancellationToken ct = default) => Task.CompletedTask;
}

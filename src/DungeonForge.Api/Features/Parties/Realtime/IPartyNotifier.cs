using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;

namespace DungeonForge.Api.Features.Parties.Realtime;

public interface IPartyNotifier
{
    Task PartyUpdatedAsync(GetPartyStateResponse state, CancellationToken ct = default);

    Task TurnResolvedAsync(ExploreResponse turn, CancellationToken ct = default);

    // Phase two of a turn: the narration text, pushed once the (slow) LLM call completes.
    Task NarrationReadyAsync(NarrationReady narration, CancellationToken ct = default);

    // The pending encounter's submitted member actions changed.
    Task DiscussionUpdatedAsync(Guid partyId, IReadOnlyList<PlayerAction> actions, CancellationToken ct = default);
}

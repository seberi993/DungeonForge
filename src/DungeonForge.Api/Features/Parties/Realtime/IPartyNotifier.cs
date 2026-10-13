using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.GetPartyState;

namespace DungeonForge.Api.Features.Parties.Realtime;

public interface IPartyNotifier
{
    Task PartyUpdatedAsync(GetPartyStateResponse state, CancellationToken ct = default);

    Task TurnResolvedAsync(ExploreResponse turn, CancellationToken ct = default);
}

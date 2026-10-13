using DungeonForge.Web.Models;

namespace DungeonForge.Web.Services;

public interface IDungeonHubClient : IAsyncDisposable
{
    event Action<ExploreResponse>? TurnResolved;
    event Action<PartyStateDto>? PartyUpdated;

    Task ConnectAsync(Guid partyId, CancellationToken ct = default);
}

using DungeonForge.Web.Models;

namespace DungeonForge.Web.Services;

public interface IDungeonHubClient : IAsyncDisposable
{
    event Action<ExploreResponse>? TurnResolved;
    event Action<PartyStateDto>? PartyUpdated;
    event Action<NarrationReady>? NarrationReady;
    event Action<Guid, IReadOnlyList<PlayerAction>>? DiscussionUpdated;
    event Action<Guid, IReadOnlyList<LockStatusDto>>? TurnLockUpdated;

    Task ConnectAsync(Guid partyId, CancellationToken ct = default);
}

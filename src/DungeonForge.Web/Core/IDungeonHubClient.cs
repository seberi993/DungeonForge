using DungeonForge.Web.Features.Dungeon;

namespace DungeonForge.Web.Core;

public interface IDungeonHubClient : IAsyncDisposable
{
    event Action<ExploreResponse>? TurnResolved;
    event Action<PartyStateDto>? PartyUpdated;
    event Action<NarrationReady>? NarrationReady;
    event Action<Guid, IReadOnlyList<PlayerAction>>? DiscussionUpdated;
    event Action<Guid, IReadOnlyList<LockStatusDto>>? TurnLockUpdated;
    event Action<PrivateMessageDto>? PrivateMessageReceived;
    event Action<ChaosEventDto>? ChaosEventReceived;

    // playerName binds this connection to a hero so private messages (whispers, secrets) reach this browser only.
    Task ConnectAsync(Guid partyId, string? playerName = null, CancellationToken ct = default);
}

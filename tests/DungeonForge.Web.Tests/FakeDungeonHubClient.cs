using DungeonForge.Web.Models;
using DungeonForge.Web.Services;

namespace DungeonForge.Web.Tests;

public sealed class FakeDungeonHubClient : IDungeonHubClient
{
    public bool FailConnect { get; init; }
    public Guid? ConnectedParty { get; private set; }
    public bool Disposed { get; private set; }

    public event Action<ExploreResponse>? TurnResolved;
    public event Action<PartyStateDto>? PartyUpdated;
    public event Action<NarrationReady>? NarrationReady;
    public event Action<Guid, IReadOnlyList<PlayerAction>>? DiscussionUpdated;

    public event Action<Guid, IReadOnlyList<LockStatusDto>>? TurnLockUpdated;
    public event Action<PrivateMessageDto>? PrivateMessageReceived;
    public event Action<ChaosEventDto>? ChaosEventReceived;

    public void RaisePrivate(PrivateMessageDto m) => PrivateMessageReceived?.Invoke(m);
    public void RaiseChaos(ChaosEventDto e) => ChaosEventReceived?.Invoke(e);

    public void RaiseLock(Guid id, IReadOnlyList<LockStatusDto> s) => TurnLockUpdated?.Invoke(id, s);

    public Task ConnectAsync(Guid partyId, string? playerName = null, CancellationToken ct = default)
    {
        if (FailConnect) throw new HttpRequestException("offline");
        ConnectedParty = partyId;
        return Task.CompletedTask;
    }

    public void RaiseTurn(ExploreResponse r) => TurnResolved?.Invoke(r);
    public void RaiseParty(PartyStateDto p) => PartyUpdated?.Invoke(p);
    public void RaiseNarration(NarrationReady n) => NarrationReady?.Invoke(n);
    public void RaiseDiscussion(Guid id, IReadOnlyList<PlayerAction> a) => DiscussionUpdated?.Invoke(id, a);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

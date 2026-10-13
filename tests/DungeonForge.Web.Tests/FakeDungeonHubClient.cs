using DungeonForge.Web.Models;
using DungeonForge.Web.Services;

namespace DungeonForge.Web.Tests;

internal sealed class FakeDungeonHubClient : IDungeonHubClient
{
    public bool FailConnect { get; init; }
    public Guid? ConnectedParty { get; private set; }
    public bool Disposed { get; private set; }

    public event Action<ExploreResponse>? TurnResolved;
    public event Action<PartyStateDto>? PartyUpdated;

    public Task ConnectAsync(Guid partyId, CancellationToken ct = default)
    {
        if (FailConnect) throw new HttpRequestException("offline");
        ConnectedParty = partyId;
        return Task.CompletedTask;
    }

    public void RaiseTurn(ExploreResponse r) => TurnResolved?.Invoke(r);
    public void RaiseParty(PartyStateDto p) => PartyUpdated?.Invoke(p);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

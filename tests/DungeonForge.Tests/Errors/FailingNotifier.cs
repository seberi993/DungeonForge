using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.Realtime;

namespace DungeonForge.Tests.Errors;

// A hub that is down: every broadcast throws. Chaos sends only fail when asked, so tests can aim at either path.
internal sealed class FailingNotifier(bool failChaos = false) : IPartyNotifier
{
    private static Task Fail() => throw new InvalidOperationException("hub is down");

    public Task PartyUpdatedAsync(GetPartyStateResponse state, CancellationToken ct = default) => Fail();

    public Task TurnResolvedAsync(ExploreResponse turn, CancellationToken ct = default) => Fail();

    public Task NarrationReadyAsync(NarrationReady narration, CancellationToken ct = default) => Fail();

    public Task DiscussionUpdatedAsync(Guid partyId, IReadOnlyList<PlayerAction> actions, CancellationToken ct = default) => Fail();

    public Task TurnLockUpdatedAsync(Guid partyId, IReadOnlyList<LockStatusDto> status, CancellationToken ct = default) => Fail();

    public Task ChaosEventAsync(Guid partyId, ChaosEvent chaosEvent, CancellationToken ct = default) =>
        failChaos ? Fail() : Task.CompletedTask;

    public Task SendPrivateAsync(Guid partyId, string playerName, PrivateMessage message, CancellationToken ct = default) =>
        failChaos ? Fail() : Task.CompletedTask;
}

using DungeonForge.Api.Domain;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Infrastructure.Narration;
using DungeonForge.Tests.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Parties;

public sealed class ExploreDungeonBroadcastTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private sealed class FixedNarrator : IDungeonNarrator
    {
        public Task<string> GenerateScenarioAsync(NarrationContext ctx, CancellationToken ct) =>
            Task.FromResult("scenario");

        public Task<TurnNarration> NarrateTurnAsync(bool success, NarrationContext ctx, CancellationToken ct) =>
            Task.FromResult(new TurnNarration("outcome", "next"));
    }

    private sealed class RecordingNotifier(bool failTurnResolved = false) : IPartyNotifier
    {
        public int PartyUpdatedCalls { get; private set; }
        public int TurnResolvedCalls { get; private set; }
        public List<CancellationToken> Tokens { get; } = [];

        public Task PartyUpdatedAsync(GetPartyStateResponse state, CancellationToken ct = default)
        {
            PartyUpdatedCalls++;
            Tokens.Add(ct);
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task TurnResolvedAsync(ExploreResponse turn, CancellationToken ct = default)
        {
            TurnResolvedCalls++;
            Tokens.Add(ct);
            ct.ThrowIfCancellationRequested();
            return failTurnResolved ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        }

        public Task NarrationReadyAsync(NarrationReady narration, CancellationToken ct = default) => Task.CompletedTask;

        public Task DiscussionUpdatedAsync(Guid partyId, IReadOnlyList<PlayerAction> actions, CancellationToken ct = default) => Task.CompletedTask;

        public Task TurnLockUpdatedAsync(Guid partyId, IReadOnlyList<LockStatusDto> status, CancellationToken ct = default) => Task.CompletedTask;
    }

    private async Task<(ExploreDungeonHandler Handler, Guid Id, IDisposable Scope)> ArrangeAsync(RecordingNotifier notifier)
    {
        var derived = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IRoller>(new FixedRoller(0))));
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);
        var handler = PartyTestBuilders.NewHandler(
            derived.Services.GetRequiredService<IPartyStore>(), new FixedRoller(0), new FixedNarrator(), notifier);
        return (handler, id, derived);
    }

    [Fact]
    public async Task Prompt_DoesNotBroadcastTurnResolved()
    {
        var notifier = new RecordingNotifier();
        var (handler, id, scope) = await ArrangeAsync(notifier);
        using var _ = scope;

        var result = await handler.HandleAsync(id, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Response.Scenario.Should().Be("scenario");
        notifier.TurnResolvedCalls.Should().Be(0);
    }

    [Fact]
    public async Task Resolve_TurnResolvedThrows_StillDeliversPartyUpdated()
    {
        var notifier = new RecordingNotifier(failTurnResolved: true);
        var (handler, id, scope) = await ArrangeAsync(notifier);
        using var _ = scope;
        await handler.HandleAsync(id, null);

        var result = await handler.HandleAsync(id, new ExploreRequest(PartyTestBuilders.WholeParty()));

        result.IsSuccess.Should().BeTrue();
        notifier.TurnResolvedCalls.Should().Be(1);
        notifier.PartyUpdatedCalls.Should().Be(1);
    }

    [Fact]
    public async Task Resolve_RequestTokenCancelled_BroadcastIsNotCancelled()
    {
        var notifier = new RecordingNotifier();
        var (handler, id, scope) = await ArrangeAsync(notifier);
        using var _ = scope;
        await handler.HandleAsync(id, null);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await handler.HandleAsync(id, new ExploreRequest(PartyTestBuilders.WholeParty()), cts.Token);

        notifier.TurnResolvedCalls.Should().Be(1);
        notifier.PartyUpdatedCalls.Should().Be(1);
        notifier.Tokens.Should().OnlyContain(t => !t.IsCancellationRequested);
    }

    [Fact]
    [CoversError(ErrorCodes.Turn.NoActions)]
    public async Task Resolve_OnlyControlCharacterAction_BroadcastsNothing()
    {
        var notifier = new RecordingNotifier();
        var (handler, id, scope) = await ArrangeAsync(notifier);
        using var _ = scope;
        await handler.HandleAsync(id, null);

        var result = await handler.HandleAsync(id, new ExploreRequest([PartyTestBuilders.Act("\u0007")]));

        result.Error!.Code.Should().Be(ErrorCodes.Turn.NoActions);
        result.Error.Operation.Should().Be(ExploreOperations.ResolveTurn);
        notifier.TurnResolvedCalls.Should().Be(0);
        notifier.PartyUpdatedCalls.Should().Be(0);
    }
}

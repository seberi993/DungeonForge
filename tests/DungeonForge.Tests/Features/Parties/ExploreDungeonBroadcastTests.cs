using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.GetPartyState;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Infrastructure.Narration;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DungeonForge.Tests.Features.Parties;

public sealed class ExploreDungeonBroadcastTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private sealed class FixedRoller(int value) : IRoller
    {
        public int Next(int max) => value;
    }

    private sealed class FixedNarrator : IDungeonNarrator
    {
        public Task<string> NarrateAsync(NarrationContext ctx, CancellationToken ct) => Task.FromResult("flavor");
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
    }

    private async Task<(ExploreDungeonHandler Handler, Guid Id, IDisposable Scope)> ArrangeAsync(RecordingNotifier notifier)
    {
        var derived = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IRoller>(new FixedRoller(0))));
        var client = derived.CreateClient();
        var created = await client.PostAsJsonAsync("/api/parties", new { name = "Heroes" });
        using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("partyId").GetGuid();
        var handler = new ExploreDungeonHandler(
            derived.Services.GetRequiredService<IMemoryCache>(),
            new FixedRoller(0),
            new FixedNarrator(),
            notifier,
            NullLogger<ExploreDungeonHandler>.Instance);
        return (handler, id, derived);
    }

    [Fact]
    public async Task Explore_TurnResolvedThrows_StillDeliversPartyUpdated()
    {
        var notifier = new RecordingNotifier(failTurnResolved: true);
        var (handler, id, scope) = await ArrangeAsync(notifier);
        using var _ = scope;

        var result = await handler.HandleAsync(id);

        result.Status.Should().Be(ExploreStatus.Explored);
        notifier.TurnResolvedCalls.Should().Be(1);
        notifier.PartyUpdatedCalls.Should().Be(1);
    }

    [Fact]
    public async Task Explore_RequestTokenCancelled_BroadcastIsNotCancelled()
    {
        var notifier = new RecordingNotifier();
        var (handler, id, scope) = await ArrangeAsync(notifier);
        using var _ = scope;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await handler.HandleAsync(id, cts.Token);

        notifier.TurnResolvedCalls.Should().Be(1);
        notifier.PartyUpdatedCalls.Should().Be(1);
        notifier.Tokens.Should().OnlyContain(t => !t.IsCancellationRequested);
    }
}

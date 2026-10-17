using System.Net;
using System.Text.Json;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Infrastructure.Narration;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static DungeonForge.Tests.Features.Parties.ExploreTestSupport;

namespace DungeonForge.Tests.Features.Parties;

public sealed class ExploreDungeonNarrationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    internal sealed class FakeNarrator(
        string scenario = "A door looms. What do you do?",
        TurnNarration? turn = null,
        bool throwOnScenario = false,
        bool throwOnTurn = false) : IDungeonNarrator
    {
        private int _scenarioCalls;
        private int _turnCalls;

        public TaskCompletionSource? TurnGate { get; init; }
        public int ScenarioCalls => Volatile.Read(ref _scenarioCalls);
        public int TurnCalls => Volatile.Read(ref _turnCalls);
        public int TotalCalls => ScenarioCalls + TurnCalls;
        public NarrationContext? SeenScenarioContext { get; private set; }
        public NarrationContext? SeenTurnContext { get; private set; }
        public bool? SeenSuccess { get; private set; }

        public Task<string> GenerateScenarioAsync(NarrationContext ctx, CancellationToken ct)
        {
            Interlocked.Increment(ref _scenarioCalls);
            SeenScenarioContext = ctx;
            return throwOnScenario ? throw new InvalidOperationException("boom") : Task.FromResult(scenario);
        }

        public async Task<TurnNarration> NarrateTurnAsync(bool success, NarrationContext ctx, CancellationToken ct)
        {
            Interlocked.Increment(ref _turnCalls);
            SeenSuccess = success;
            SeenTurnContext = ctx;
            if (TurnGate is not null)
            {
                await TurnGate.Task;
            }

            return throwOnTurn
                ? throw new InvalidOperationException("boom")
                : turn ?? new TurnNarration("You prevail.", "The tunnel opens into a cavern. What do you do?");
        }
    }

    private WebApplicationFactory<Program> With(
        IDungeonNarrator narrator, IRoller? roller = null, IPartyNotifier? notifier = null) =>
        factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            if (notifier is not null) s.AddSingleton(notifier);
            s.AddSingleton(roller ?? new FixedRoller(0, Succeed));
            s.AddSingleton(narrator);
        }));

    // ---- Prompt step: at most one call, and none when a scenario is already stored ----

    [Fact]
    public async Task FirstPrompt_GeneratesScenarioWithExactlyOneCall_AndPassesFactsOnly()
    {
        var narrator = new FakeNarrator("The dark hums. What do you do?");
        using var derived = With(narrator);
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await ReadAsync(await PromptAsync(client, id));

        doc.RootElement.GetProperty("scenario").GetString().Should().Be("The dark hums. What do you do?");
        narrator.ScenarioCalls.Should().Be(1);
        narrator.TurnCalls.Should().Be(0);
        narrator.SeenScenarioContext!.Room.Should().Be(RoomType.Combat);
        narrator.SeenScenarioContext.PartyName.Should().Be("Heroes");
    }

    [Fact]
    public async Task PromptWithStoredScenario_MakesNoNarratorCall()
    {
        var narrator = new FakeNarrator();
        using var derived = With(narrator);
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();

        for (var i = 0; i < 5; i++)
        {
            (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        }

        narrator.TotalCalls.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentFirstPrompts_MakeExactlyOneNarratorCall()
    {
        var narrator = new FakeNarrator();
        using var derived = With(narrator);
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PromptAsync(client, id)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        narrator.ScenarioCalls.Should().Be(1);
    }

    [Fact]
    public async Task FirstPrompt_NarratorThrows_FallsBackToDeterministicScenario()
    {
        using var derived = With(new FakeNarrator(throwOnScenario: true));
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await ReadAsync(await PromptAsync(client, id));

        doc.RootElement.GetProperty("scenario").GetString().Should().Be(FallbackFlavor.Scenario(RoomType.Combat, 0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FirstPrompt_NarratorReturnsBlank_FallsBack(string blank)
    {
        using var derived = With(new FakeNarrator(blank), new FixedRoller(2));
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await ReadAsync(await PromptAsync(client, id));

        doc.RootElement.GetProperty("scenario").GetString().Should().Be(FallbackFlavor.Scenario(RoomType.Puzzle, 0));
    }

    // ---- Resolve step: ONE call, next room rolled first, scenario stored for the next prompt ----

    [Fact]
    public async Task Resolve_MakesExactlyOneNarratorCall_AndNextPromptMakesNone()
    {
        var notifier = new CapturingNotifier();
        var narrator = new FakeNarrator();
        using var derived = With(narrator, notifier: notifier);
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        narrator.ScenarioCalls.Should().Be(1);

        (await ResolveAsync(client, id)).EnsureSuccessStatusCode();
        var ready = await notifier.NarrationAsync;

        narrator.TurnCalls.Should().Be(1);
        narrator.ScenarioCalls.Should().Be(1);
        ready.Outcome.Should().Be("You prevail.");
        ready.NextScenario.Should().Be("The tunnel opens into a cavern. What do you do?");

        using var next = await ReadAsync(await PromptAsync(client, id));
        next.RootElement.GetProperty("scenario").GetString().Should().Be(ready.NextScenario);
        next.RootElement.GetProperty("scenarioPending").GetBoolean().Should().BeFalse();
        next.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
        narrator.TotalCalls.Should().Be(2);
    }

    [Fact]
    public async Task Resolve_RollsNextRoomInCSharpBeforeTheNarratorCall_AndNarrationReadyCarriesIt()
    {
        var notifier = new CapturingNotifier();
        var narrator = new FakeNarrator();
        using var derived = With(narrator, new SequenceRoller(Succeed, 0, 2), notifier);
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();

        (await ResolveAsync(client, id, Act("flank them", "Conan"), Act("cast a light", "Merlin", Guid.NewGuid()), Act("hold the line", "Mercy", Guid.NewGuid()))).EnsureSuccessStatusCode();
        var ready = await notifier.NarrationAsync;

        narrator.SeenTurnContext!.Room.Should().Be(RoomType.Combat);
        narrator.SeenTurnContext.NextRoom.Should().Be(RoomType.Puzzle);
        narrator.SeenTurnContext.Actions.Select(a => a.Text).Should().Equal("flank them", "cast a light", "hold the line");
        narrator.SeenSuccess.Should().BeTrue();
        ready.PartyId.Should().Be(id);
        ready.Turn.Should().Be(1);
        ready.NextRoomType.Should().Be(RoomType.Puzzle);

        using var next = await ReadAsync(await PromptAsync(client, id));
        next.RootElement.GetProperty("roomType").GetString().Should().Be("puzzle");
    }

    [Fact]
    public async Task Resolve_FailedRoll_PassesSuccessFalseToNarrator()
    {
        var notifier = new CapturingNotifier();
        var narrator = new FakeNarrator();
        using var derived = With(narrator, new FixedRoller(0, Fail), notifier);
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await TurnAsync(client, id);
        await notifier.NarrationAsync;

        narrator.SeenSuccess.Should().BeFalse();
        doc.RootElement.GetProperty("damage").GetInt32().Should().Be(6);
        narrator.SeenTurnContext!.Failed.Should().Equal("Conan", "Merlin", "Mercy");
        doc.RootElement.TryGetProperty("lastOutcome", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Resolve_NarratorThrows_BothPartsFallBack_AndTurnStillResolves()
    {
        var notifier = new CapturingNotifier();
        using var derived = With(new FakeNarrator(throwOnTurn: true), new FixedRoller(1, Fail), notifier);
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await TurnAsync(client, id);
        var ready = await notifier.NarrationAsync;

        doc.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
        ready.Outcome.Should().EndWith(FallbackFlavor.Outcome(RoomType.Chest, false, 0));
        ready.NextScenario.Should().Be(FallbackFlavor.Scenario(RoomType.Chest, 1));
        using var next = await PromptWhenReadyAsync(client, id);
        next.RootElement.GetProperty("scenario").GetString().Should().Be(ready.NextScenario);
    }

    [Fact]
    public async Task Resolve_BlankOutcome_OnlyOutcomeFallsBack_NextIsKept()
    {
        var notifier = new CapturingNotifier();
        using var derived = With(new FakeNarrator(turn: new TurnNarration("  ", "A new hall. What do you do?")), new FixedRoller(1), notifier);
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        using var _ = await TurnAsync(client, id);
        var ready = await notifier.NarrationAsync;

        ready.Outcome.Should().EndWith(FallbackFlavor.Outcome(RoomType.Chest, true, 0));
        ready.NextScenario.Should().Be("A new hall. What do you do?");
    }

    [Fact]
    public async Task Resolve_BlankNext_OnlyNextFallsBack_OutcomeIsKept()
    {
        var notifier = new CapturingNotifier();
        using var derived = With(new FakeNarrator(turn: new TurnNarration("Steel rings.", "")), new FixedRoller(2), notifier);
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        using var _ = await TurnAsync(client, id);
        var ready = await notifier.NarrationAsync;

        ready.Outcome.Should().Be("Steel rings.");
        ready.NextScenario.Should().Be(FallbackFlavor.Scenario(RoomType.Puzzle, 1));
    }

    // ---- In-flight window: no second call, no double resolve ----

    [Fact]
    public async Task WhileNarrationInFlight_PromptReturnsScenarioPending_WithoutCalling_AndResolveIsRejected()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifier = new CapturingNotifier();
        var narrator = new FakeNarrator { TurnGate = gate };
        using var derived = With(narrator, new FixedRoller(1), notifier);
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        (await ResolveAsync(client, id)).EnsureSuccessStatusCode();

        using var pending = await ReadAsync(await PromptAsync(client, id));
        var again = await ResolveAsync(client, id);

        pending.RootElement.GetProperty("scenarioPending").GetBoolean().Should().BeTrue();
        pending.RootElement.GetProperty("roomType").GetString().Should().Be("chest");
        pending.RootElement.TryGetProperty("scenario", out _).Should().BeFalse();
        pending.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        narrator.TotalCalls.Should().Be(2, "one scenario call plus the single in-flight turn call");

        gate.SetResult();
        var ready = await notifier.NarrationAsync;
        using var next = await PromptWhenReadyAsync(client, id);
        next.RootElement.GetProperty("scenario").GetString().Should().Be(ready.NextScenario);
        narrator.TotalCalls.Should().Be(2);
    }

    [Fact]
    public async Task ConcurrentResolves_ResolveExactlyOnce_WithExactlyOneTurnCall()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var narrator = new FakeNarrator { TurnGate = gate };
        using var derived = With(narrator, new FixedRoller(1));
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => ResolveAsync(client, id)));
        gate.SetResult();

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest).Should().Be(19);
        narrator.TurnCalls.Should().Be(1);
        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
    }

    // ---- Defeat: still one call, next scenario ignored ----

    [Fact]
    public async Task PartyDefeated_NarratesWithOneCall_AndStoresNoNextScenario()
    {
        var narrator = new FakeNarrator();
        using var derived = With(narrator, new FixedRoller(0, Fail));
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client, members: [new { name = "Merlin", @class = "mage" }]);
        for (var i = 0; i < 2; i++)
        {
            using var _ = await TurnAsync(client, id);
        }

        var before = narrator.TurnCalls;
        using var final = await TurnAsync(client, id); // mage (16 HP) falls on the third failure

        final.RootElement.GetProperty("isPartyDefeated").GetBoolean().Should().BeTrue();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (narrator.TurnCalls == before && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        narrator.TurnCalls.Should().Be(before + 1);
        narrator.SeenTurnContext!.NextRoom.Should().BeNull();
        (await PromptAsync(client, id)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---- Failures downstream of narration never break the next turn ----

    [Fact]
    public async Task NarrationReadyBroadcastFailure_StillStoresNextScenario()
    {
        var narrator = new FakeNarrator();
        using var derived = With(narrator, new FixedRoller(1), new ThrowingNarrationNotifier());
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        using var _ = await TurnAsync(client, id);
        using var next = await PromptWhenReadyAsync(client, id);

        next.RootElement.GetProperty("scenario").GetString().Should().Be("The tunnel opens into a cavern. What do you do?");
    }

    [Fact]
    public async Task Handler_PromptRequestCancelled_PropagatesCancellation_AndDoesNotWedgeTheParty()
    {
        using var derived = With(new FakeNarrator());
        var id = await PartyTestData.CreatePartyAsync(derived.CreateClient());
        var cancelling = new CancellingNarrator();
        var handler = NewHandler(
            derived.Services.GetRequiredService<IPartyStore>(), new FixedRoller(0), cancelling, new NoopNotifier());

        var act = () => handler.HandleAsync(id, null);
        await act.Should().ThrowAsync<OperationCanceledException>();

        cancelling.Cancel = false;
        var retry = await handler.HandleAsync(id, null);
        retry.IsSuccess.Should().BeTrue();
        retry.Value.Response.Scenario.Should().Be("Calm now. What do you do?");
    }

    [Fact]
    public async Task NarrationPhaseFault_CompletesWithFallback_AndPartyStaysPromptable()
    {
        using var derived = With(new FakeNarrator());
        var id = await PartyTestData.CreatePartyAsync(derived.CreateClient());
        var cache = new FaultableCache(derived.Services.GetRequiredService<IMemoryCache>());
        var notifier = new CapturingNotifier();
        var handler = NewHandler(new PartyStore(cache), new FixedRoller(0, Succeed), new FaultArmingNarrator(cache), notifier);

        (await handler.HandleAsync(id, null)).IsSuccess.Should().BeTrue();
        var resolved = await handler.HandleAsync(id, new ExploreRequest(WholeParty()));
        resolved.IsSuccess.Should().BeTrue();

        var ready = await notifier.NarrationAsync;
        ready.NextScenario.Should().NotBeNullOrWhiteSpace();

        cache.FailSets = false;
        var next = await handler.HandleAsync(id, null);
        next.Value.Response.ScenarioPending.Should().BeFalse();
        next.Value.Response.Scenario.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task FirstTurnCompletion_BroadcastsNarrationReadyWithScenario()
    {
        var notifier = new CapturingNotifier();
        using var derived = With(new FakeNarrator("The dark hums. What do you do?"), notifier: notifier);
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await ReadAsync(await PromptAsync(client, id));

        var ready = await notifier.FirstScenarioAsync;
        ready.PartyId.Should().Be(id);
        ready.Outcome.Should().BeEmpty();
        ready.NextScenario.Should().Be("The dark hums. What do you do?");
        ready.NextRoomType.Should().Be(RoomType.Combat);
    }

    [Fact]
    public async Task FirstTurnCompletion_BroadcastFailure_DoesNotFailTheRequest()
    {
        using var derived = With(new FakeNarrator(), notifier: new ThrowingNarrationNotifier());
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        (await PromptAsync(client, id)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    internal sealed class FaultArmingNarrator(FaultableCache cache) : IDungeonNarrator
    {
        public Task<string> GenerateScenarioAsync(NarrationContext ctx, CancellationToken ct) =>
            Task.FromResult("A door looms. What do you do?");

        public Task<TurnNarration> NarrateTurnAsync(bool success, NarrationContext ctx, CancellationToken ct)
        {
            cache.FailSets = true;
            return Task.FromResult(new TurnNarration("You prevail.", "Onward. What do you do?"));
        }
    }

    internal sealed class FaultableCache(IMemoryCache inner) : IMemoryCache
    {
        public volatile bool FailSets;

        public ICacheEntry CreateEntry(object key) =>
            FailSets ? throw new InvalidOperationException("cache fault") : inner.CreateEntry(key);

        public void Remove(object key) => inner.Remove(key);
        public bool TryGetValue(object key, out object? value) => inner.TryGetValue(key, out value);
        public void Dispose() { }
    }

    private sealed class CancellingNarrator : IDungeonNarrator
    {
        public bool Cancel { get; set; } = true;

        public Task<string> GenerateScenarioAsync(NarrationContext ctx, CancellationToken ct) =>
            Cancel ? throw new OperationCanceledException() : Task.FromResult("Calm now. What do you do?");

        public Task<TurnNarration> NarrateTurnAsync(bool success, NarrationContext ctx, CancellationToken ct) =>
            throw new OperationCanceledException();
    }

    private sealed class ThrowingNarrationNotifier : IPartyNotifier
    {
        public Task PartyUpdatedAsync(GetPartyStateResponse state, CancellationToken ct = default) => Task.CompletedTask;
        public Task TurnResolvedAsync(ExploreResponse turn, CancellationToken ct = default) => Task.CompletedTask;
        public Task NarrationReadyAsync(NarrationReady narration, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
        public Task DiscussionUpdatedAsync(Guid partyId, IReadOnlyList<PlayerAction> actions, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task TurnLockUpdatedAsync(Guid partyId, IReadOnlyList<LockStatusDto> status, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopNotifier : IPartyNotifier
    {
        public Task TurnLockUpdatedAsync(Guid partyId, IReadOnlyList<LockStatusDto> status, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task PartyUpdatedAsync(GetPartyStateResponse state, CancellationToken ct = default) => Task.CompletedTask;
        public Task TurnResolvedAsync(ExploreResponse turn, CancellationToken ct = default) => Task.CompletedTask;
        public Task NarrationReadyAsync(NarrationReady narration, CancellationToken ct = default) => Task.CompletedTask;
        public Task DiscussionUpdatedAsync(Guid partyId, IReadOnlyList<PlayerAction> actions, CancellationToken ct = default) => Task.CompletedTask;
    }
}

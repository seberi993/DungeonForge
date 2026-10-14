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
    private sealed class FakeNarrator(
        EncounterChoice? choices = null,
        string outcome = "You prevail.",
        bool throwOnChoices = false,
        bool throwOnOutcome = false) : IDungeonNarrator
    {
        public NarrationContext? SeenChoicesContext { get; private set; }
        public NarrationContext? SeenOutcomeContext { get; private set; }
        public string? SeenOption { get; private set; }
        public bool? SeenSuccess { get; private set; }

        public Task<EncounterChoice> GenerateChoicesAsync(NarrationContext ctx, CancellationToken ct)
        {
            SeenChoicesContext = ctx;
            return throwOnChoices
                ? throw new InvalidOperationException("boom")
                : Task.FromResult(choices ?? new EncounterChoice("A door looms.", ["Open it", "Kick it", "Leave"]));
        }

        public Task<string> NarrateOutcomeAsync(string chosenOption, bool success, NarrationContext ctx, CancellationToken ct)
        {
            SeenOption = chosenOption;
            SeenSuccess = success;
            SeenOutcomeContext = ctx;
            return throwOnOutcome ? throw new InvalidOperationException("boom") : Task.FromResult(outcome);
        }
    }

    private WebApplicationFactory<Program> With(FakeNarrator narrator, int room = 0, int success = Succeed, CapturingNotifier? notifier = null) =>
        factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            if (notifier is not null) s.AddSingleton<IPartyNotifier>(notifier);
            s.AddSingleton<IRoller>(new FixedRoller(room, success));
            s.AddSingleton<IDungeonNarrator>(narrator);
        }));

    [Fact]
    public async Task Prompt_UsesNarratorChoices_AndPassesFactsOnly()
    {
        var narrator = new FakeNarrator(new EncounterChoice("The dark hums.", ["Sneak", "Fight", "Gamble"]));
        using var derived = With(narrator);
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await ReadAsync(await PromptAsync(client, id));

        var root = doc.RootElement;
        root.GetProperty("choicePrompt").GetString().Should().Be("The dark hums.");
        root.GetProperty("choices").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("Sneak", "Fight", "Gamble");
        narrator.SeenChoicesContext.Should().NotBeNull();
        narrator.SeenChoicesContext!.Room.Should().Be(RoomType.Combat);
        narrator.SeenChoicesContext.PartyName.Should().Be("Heroes");
    }

    [Fact]
    public async Task Choice_UsesNarratorOutcome_AndLeavesMathToCSharp()
    {
        var notifier = new CapturingNotifier();
        var narrator = new FakeNarrator(outcome: "Steel rings. The goblins flee.");
        using var derived = With(narrator, room: 0, success: Fail, notifier: notifier);
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await TurnAsync(client, id, 1);

        var root = doc.RootElement;
        root.TryGetProperty("lastOutcome", out _).Should().BeFalse();
        (await notifier.NarrationAsync).Outcome.Should().Be("Steel rings. The goblins flee.");
        root.GetProperty("targetName").GetString().Should().Be("Conan");
        root.GetProperty("damage").GetInt32().Should().Be(2);
        root.GetProperty("gold").GetInt32().Should().Be(0);
        narrator.SeenOption.Should().Be("Kick it");
        narrator.SeenSuccess.Should().BeFalse();
        narrator.SeenOutcomeContext!.Room.Should().Be(RoomType.Combat);
    }

    [Fact]
    public async Task Choice_Success_PassesSuccessTrueToNarrator()
    {
        var notifier = new CapturingNotifier();
        var narrator = new FakeNarrator();
        using var derived = With(narrator, room: 0, success: Succeed, notifier: notifier);
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        using var _ = await TurnAsync(client, id, 0);
        await notifier.NarrationAsync;

        narrator.SeenSuccess.Should().BeTrue();
        narrator.SeenOption.Should().Be("Open it");
    }

    [Fact]
    public async Task Prompt_NarratorThrows_FallsBackToThreeDeterministicChoices()
    {
        using var derived = With(new FakeNarrator(throwOnChoices: true));
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await ReadAsync(await PromptAsync(client, id));

        var expected = FallbackFlavor.Choices(RoomType.Combat, 0);
        doc.RootElement.GetProperty("choicePrompt").GetString().Should().Be(expected.Prompt);
        doc.RootElement.GetProperty("choices").EnumerateArray().Select(e => e.GetString())
            .Should().Equal(expected.Options);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Prompt_NarratorReturnsWrongChoiceCount_FallsBack(int count)
    {
        var bad = new EncounterChoice("Odd.", Enumerable.Range(0, count).Select(i => $"Opt {i}").ToArray());
        using var derived = With(new FakeNarrator(bad));
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await ReadAsync(await PromptAsync(client, id));

        doc.RootElement.GetProperty("choices").GetArrayLength().Should().Be(3);
        doc.RootElement.GetProperty("choicePrompt").GetString().Should().Be(FallbackFlavor.Choices(RoomType.Combat, 0).Prompt);
    }

    [Fact]
    public async Task Prompt_NarratorReturnsBlankOption_FallsBack()
    {
        var bad = new EncounterChoice("Odd.", ["Fine", "  ", "Fine too"]);
        using var derived = With(new FakeNarrator(bad));
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await ReadAsync(await PromptAsync(client, id));

        doc.RootElement.GetProperty("choicePrompt").GetString().Should().Be(FallbackFlavor.Choices(RoomType.Combat, 0).Prompt);
    }

    [Fact]
    public async Task Choice_OutcomeNarratorThrows_FallsBackAndStillResolvesTurn()
    {
        var notifier = new CapturingNotifier();
        using var derived = With(new FakeNarrator(throwOnOutcome: true), room: 0, success: Fail, notifier: notifier);
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await TurnAsync(client, id);

        doc.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("damage").GetInt32().Should().Be(2);
        (await notifier.NarrationAsync).Outcome
            .Should().Be(FallbackFlavor.Outcome(RoomType.Combat, false, 0));
    }

    [Fact]
    public async Task Choice_OutcomeNarratorReturnsBlank_FallsBack()
    {
        var notifier = new CapturingNotifier();
        using var derived = With(new FakeNarrator(outcome: "   "), room: 1, success: Succeed, notifier: notifier);
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await TurnAsync(client, id);

        (await notifier.NarrationAsync).Outcome
            .Should().Be(FallbackFlavor.Outcome(RoomType.Chest, true, 0));
    }

    [Fact]
    public async Task Handler_PromptRequestCancelled_PropagatesCancellationFromNarrator()
    {
        using var derived = With(new FakeNarrator());
        var id = await PartyTestData.CreatePartyAsync(derived.CreateClient());
        var handler = new ExploreDungeonHandler(
            derived.Services.GetRequiredService<IMemoryCache>(),
            new FixedRoller(0),
            new CancellingNarrator(),
            new NoopNotifier(),
            NullLogger<ExploreDungeonHandler>.Instance);
        using var cts = new CancellationTokenSource();

        var act = () => handler.HandleAsync(id, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class CancellingNarrator : IDungeonNarrator
    {
        public Task<EncounterChoice> GenerateChoicesAsync(NarrationContext ctx, CancellationToken ct) =>
            throw new OperationCanceledException();

        public Task<string> NarrateOutcomeAsync(string chosenOption, bool success, NarrationContext ctx, CancellationToken ct) =>
            throw new OperationCanceledException();
    }

    private sealed class NoopNotifier : IPartyNotifier
    {
        public Task PartyUpdatedAsync(GetPartyStateResponse state, CancellationToken ct = default) => Task.CompletedTask;
        public Task TurnResolvedAsync(ExploreResponse turn, CancellationToken ct = default) => Task.CompletedTask;
        public Task NarrationReadyAsync(NarrationReady narration, CancellationToken ct = default) => Task.CompletedTask;
        public Task DiscussionUpdatedAsync(Guid partyId, IReadOnlyList<PlayerAction> actions, CancellationToken ct = default) => Task.CompletedTask;
    }
}

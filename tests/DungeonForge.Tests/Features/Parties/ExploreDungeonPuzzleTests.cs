using System.Net;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Infrastructure.Narration;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using static DungeonForge.Tests.Features.Parties.ExploreTestSupport;

namespace DungeonForge.Tests.Features.Parties;

public sealed class ExploreDungeonPuzzleTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const int PuzzleRoom = 2;

    private sealed class SpyNarrator : IDungeonNarrator
    {
        public NarrationContext? OutcomeContext { get; private set; }
        public int OutcomeCalls { get; private set; }

        public Task<EncounterChoice> GenerateChoicesAsync(NarrationContext ctx, CancellationToken ct) =>
            Task.FromResult(new EncounterChoice("Runes glow.", ["Study", "Press", "Smash"]));

        public Task<string> NarrateOutcomeAsync(string chosenOption, bool success, NarrationContext ctx, CancellationToken ct)
        {
            OutcomeCalls++;
            OutcomeContext = ctx;
            return Task.FromResult("The runes settle.");
        }
    }

    private static PlayerAction Act(Guid member, string text, string name = "Conan") => new(member, name, text);

    private (HttpClient Client, CapturingNotifier Notifier, SpyNarrator Narrator, IDisposable Scope) Arrange(int room, int success)
    {
        var notifier = new CapturingNotifier();
        var narrator = new SpyNarrator();
        var derived = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IRoller>(new FixedRoller(room, success));
            s.AddSingleton<IDungeonNarrator>(narrator);
            s.AddSingleton<IPartyNotifier>(notifier);
        }));
        return (derived.CreateClient(), notifier, narrator, derived);
    }

    // ---- Puzzle room ----

    [Fact]
    public async Task Prompt_PuzzleRoll_YieldsPuzzleRoom()
    {
        var (client, _, _, scope) = Arrange(PuzzleRoom, Succeed);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await ReadAsync(await PromptAsync(client, id));

        doc.RootElement.GetProperty("roomType").GetString().Should().Be("puzzle");
        doc.RootElement.GetProperty("choices").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task Resolve_PuzzleSuccess_AwardsHigherGold_Deterministically()
    {
        var (client, _, _, scope) = Arrange(PuzzleRoom, Succeed);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await TurnAsync(client, id, 0);

        doc.RootElement.GetProperty("goldFound").GetInt32().Should().Be(10);
        doc.RootElement.GetProperty("damage").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("gold").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task Resolve_PuzzleFailure_DealsHigherDamage_Deterministically()
    {
        var (client, _, _, scope) = Arrange(PuzzleRoom, Fail);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await TurnAsync(client, id, 0);

        doc.RootElement.GetProperty("damage").GetInt32().Should().Be(4);
        doc.RootElement.GetProperty("goldFound").GetInt32().Should().Be(0);
    }

    // ---- Actions: submit, validate ----

    [Fact]
    public async Task ActionsOnly_Request_SubmitsToDiscussion_AndBroadcasts_WithoutAdvancingTurn()
    {
        var (client, notifier, _, scope) = Arrange(PuzzleRoom, Succeed);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        var member = Guid.NewGuid();

        var response = await ActAsync(client, id, null, Act(member, "  read   the\nrunes "));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await ReadAsync(response);
        doc.RootElement.GetProperty("turn").GetInt32().Should().Be(0);
        var discussion = doc.RootElement.GetProperty("discussion");
        discussion.GetArrayLength().Should().Be(1);
        discussion[0].GetProperty("memberId").GetGuid().Should().Be(member);
        discussion[0].GetProperty("name").GetString().Should().Be("Conan");
        discussion[0].GetProperty("text").GetString().Should().Be("read the runes");
        notifier.Discussions.Should().ContainSingle().Which.Should().ContainSingle(a => a.Text == "read the runes");
    }

    [Fact]
    public async Task ActionsOnly_SameMemberResubmits_ReplacesPreviousAction()
    {
        var (client, _, _, scope) = Arrange(PuzzleRoom, Succeed);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        var member = Guid.NewGuid();
        (await ActAsync(client, id, null, Act(member, "first"))).EnsureSuccessStatusCode();

        using var doc = await ReadAsync(await ActAsync(client, id, null, Act(member, "second")));

        doc.RootElement.GetProperty("discussion").EnumerateArray().Select(e => e.GetProperty("text").GetString())
            .Should().Equal("second");
    }

    [Theory]
    [InlineData(9, "ok")]
    [InlineData(1, "")]
    [InlineData(1, "   ")]
    [InlineData(1, "TOOLONG")]
    public async Task Actions_Invalid_Returns400WithActionsKey(int count, string text)
    {
        var (client, _, _, scope) = Arrange(PuzzleRoom, Succeed);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        if (text == "TOOLONG") text = new string('x', ExploreRequestLimits.MaxActionTextLength + 1);
        var actions = Enumerable.Range(0, count).Select(_ => Act(Guid.NewGuid(), text)).ToArray();

        var response = await ActAsync(client, id, null, actions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = await ReadAsync(response);
        doc.RootElement.GetProperty("errors").TryGetProperty("actions", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Actions_Invalid_AlsoRejectedWhenResolving()
    {
        var (client, _, _, scope) = Arrange(PuzzleRoom, Succeed);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();

        var response = await ActAsync(client, id, 0, Act(Guid.NewGuid(), ""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- Resolution math with action bonus ----

    // Choice 2 has base success 30%; each distinct action adds 5 points, capped at 15.
    [Theory]
    [InlineData(34, 0, false)] // 34 >= 30
    [InlineData(34, 1, true)]  // 34 < 35
    [InlineData(39, 1, false)]
    [InlineData(39, 2, true)]  // 39 < 40
    [InlineData(44, 3, true)]  // 44 < 45
    [InlineData(45, 3, false)]
    [InlineData(45, 6, false)] // bonus is capped at 15
    public async Task Resolve_DistinctActionsAddCappedBonus(int roll, int distinctActions, bool expectSuccess)
    {
        var (client, _, _, scope) = Arrange(PuzzleRoom, roll);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        var actions = Enumerable.Range(0, distinctActions).Select(i => Act(Guid.NewGuid(), $"idea {i}")).ToArray();

        using var doc = await ReadAsync(await ActAsync(client, id, 2, actions));

        doc.RootElement.GetProperty("goldFound").GetInt32().Should().Be(expectSuccess ? 10 : 0);
    }

    [Fact]
    public async Task Resolve_DuplicateMemberActions_CountOnce()
    {
        var (client, _, _, scope) = Arrange(PuzzleRoom, 39);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        var member = Guid.NewGuid();

        using var doc = await ReadAsync(await ActAsync(client, id, 2, Act(member, "a"), Act(member, "b")));

        doc.RootElement.GetProperty("goldFound").GetInt32().Should().Be(0); // one distinct => 35 threshold
    }

    [Fact]
    public async Task Resolve_UsesActionsSubmittedEarlierFromOtherMembers()
    {
        var (client, _, _, scope) = Arrange(PuzzleRoom, 39);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        (await ActAsync(client, id, null, Act(Guid.NewGuid(), "idea one"))).EnsureSuccessStatusCode();

        using var doc = await ReadAsync(await ActAsync(client, id, 2, Act(Guid.NewGuid(), "idea two", "Merlin")));

        doc.RootElement.GetProperty("goldFound").GetInt32().Should().Be(10); // 2 distinct => 40 threshold
    }

    // ---- Two-phase narration ----

    [Fact]
    public async Task Resolve_ReturnsMechanicsImmediately_ThenBroadcastsNarrationReadyWithActionsInContext()
    {
        var (client, notifier, narrator, scope) = Arrange(PuzzleRoom, Succeed);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        using var doc = await ReadAsync(await ActAsync(client, id, 0, Act(a, "first idea"), Act(b, "second idea", "Merlin")));

        doc.RootElement.TryGetProperty("lastOutcome", out _).Should().BeFalse();
        doc.RootElement.GetProperty("narrationPending").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("turn").GetInt32().Should().Be(1);

        var ready = await notifier.NarrationAsync;
        ready.PartyId.Should().Be(id);
        ready.Turn.Should().Be(1);
        ready.Outcome.Should().Be("The runes settle.");
        narrator.OutcomeCalls.Should().Be(1);
        narrator.OutcomeContext!.Actions.Select(x => x.Text).Should().Equal("first idea", "second idea");
        narrator.OutcomeContext.Turn.Should().Be(0);
    }

    [Fact]
    public async Task NarrationReady_Broadcast_FailureDoesNotBreakAnything()
    {
        var narrator = new SpyNarrator();
        using var derived = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IRoller>(new FixedRoller(PuzzleRoom, Succeed));
            s.AddSingleton<IDungeonNarrator>(narrator);
            s.AddSingleton<IPartyNotifier>(new ThrowingNarrationNotifier());
        }));
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await TurnAsync(client, id, 0);

        doc.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
    }

    private sealed class ThrowingNarrationNotifier : IPartyNotifier
    {
        public Task PartyUpdatedAsync(GetPartyStateResponse state, CancellationToken ct = default) => Task.CompletedTask;
        public Task TurnResolvedAsync(ExploreResponse turn, CancellationToken ct = default) => Task.CompletedTask;
        public Task NarrationReadyAsync(NarrationReady narration, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
        public Task DiscussionUpdatedAsync(Guid partyId, IReadOnlyList<PlayerAction> actions, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}

using System.Net;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Infrastructure.Narration;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using static DungeonForge.Tests.Support.PartyTestBuilders;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Parties;

public sealed class ExploreDungeonPuzzleTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const int PuzzleRoom = 2;

    private sealed class SpyNarrator : IDungeonNarrator
    {
        public NarrationContext? TurnContext { get; private set; }
        public int TurnCalls { get; private set; }

        public Task<string> GenerateScenarioAsync(NarrationContext ctx, CancellationToken ct) =>
            Task.FromResult("Runes glow on the wall. What do you do?");

        public Task<TurnNarration> NarrateTurnAsync(bool success, NarrationContext ctx, CancellationToken ct)
        {
            TurnCalls++;
            TurnContext = ctx;
            return Task.FromResult(new TurnNarration("The runes settle.", "A vault door waits. What do you do?"));
        }
    }

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
    public async Task Prompt_PuzzleRoll_YieldsPuzzleRoomWithScenario()
    {
        var (client, _, _, scope) = Arrange(PuzzleRoom, Succeed);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await ReadAsync(await PromptAsync(client, id));

        doc.RootElement.GetProperty("roomType").GetString().Should().Be("puzzle");
        doc.RootElement.GetProperty("scenario").GetString().Should().Be("Runes glow on the wall. What do you do?");
    }

    [Fact]
    public async Task Resolve_PuzzleSuccess_AwardsHigherGold_Deterministically()
    {
        var (client, _, _, scope) = Arrange(PuzzleRoom, Succeed);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await TurnAsync(client, id);

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

        using var doc = await TurnAsync(client, id);

        doc.RootElement.GetProperty("damage").GetInt32().Should().Be(7);
        doc.RootElement.GetProperty("goldFound").GetInt32().Should().Be(0);
    }

    // ---- Discussion submit (no resolve) ----

    [Fact]
    public async Task SubmitOnly_Request_LocksAnswerPrivately_WithoutAdvancingTurn()
    {
        var (client, notifier, narrator, scope) = Arrange(PuzzleRoom, Succeed);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        var member = Guid.NewGuid();

        var response = await SubmitAsync(client, id, Act("  read   the\nrunes ", member: member));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await ReadAsync(response);
        doc.RootElement.GetProperty("turn").GetInt32().Should().Be(0);
        doc.RootElement.ToString().Should().NotContain("read the runes");
        notifier.Discussions.Should().BeEmpty();
        narrator.TurnCalls.Should().Be(0);
    }

    [Fact]
    public async Task SubmitOnly_SameMemberResubmits_ReplacesPreviousAction()
    {
        var (client, _, _, scope) = Arrange(PuzzleRoom, Succeed);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        var member = Guid.NewGuid();
        (await SubmitAsync(client, id, Act("first", member: member))).EnsureSuccessStatusCode();

        using var doc = await ReadAsync(await SubmitAsync(client, id, Act("second", member: member)));

        doc.RootElement.GetProperty("lockStatus").EnumerateArray().Count(l => l.GetProperty("lockedIn").GetBoolean()).Should().Be(1);
        doc.RootElement.ToString().Should().NotContain("second");
    }

    // ---- Validation (filter) ----

    [Theory]
    [InlineData(9, "ok")]
    [InlineData(1, "")]
    [InlineData(1, "   ")]
    [InlineData(1, "\u0007")]
    [InlineData(1, "TOOLONG")]
    public async Task Actions_Invalid_Returns400WithActionsKey_ForResolveAndSubmit(int count, string text)
    {
        var (client, _, narrator, scope) = Arrange(PuzzleRoom, Succeed);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        if (text == "TOOLONG") text = new string('x', ExploreRequestLimits.MaxActionTextLength + 1);
        var actions = Enumerable.Range(0, count).Select(_ => Act(text, member: Guid.NewGuid())).ToArray();

        var resolve = await ResolveAsync(client, id, actions);
        var submit = await SubmitAsync(client, id, actions);

        resolve.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        submit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = await ReadAsync(resolve);
        doc.RootElement.GetProperty("errors").TryGetProperty("actions", out _).Should().BeTrue();
        narrator.TurnCalls.Should().Be(0);
    }

    // ---- Resolution math: each action rolls on its own; base 50%, +10% per teammate doing the same, -10% per clash ----

    private static Task<Guid> CreatePartyOfAsync(HttpClient client, int size) =>
        PartyTestData.CreatePartyAsync(client, members: Enumerable.Range(0, size).Select(i => (object)new { name = $"Hero{i}", @class = "warrior" }).ToArray());

    [Theory]
    [InlineData(49, "idea one", "idea two", true)]            // unrelated plans: 50% each, 49 < 50
    [InlineData(50, "idea one", "idea two", false)]
    [InlineData(59, "attack the door", "smash it open", true)] // same plan: teamwork 60% each
    [InlineData(60, "attack the door", "smash it open", false)]
    [InlineData(39, "attack the door", "run away", true)]      // clashing plans: 40% each
    [InlineData(40, "attack the door", "run away", false)]
    public async Task Resolve_OddsDependOnWhatTeammatesLockedIn(int roll, string first, string second, bool expectSuccess)
    {
        var (client, _, _, scope) = Arrange(PuzzleRoom, roll);
        using var _s = scope;
        var id = await CreatePartyOfAsync(client, 2);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();

        using var doc = await ReadAsync(await ResolveAsync(client, id,
            Act(first, "Hero0", Guid.NewGuid()), Act(second, "Hero1", Guid.NewGuid())));

        doc.RootElement.GetProperty("goldFound").GetInt32().Should().Be(expectSuccess ? 10 : 0);
    }

    [Fact]
    public async Task Resolve_EachFailedMemberTakesTheirOwnHit_AndNobodyElseDoes()
    {
        var (client, _, _, scope) = Arrange(PuzzleRoom, 40);
        using var _s = scope;
        var id = await CreatePartyOfAsync(client, 3);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();

        // Two attackers back each other against one clashing runner: attackers roll 50%, the runner only 30%.
        // A roll of 40 saves both attackers and fails the runner, so only Hero2 is hurt, and the majority wins the room.
        using var doc = await ReadAsync(await ResolveAsync(client, id,
            Act("attack the door", "Hero0", Guid.NewGuid()),
            Act("smash the door", "Hero1", Guid.NewGuid()),
            Act("run away", "Hero2", Guid.NewGuid())));

        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("hits").EnumerateArray().Select(h => h.GetProperty("name").GetString())
            .Should().Equal("Hero2");
        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("members").EnumerateArray()
            .Select(m => m.GetProperty("maxHp").GetInt32() - m.GetProperty("hp").GetInt32())
            .Should().Equal(0, 0, 7);
    }

    [Fact]
    public async Task Resolve_DuplicateMemberActions_CountOnce()
    {
        var (client, _, _, scope) = Arrange(PuzzleRoom, 50);
        using var _s = scope;
        var id = await CreatePartyOfAsync(client, 1);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        var member = Guid.NewGuid();

        using var doc = await ReadAsync(await ResolveAsync(client, id, Act("a", member: member), Act("b", member: member)));

        doc.RootElement.GetProperty("goldFound").GetInt32().Should().Be(0); // one distinct action => 50%, roll 50 fails
    }

    [Fact]
    public async Task Resolve_UsesActionsSubmittedEarlierFromOtherMembers()
    {
        var (client, _, _, scope) = Arrange(PuzzleRoom, 49);
        using var _s = scope;
        var id = await CreatePartyOfAsync(client, 2);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        (await SubmitAsync(client, id, Act("idea one", member: Guid.NewGuid()))).EnsureSuccessStatusCode();

        using var doc = await ReadAsync(await ResolveAsync(client, id, Act("idea two", "Merlin", Guid.NewGuid())));

        doc.RootElement.GetProperty("goldFound").GetInt32().Should().Be(10); // both earlier and new action roll 49 < 50
    }

    // ---- Two-phase narration ----

    [Fact]
    public async Task Resolve_ReturnsMechanicsImmediately_ThenBroadcastsNarrationReadyWithNextScenario()
    {
        var (client, notifier, narrator, scope) = Arrange(PuzzleRoom, Succeed);
        using var _s = scope;
        var id = await CreatePartyOfAsync(client, 2);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();

        using var doc = await ReadAsync(await ResolveAsync(client, id,
            Act("first idea", member: Guid.NewGuid()), Act("second idea", "Merlin", Guid.NewGuid())));

        doc.RootElement.TryGetProperty("lastOutcome", out _).Should().BeFalse();
        doc.RootElement.GetProperty("narrationPending").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("turn").GetInt32().Should().Be(1);

        var ready = await notifier.NarrationAsync;
        ready.PartyId.Should().Be(id);
        ready.Turn.Should().Be(1);
        ready.Outcome.Should().Be("The runes settle.");
        ready.NextScenario.Should().Be("A vault door waits. What do you do?");
        ready.NextRoomType.Should().Be(RoomType.Puzzle);
        narrator.TurnCalls.Should().Be(1);
        narrator.TurnContext!.Actions.Select(x => x.Text).Should().Equal("first idea", "second idea");
        narrator.TurnContext.Turn.Should().Be(0);
    }
}

using System.Net;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
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

        doc.RootElement.GetProperty("damage").GetInt32().Should().Be(4);
        doc.RootElement.GetProperty("goldFound").GetInt32().Should().Be(0);
    }

    // ---- Discussion submit (no resolve) ----

    [Fact]
    public async Task SubmitOnly_Request_SubmitsToDiscussion_AndBroadcasts_WithoutAdvancingTurn()
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
        var discussion = doc.RootElement.GetProperty("discussion");
        discussion.GetArrayLength().Should().Be(1);
        discussion[0].GetProperty("memberId").GetGuid().Should().Be(member);
        discussion[0].GetProperty("name").GetString().Should().Be("Conan");
        discussion[0].GetProperty("text").GetString().Should().Be("read the runes");
        notifier.Discussions.Should().ContainSingle().Which.Should().ContainSingle(a => a.Text == "read the runes");
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

        doc.RootElement.GetProperty("discussion").EnumerateArray().Select(e => e.GetProperty("text").GetString())
            .Should().Equal("second");
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

    // ---- Resolution math: base 55% + 5% per distinct acting member (max +15) ----

    [Theory]
    [InlineData(59, 1, true)]   // 60 > 59
    [InlineData(60, 1, false)]
    [InlineData(64, 2, true)]   // 65 > 64
    [InlineData(65, 2, false)]
    [InlineData(69, 3, true)]   // 70 > 69
    [InlineData(70, 3, false)]
    [InlineData(70, 6, false)]  // bonus is capped at 15
    public async Task Resolve_DistinctActionsAddCappedBonus(int roll, int distinctActions, bool expectSuccess)
    {
        var (client, _, _, scope) = Arrange(PuzzleRoom, roll);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        var actions = Enumerable.Range(0, distinctActions).Select(i => Act($"idea {i}", member: Guid.NewGuid())).ToArray();

        using var doc = await ReadAsync(await ResolveAsync(client, id, actions));

        doc.RootElement.GetProperty("goldFound").GetInt32().Should().Be(expectSuccess ? 10 : 0);
    }

    [Fact]
    public async Task Resolve_DuplicateMemberActions_CountOnce()
    {
        var (client, _, _, scope) = Arrange(PuzzleRoom, 60);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        var member = Guid.NewGuid();

        using var doc = await ReadAsync(await ResolveAsync(client, id, Act("a", member: member), Act("b", member: member)));

        doc.RootElement.GetProperty("goldFound").GetInt32().Should().Be(0); // one distinct => 60 threshold, roll 60 fails
    }

    [Fact]
    public async Task Resolve_UsesActionsSubmittedEarlierFromOtherMembers()
    {
        var (client, _, _, scope) = Arrange(PuzzleRoom, 64);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        (await SubmitAsync(client, id, Act("idea one", member: Guid.NewGuid()))).EnsureSuccessStatusCode();

        using var doc = await ReadAsync(await ResolveAsync(client, id, Act("idea two", "Merlin", Guid.NewGuid())));

        doc.RootElement.GetProperty("goldFound").GetInt32().Should().Be(10); // 2 distinct => 65 threshold, roll 64 succeeds
    }

    // ---- Two-phase narration ----

    [Fact]
    public async Task Resolve_ReturnsMechanicsImmediately_ThenBroadcastsNarrationReadyWithNextScenario()
    {
        var (client, notifier, narrator, scope) = Arrange(PuzzleRoom, Succeed);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
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

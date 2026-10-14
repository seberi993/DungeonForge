using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using static DungeonForge.Tests.Features.Parties.ExploreTestSupport;

namespace DungeonForge.Tests.Features.Parties;

public sealed class ExploreDungeonTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _derived = [];

    public void Dispose() => _derived.ForEach(f => f.Dispose());

    private HttpClient ClientWithRoll(int room, int success = Succeed)
    {
        var derived = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IRoller>(new FixedRoller(room, success));
        }));
        _derived.Add(derived);
        return derived.CreateClient();
    }

    private static Task<Guid> CreateSoloMagePartyAsync(HttpClient client) =>
        PartyTestData.CreatePartyAsync(client, members: [new { name = "Merlin", @class = "mage" }]);

    private static JsonElement Member(JsonDocument doc, int index) =>
        doc.RootElement.GetProperty("members")[index];

    // ---- Prompt step ----

    [Fact]
    public async Task Explore_NoIndex_ReturnsPromptWithThreeChoices_AndChangesNothing()
    {
        var client = ClientWithRoll(0);
        var id = await PartyTestData.CreatePartyAsync(client);

        var response = await PromptAsync(client, id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await ReadAsync(response);
        var root = doc.RootElement;
        root.GetProperty("turn").GetInt32().Should().Be(0);
        root.GetProperty("roomType").GetString().Should().Be("combat");
        root.GetProperty("damage").GetInt32().Should().Be(0);
        root.GetProperty("goldFound").GetInt32().Should().Be(0);
        root.GetProperty("choicePrompt").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("choices").GetArrayLength().Should().Be(3);

        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(0);
        state.RootElement.GetProperty("gold").GetInt32().Should().Be(0);
        state.RootElement.GetProperty("members").EnumerateArray()
            .Should().OnlyContain(m => m.GetProperty("hp").GetInt32() == m.GetProperty("maxHp").GetInt32());
    }

    [Fact]
    public async Task Explore_NoIndexTwice_ReturnsSamePendingPromptWithoutReRolling()
    {
        var derived = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddSingleton<IRoller>(new FlippingRoomRoller())));
        _derived.Add(derived);
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        using var first = await ReadAsync(await PromptAsync(client, id));
        using var second = await ReadAsync(await PromptAsync(client, id));

        second.RootElement.GetProperty("roomType").GetString()
            .Should().Be(first.RootElement.GetProperty("roomType").GetString());
        second.RootElement.GetProperty("choicePrompt").GetString()
            .Should().Be(first.RootElement.GetProperty("choicePrompt").GetString());
        second.RootElement.GetProperty("choices").GetRawText()
            .Should().Be(first.RootElement.GetProperty("choices").GetRawText());
    }

    private sealed class FlippingRoomRoller : DungeonForge.Api.Features.Parties.ExploreDungeon.IRoller
    {
        private int _calls;
        public int Next(int max) => max == 2 ? Interlocked.Increment(ref _calls) % 2 : 0;
    }

    [Fact]
    public async Task Explore_UnknownPartyId_ReturnsNotFound()
    {
        var response = await factory.CreateClient().PostAsync($"/api/parties/{Guid.NewGuid()}/explore", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Explore_UnknownPartyIdWithChoice_ReturnsNotFound()
    {
        var response = await ChooseAsync(factory.CreateClient(), Guid.NewGuid(), 0);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Resolve step: validation ----

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(100)]
    public async Task Explore_OutOfRangeIndex_ReturnsBadRequestAndKeepsPending(int index)
    {
        var client = ClientWithRoll(0);
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();

        var response = await ChooseAsync(client, id, index);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(0);
        // Pending encounter survives a bad index: a valid choice still resolves.
        (await ChooseAsync(client, id, 0)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public async Task Explore_OutOfRangeIndex_IsRejectedByFilterBeforePartyLookup(int index)
    {
        var client = ClientWithRoll(0);

        var response = await ChooseAsync(client, Guid.NewGuid(), index);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await ReadAsync(response);
        body.RootElement.GetProperty("errors").TryGetProperty("selectedChoiceIndex", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Explore_IndexWithoutPendingEncounter_ReturnsBadRequest()
    {
        var client = ClientWithRoll(0);
        var id = await PartyTestData.CreatePartyAsync(client);

        var response = await ChooseAsync(client, id, 0);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Explore_ChoiceResolved_ClearsPending_SoSecondChoiceIsBadRequest()
    {
        var client = ClientWithRoll(0);
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        (await ChooseAsync(client, id, 0)).StatusCode.Should().Be(HttpStatusCode.OK);

        var again = await ChooseAsync(client, id, 0);

        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- Resolve step: outcomes ----

    [Fact]
    public async Task Explore_CombatSuccess_NoDamage_AdvancesTurn()
    {
        var client = ClientWithRoll(0, Succeed);
        var id = await PartyTestData.CreatePartyAsync(client);

        using var t1 = await TurnAsync(client, id, 0);

        t1.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
        t1.RootElement.GetProperty("roomType").GetString().Should().Be("combat");
        t1.RootElement.GetProperty("damage").GetInt32().Should().Be(0);
        t1.RootElement.GetProperty("goldFound").GetInt32().Should().Be(0);
        t1.RootElement.GetProperty("lastOutcome").GetString().Should().NotBeNullOrWhiteSpace();
        // Nulls are omitted on the wire: a resolved turn carries no choices/prompt.
        t1.RootElement.TryGetProperty("choices", out _).Should().BeFalse();
        t1.RootElement.TryGetProperty("choicePrompt", out _).Should().BeFalse();

        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
        state.RootElement.GetProperty("members").EnumerateArray()
            .Should().OnlyContain(m => m.GetProperty("hp").GetInt32() == m.GetProperty("maxHp").GetInt32());
    }

    [Fact]
    public async Task Explore_CombatFailure_DamagesWarriorByTwoPlusTurnMod3()
    {
        var client = ClientWithRoll(0, Fail);
        var id = await PartyTestData.CreatePartyAsync(client);

        using var t1 = await TurnAsync(client, id);
        t1.RootElement.GetProperty("roomType").GetString().Should().Be("combat");
        t1.RootElement.GetProperty("targetName").GetString().Should().Be("Conan");
        t1.RootElement.GetProperty("damage").GetInt32().Should().Be(2);
        t1.RootElement.GetProperty("goldFound").GetInt32().Should().Be(0);
        t1.RootElement.GetProperty("isPartyDefeated").GetBoolean().Should().BeFalse();
        t1.RootElement.GetProperty("flavorEvent").GetString().Should().NotBeNullOrWhiteSpace();

        using var t2 = await TurnAsync(client, id);
        t2.RootElement.GetProperty("damage").GetInt32().Should().Be(3);
        t2.RootElement.GetProperty("gold").GetInt32().Should().Be(0);

        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        Member(state, 0).GetProperty("hp").GetInt32().Should().Be(25);
        Member(state, 1).GetProperty("hp").GetInt32().Should().Be(16);
        Member(state, 2).GetProperty("hp").GetInt32().Should().Be(20);
    }

    [Theory]
    [InlineData(0, 79, true)]
    [InlineData(0, 80, false)]
    [InlineData(1, 54, true)]
    [InlineData(1, 55, false)]
    [InlineData(2, 29, true)]
    [InlineData(2, 30, false)]
    public async Task Explore_SuccessChanceDependsOnChoiceRiskTier(int index, int roll, bool expectSuccess)
    {
        var client = ClientWithRoll(0, roll);
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await TurnAsync(client, id, index);

        (doc.RootElement.GetProperty("damage").GetInt32() == 0).Should().Be(expectSuccess);
    }

    [Fact]
    public async Task Explore_Response_DoesNotExposeLegacyHpFields()
    {
        var client = ClientWithRoll(0);
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await TurnAsync(client, id);

        doc.RootElement.TryGetProperty("hp", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("hpChange", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Explore_ChestSuccess_AddsGoldFivePlusTurnAndDealsNoDamage()
    {
        var client = ClientWithRoll(1, Succeed);
        var id = await PartyTestData.CreatePartyAsync(client);

        using var t1 = await TurnAsync(client, id);
        t1.RootElement.GetProperty("roomType").GetString().Should().Be("chest");
        t1.RootElement.GetProperty("damage").GetInt32().Should().Be(0);
        t1.RootElement.GetProperty("goldFound").GetInt32().Should().Be(5);
        t1.RootElement.GetProperty("gold").GetInt32().Should().Be(5);

        using var t2 = await TurnAsync(client, id);
        t2.RootElement.GetProperty("goldFound").GetInt32().Should().Be(6);
        t2.RootElement.GetProperty("gold").GetInt32().Should().Be(11);

        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("gold").GetInt32().Should().Be(11);
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(2);
        state.RootElement.GetProperty("members").EnumerateArray()
            .Should().OnlyContain(m => m.GetProperty("hp").GetInt32() == m.GetProperty("maxHp").GetInt32());
    }

    [Fact]
    public async Task Explore_ChestFailure_FindsNoGoldAndDealsNoDamage_ButAdvancesTurn()
    {
        var client = ClientWithRoll(1, Fail);
        var id = await PartyTestData.CreatePartyAsync(client);

        using var t1 = await TurnAsync(client, id);

        t1.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
        t1.RootElement.GetProperty("goldFound").GetInt32().Should().Be(0);
        t1.RootElement.GetProperty("damage").GetInt32().Should().Be(0);
        t1.RootElement.GetProperty("gold").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Explore_CombatFailuresUntilWarriorDown_FallsThroughToNextMember()
    {
        var client = ClientWithRoll(0, Fail);
        var id = await PartyTestData.CreatePartyAsync(client);

        // Warrior (30 HP) absorbs 2,3,4 x3 = 27 over 9 turns (3 left); turn 10 deals 2 (1 left); turn 11 would deal 3 but clamps to 1 (0 left).
        var targets = new List<string?>();
        var damages = new List<int>();
        for (var i = 0; i < 11; i++)
        {
            using var doc = await TurnAsync(client, id);
            targets.Add(doc.RootElement.GetProperty("targetName").GetString());
            damages.Add(doc.RootElement.GetProperty("damage").GetInt32());
        }

        targets.Should().OnlyContain(t => t == "Conan");
        damages.Should().Equal(2, 3, 4, 2, 3, 4, 2, 3, 4, 2, 1);

        using var twelfth = await TurnAsync(client, id);
        twelfth.RootElement.GetProperty("targetName").GetString().Should().Be("Merlin");
        twelfth.RootElement.GetProperty("damage").GetInt32().Should().Be(4);
        twelfth.RootElement.GetProperty("isPartyDefeated").GetBoolean().Should().BeFalse();

        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        Member(state, 0).GetProperty("isDefeated").GetBoolean().Should().BeTrue();
        Member(state, 0).GetProperty("hp").GetInt32().Should().Be(0);
        Member(state, 1).GetProperty("hp").GetInt32().Should().Be(12);
        state.RootElement.GetProperty("isPartyDefeated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Explore_LastMemberReachesZero_ReportsPartyDefeatedWithClampedDamage()
    {
        var client = ClientWithRoll(0, Fail);
        var id = await CreateSoloMagePartyAsync(client);

        // Mage 16 HP: damage 2,3,4,2,3 = 14 -> 2 HP left; turn 6 would deal 4 but clamps to 2.
        var damages = new List<int>();
        JsonDocument? last = null;
        for (var i = 0; i < 6; i++)
        {
            last?.Dispose();
            last = await TurnAsync(client, id);
            damages.Add(last.RootElement.GetProperty("damage").GetInt32());
        }

        using (last)
        {
            damages.Should().Equal(2, 3, 4, 2, 3, 2);
            last!.RootElement.GetProperty("isPartyDefeated").GetBoolean().Should().BeTrue();
        }

        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("isPartyDefeated").GetBoolean().Should().BeTrue();
        Member(state, 0).GetProperty("hp").GetInt32().Should().Be(0);
        Member(state, 0).GetProperty("isDefeated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Explore_DefeatedParty_ReturnsConflictForPromptAndChoice_AndDoesNotAdvanceTurn()
    {
        var client = ClientWithRoll(0, Fail);
        var id = await CreateSoloMagePartyAsync(client);
        for (var i = 0; i < 6; i++)
        {
            using var _ = await TurnAsync(client, id);
        }

        (await PromptAsync(client, id)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ChooseAsync(client, id, 0)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(6);
    }

    [Fact]
    public async Task Explore_ThirdCombatFailure_DealsFourDamage()
    {
        var client = ClientWithRoll(0, Fail);
        var id = await PartyTestData.CreatePartyAsync(client);

        JsonDocument? last = null;
        for (var i = 0; i < 3; i++)
        {
            last?.Dispose();
            last = await TurnAsync(client, id);
        }

        using (last)
        {
            last!.RootElement.GetProperty("damage").GetInt32().Should().Be(4);
            last.RootElement.GetProperty("targetName").GetString().Should().Be("Conan");
        }

        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        Member(state, 0).GetProperty("hp").GetInt32().Should().Be(21);
    }

    // ---- Concurrency ----

    [Fact]
    public async Task Explore_ConcurrentPrompts_AllReturnTheSamePendingEncounter_AndTurnStaysZero()
    {
        var client = ClientWithRoll(1);
        var id = await PartyTestData.CreatePartyAsync(client);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PromptAsync(client, id)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        var prompts = new HashSet<string>();
        foreach (var r in responses)
        {
            using var doc = await ReadAsync(r);
            prompts.Add(doc.RootElement.GetProperty("choicePrompt").GetString()!);
        }

        prompts.Should().HaveCount(1);
        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Explore_ConcurrentChoices_ResolveExactlyOnce()
    {
        var client = ClientWithRoll(1);
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => ChooseAsync(client, id, 0)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest).Should().Be(19);
        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Explore_MalformedBody_Index_AsString_IsRejected()
    {
        var client = ClientWithRoll(0);
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync($"/api/parties/{id}/explore", new { selectedChoiceIndex = "abc" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Explore_EmptyJsonObjectBody_BehavesLikePromptRequest()
    {
        var client = ClientWithRoll(0);
        var id = await PartyTestData.CreatePartyAsync(client);

        var response = await client.PostAsJsonAsync($"/api/parties/{id}/explore", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await ReadAsync(response);
        doc.RootElement.GetProperty("choices").GetArrayLength().Should().Be(3);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using static DungeonForge.Tests.Support.PartyTestBuilders;
using DungeonForge.Tests.Support;

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
    public async Task Explore_NoActions_ReturnsScenarioWithoutChoices_AndChangesNothing()
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
        root.GetProperty("scenario").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("scenarioPending").GetBoolean().Should().BeFalse();
        root.TryGetProperty("choices", out _).Should().BeFalse();
        root.TryGetProperty("choicePrompt", out _).Should().BeFalse();

        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(0);
        state.RootElement.GetProperty("gold").GetInt32().Should().Be(0);
        state.RootElement.GetProperty("members").EnumerateArray()
            .Should().OnlyContain(m => m.GetProperty("hp").GetInt32() == m.GetProperty("maxHp").GetInt32());
    }

    [Fact]
    public async Task Explore_PromptTwice_ReturnsSamePendingScenarioWithoutReRolling()
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
        second.RootElement.GetProperty("scenario").GetString()
            .Should().Be(first.RootElement.GetProperty("scenario").GetString());
    }

    private sealed class FlippingRoomRoller : IRoller
    {
        private int _calls;
        public int Next(int max) => max == 3 ? Interlocked.Increment(ref _calls) % 2 : 0;
    }

    [Fact]
    public async Task Explore_EmptyActionsArrayOrEmptyObject_BehavesLikePromptRequest()
    {
        var client = ClientWithRoll(0);
        var id = await PartyTestData.CreatePartyAsync(client);

        var empty = await client.PostAsJsonAsync($"/api/parties/{id}/explore", new { });
        var emptyList = await client.PostAsJsonAsync($"/api/parties/{id}/explore", new { actions = Array.Empty<object>() });

        empty.StatusCode.Should().Be(HttpStatusCode.OK);
        emptyList.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await ReadAsync(emptyList);
        doc.RootElement.GetProperty("turn").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("scenario").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Explore_UnknownPartyId_ReturnsNotFound()
    {
        var response = await factory.CreateClient().PostAsync($"/api/parties/{Guid.NewGuid()}/explore", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Explore_UnknownPartyIdWithActions_ReturnsNotFound()
    {
        var response = await ResolveAsync(factory.CreateClient(), Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Resolve step: validation ----

    [Fact]
    public async Task Explore_ResolveWithoutPendingScenario_ReturnsBadRequest()
    {
        var client = ClientWithRoll(0);
        var id = await PartyTestData.CreatePartyAsync(client);

        var response = await ResolveAsync(client, id);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Explore_ResolveWithOnlyControlCharacterAction_ReturnsBadRequestAndKeepsPending()
    {
        var client = ClientWithRoll(0);
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();

        var response = await ResolveAsync(client, id, Act("\u0007\u0007"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ResolveAsync(client, id)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- Resolve step: outcomes ----

    [Fact]
    public async Task Explore_CombatSuccess_NoDamage_AdvancesTurn()
    {
        var client = ClientWithRoll(0, Succeed);
        var id = await PartyTestData.CreatePartyAsync(client);

        using var t1 = await TurnAsync(client, id);

        t1.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
        t1.RootElement.GetProperty("roomType").GetString().Should().Be("combat");
        t1.RootElement.GetProperty("damage").GetInt32().Should().Be(0);
        t1.RootElement.GetProperty("goldFound").GetInt32().Should().Be(0);
        // Two-phase contract: mechanics return immediately; narration arrives later via NarrationReady.
        t1.RootElement.TryGetProperty("lastOutcome", out _).Should().BeFalse();
        t1.RootElement.GetProperty("narrationPending").GetBoolean().Should().BeTrue();
        t1.RootElement.TryGetProperty("scenario", out _).Should().BeFalse();

        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
        state.RootElement.GetProperty("members").EnumerateArray()
            .Should().OnlyContain(m => m.GetProperty("hp").GetInt32() == m.GetProperty("maxHp").GetInt32());
    }

    [Fact]
    public async Task Explore_CombatFailure_EveryFailedMemberTakesTheirOwnSixPlusHit()
    {
        var client = ClientWithRoll(0, Fail);
        var id = await PartyTestData.CreatePartyAsync(client);

        using var t1 = await TurnAsync(client, id);
        t1.RootElement.GetProperty("roomType").GetString().Should().Be("combat");
        t1.RootElement.GetProperty("targetName").GetString().Should().Be("Conan");
        t1.RootElement.GetProperty("damage").GetInt32().Should().Be(6);
        t1.RootElement.GetProperty("hits").EnumerateArray()
            .Select(h => (h.GetProperty("name").GetString(), h.GetProperty("damage").GetInt32()))
            .Should().Equal(("Conan", 6), ("Merlin", 6), ("Mercy", 6));
        t1.RootElement.GetProperty("goldFound").GetInt32().Should().Be(0);
        t1.RootElement.GetProperty("isPartyDefeated").GetBoolean().Should().BeFalse();
        t1.RootElement.GetProperty("flavorEvent").GetString().Should().NotBeNullOrWhiteSpace();

        using var t2 = await TurnAsync(client, id);
        t2.RootElement.GetProperty("damage").GetInt32().Should().Be(7);
        t2.RootElement.GetProperty("gold").GetInt32().Should().Be(0);

        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        Member(state, 0).GetProperty("hp").GetInt32().Should().Be(17);
        Member(state, 1).GetProperty("hp").GetInt32().Should().Be(3);
        Member(state, 2).GetProperty("hp").GetInt32().Should().Be(7);
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
    }

    [Fact]
    public async Task Explore_ChestFailure_FindsNoGoldAndTrapsEachFailedMember_AndAdvancesTurn()
    {
        var client = ClientWithRoll(1, Fail);
        var id = await PartyTestData.CreatePartyAsync(client);

        using var t1 = await TurnAsync(client, id);

        t1.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
        t1.RootElement.GetProperty("goldFound").GetInt32().Should().Be(0);
        t1.RootElement.GetProperty("damage").GetInt32().Should().Be(4);
        t1.RootElement.GetProperty("gold").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Explore_FailingEveryTurn_DownsTheSquishyHeroesFirst_ThenTheWholeParty()
    {
        var client = ClientWithRoll(0, Fail);
        var id = await PartyTestData.CreatePartyAsync(client);

        // Hits are 6,7,8,7 then 8: Merlin (16) and Mercy (20) are down on turn 3; Conan (30) falls on turn 5.
        var hits = new List<int>();
        JsonDocument? last = null;
        for (var i = 0; i < 4; i++)
        {
            last?.Dispose();
            last = await TurnAsync(client, id);
            hits.Add(last.RootElement.GetProperty("damage").GetInt32());
            last.RootElement.GetProperty("isPartyDefeated").GetBoolean().Should().BeFalse();
        }

        last!.Dispose();
        hits.Should().Equal(6, 7, 8, 7);

        using var fourth = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        Member(fourth, 0).GetProperty("hp").GetInt32().Should().Be(2);
        Member(fourth, 1).GetProperty("isDefeated").GetBoolean().Should().BeTrue();
        Member(fourth, 2).GetProperty("isDefeated").GetBoolean().Should().BeTrue();

        using var fifth = await TurnAsync(client, id);
        fifth.RootElement.GetProperty("targetName").GetString().Should().Be("Conan");
        fifth.RootElement.GetProperty("damage").GetInt32().Should().Be(2);
        fifth.RootElement.GetProperty("isPartyDefeated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Explore_LastMemberReachesZero_ReportsPartyDefeatedWithClampedDamage()
    {
        var client = ClientWithRoll(0, Fail);
        var id = await CreateSoloMagePartyAsync(client);

        // Mage 16 HP: damage 6,7 = 13 -> 3 HP left; turn 3 would deal 8 but clamps to 3.
        var damages = new List<int>();
        JsonDocument? last = null;
        for (var i = 0; i < 3; i++)
        {
            last?.Dispose();
            last = await TurnAsync(client, id);
            damages.Add(last.RootElement.GetProperty("damage").GetInt32());
        }

        using (last)
        {
            damages.Should().Equal(6, 7, 3);
            last!.RootElement.GetProperty("isPartyDefeated").GetBoolean().Should().BeTrue();
        }

        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("isPartyDefeated").GetBoolean().Should().BeTrue();
        Member(state, 0).GetProperty("hp").GetInt32().Should().Be(0);
        Member(state, 0).GetProperty("isDefeated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Explore_DefeatedParty_ReturnsConflictForPromptAndResolve_AndDoesNotAdvanceTurn()
    {
        var client = ClientWithRoll(0, Fail);
        var id = await CreateSoloMagePartyAsync(client);
        for (var i = 0; i < 3; i++)
        {
            using var _ = await TurnAsync(client, id);
        }

        (await PromptAsync(client, id)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ResolveAsync(client, id)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(3);
    }

    // ---- Concurrency ----

    [Fact]
    public async Task Explore_ConcurrentPrompts_AllReturnTheSamePendingScenario_AndTurnStaysZero()
    {
        var client = ClientWithRoll(1);
        var id = await PartyTestData.CreatePartyAsync(client);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PromptAsync(client, id)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        var scenarios = new HashSet<string>();
        foreach (var r in responses)
        {
            using var doc = await ReadAsync(r);
            if (doc.RootElement.TryGetProperty("scenario", out var s))
            {
                scenarios.Add(s.GetString()!);
            }
        }

        scenarios.Should().HaveCount(1);
        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Explore_MalformedBody_ActionsAsString_IsRejected()
    {
        var client = ClientWithRoll(0);
        var id = await PartyTestData.CreatePartyAsync(client);

        var response = await client.PostAsJsonAsync($"/api/parties/{id}/explore", new { actions = "abc" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

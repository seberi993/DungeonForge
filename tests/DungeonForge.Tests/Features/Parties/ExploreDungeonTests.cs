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
    public async Task Explore_DefeatedParty_ReturnsConflictForPromptAndResolve_AndDoesNotAdvanceTurn()
    {
        var client = ClientWithRoll(0, Fail);
        var id = await CreateSoloMagePartyAsync(client);
        for (var i = 0; i < 6; i++)
        {
            using var _ = await TurnAsync(client, id);
        }

        (await PromptAsync(client, id)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ResolveAsync(client, id)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(6);
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

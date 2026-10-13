using System.Net;
using System.Text.Json;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DungeonForge.Tests.Features.Parties;

public sealed class ExploreDungeonTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private sealed class FixedRoller(int value) : IRoller
    {
        public int Next(int max) => value;
    }

    private readonly List<WebApplicationFactory<Program>> _derived = [];

    public void Dispose() => _derived.ForEach(f => f.Dispose());

    private HttpClient ClientWithRoll(int value)
    {
        var derived = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IRoller>(new FixedRoller(value));
        }));
        _derived.Add(derived);
        return derived.CreateClient();
    }

    private static Task<Guid> CreateSoloMagePartyAsync(HttpClient client) =>
        PartyTestData.CreatePartyAsync(client, members: [new { name = "Merlin", @class = "mage" }]);

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync());

    private static JsonElement Member(JsonDocument doc, int index) =>
        doc.RootElement.GetProperty("members")[index];

    [Fact]
    public async Task Explore_ExistingParty_IncrementsTurnAndMutatesState()
    {
        var client = ClientWithRoll(0);
        var id = await PartyTestData.CreatePartyAsync(client);

        var first = await client.PostAsync($"/api/parties/{id}/explore", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        using var firstDoc = await ReadAsync(first);
        firstDoc.RootElement.GetProperty("turn").GetInt32().Should().Be(1);

        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
        Member(state, 0).GetProperty("hp").GetInt32().Should().Be(28);

        var second = await client.PostAsync($"/api/parties/{id}/explore", null);
        using var secondDoc = await ReadAsync(second);
        secondDoc.RootElement.GetProperty("turn").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Explore_UnknownPartyId_ReturnsNotFound()
    {
        var response = await factory.CreateClient().PostAsync($"/api/parties/{Guid.NewGuid()}/explore", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Explore_CombatRoll_DamagesWarriorByTwoPlusTurnMod3()
    {
        var client = ClientWithRoll(0);
        var id = await PartyTestData.CreatePartyAsync(client);

        using var t1 = await ReadAsync(await client.PostAsync($"/api/parties/{id}/explore", null));
        t1.RootElement.GetProperty("roomType").GetString().Should().Be("combat");
        t1.RootElement.GetProperty("targetName").GetString().Should().Be("Conan");
        t1.RootElement.GetProperty("damage").GetInt32().Should().Be(2);
        t1.RootElement.GetProperty("goldFound").GetInt32().Should().Be(0);
        t1.RootElement.GetProperty("isPartyDefeated").GetBoolean().Should().BeFalse();
        t1.RootElement.GetProperty("flavorEvent").GetString().Should().NotBeNullOrWhiteSpace();

        using var t2 = await ReadAsync(await client.PostAsync($"/api/parties/{id}/explore", null));
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

        using var doc = await ReadAsync(await client.PostAsync($"/api/parties/{id}/explore", null));

        doc.RootElement.TryGetProperty("hp", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("hpChange", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Explore_ChestRoll_AddsGoldFivePlusTurnAndDealsNoDamage()
    {
        var client = ClientWithRoll(1);
        var id = await PartyTestData.CreatePartyAsync(client);

        using var t1 = await ReadAsync(await client.PostAsync($"/api/parties/{id}/explore", null));
        t1.RootElement.GetProperty("roomType").GetString().Should().Be("chest");
        t1.RootElement.GetProperty("damage").GetInt32().Should().Be(0);
        t1.RootElement.GetProperty("goldFound").GetInt32().Should().Be(5);
        t1.RootElement.GetProperty("gold").GetInt32().Should().Be(5);

        using var t2 = await ReadAsync(await client.PostAsync($"/api/parties/{id}/explore", null));
        t2.RootElement.GetProperty("goldFound").GetInt32().Should().Be(6);
        t2.RootElement.GetProperty("gold").GetInt32().Should().Be(11);

        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("gold").GetInt32().Should().Be(11);
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(2);
        state.RootElement.GetProperty("members").EnumerateArray()
            .Should().OnlyContain(m => m.GetProperty("hp").GetInt32() == m.GetProperty("maxHp").GetInt32());
    }

    [Fact]
    public async Task Explore_CombatUntilWarriorDown_FallsThroughToNextMember()
    {
        var client = ClientWithRoll(0);
        var id = await PartyTestData.CreatePartyAsync(client);

        // Warrior (30 HP) absorbs 2,3,4 x3 = 27 over 9 turns (3 left); turn 10 deals 2 (1 left); turn 11 would deal 3 but clamps to 1 (0 left).
        var targets = new List<string?>();
        var damages = new List<int>();
        for (var i = 0; i < 11; i++)
        {
            using var doc = await ReadAsync(await client.PostAsync($"/api/parties/{id}/explore", null));
            targets.Add(doc.RootElement.GetProperty("targetName").GetString());
            damages.Add(doc.RootElement.GetProperty("damage").GetInt32());
        }

        targets.Should().OnlyContain(t => t == "Conan");
        damages.Should().Equal(2, 3, 4, 2, 3, 4, 2, 3, 4, 2, 1);

        using var twelfth = await ReadAsync(await client.PostAsync($"/api/parties/{id}/explore", null));
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
        var client = ClientWithRoll(0);
        var id = await CreateSoloMagePartyAsync(client);

        // Mage 16 HP: damage 2,3,4,2,3 = 14 -> 2 HP left; turn 6 would deal 4 but clamps to 2.
        var damages = new List<int>();
        JsonDocument? last = null;
        for (var i = 0; i < 6; i++)
        {
            last?.Dispose();
            last = await ReadAsync(await client.PostAsync($"/api/parties/{id}/explore", null));
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
    public async Task Explore_DefeatedParty_ReturnsConflictAndDoesNotAdvanceTurn()
    {
        var client = ClientWithRoll(0);
        var id = await CreateSoloMagePartyAsync(client);
        for (var i = 0; i < 6; i++)
        {
            (await client.PostAsync($"/api/parties/{id}/explore", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var response = await client.PostAsync($"/api/parties/{id}/explore", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(6);
    }

    [Fact]
    public async Task Explore_ThirdCombat_DealsFourDamage()
    {
        var client = ClientWithRoll(0);
        var id = await PartyTestData.CreatePartyAsync(client);

        JsonDocument? last = null;
        for (var i = 0; i < 3; i++)
        {
            last?.Dispose();
            last = await ReadAsync(await client.PostAsync($"/api/parties/{id}/explore", null));
        }

        using (last)
        {
            last!.RootElement.GetProperty("damage").GetInt32().Should().Be(4);
            last.RootElement.GetProperty("targetName").GetString().Should().Be("Conan");
        }

        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        Member(state, 0).GetProperty("hp").GetInt32().Should().Be(21);
    }

    [Fact]
    public async Task Explore_ConcurrentRequests_DoNotLoseTurns()
    {
        var client = ClientWithRoll(1);
        var id = await PartyTestData.CreatePartyAsync(client);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => client.PostAsync($"/api/parties/{id}/explore", null)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(20);
    }

    [Fact]
    public async Task Explore_ConcurrentCombat_AppliesEveryHitToMembers()
    {
        var client = ClientWithRoll(0);
        var id = await PartyTestData.CreatePartyAsync(client);

        var responses = await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => client.PostAsync($"/api/parties/{id}/explore", null)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        Member(state, 0).GetProperty("hp").GetInt32().Should().Be(21);
    }
}

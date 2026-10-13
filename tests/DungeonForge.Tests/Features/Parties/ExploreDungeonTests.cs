using System.Net;
using System.Net.Http.Json;
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


    private static async Task<Guid> CreatePartyAsync(HttpClient client)
    {
        var created = await client.PostAsJsonAsync("/api/parties", new { name = "Heroes" });
        using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("partyId").GetGuid();
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync());

    [Fact]
    public async Task Explore_ExistingParty_IncrementsTurnAndMutatesState()
    {
        var client = ClientWithRoll(0);
        var id = await CreatePartyAsync(client);

        var first = await client.PostAsync($"/api/parties/{id}/explore", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        using var firstDoc = await ReadAsync(first);
        firstDoc.RootElement.GetProperty("turn").GetInt32().Should().Be(1);

        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
        state.RootElement.GetProperty("hp").GetInt32().Should().Be(18);

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
    public async Task Explore_CombatRoll_ReducesHpByTwoPlusTurnMod3()
    {
        var client = ClientWithRoll(0);
        var id = await CreatePartyAsync(client);

        using var t1 = await ReadAsync(await client.PostAsync($"/api/parties/{id}/explore", null));
        t1.RootElement.GetProperty("roomType").GetString().Should().Be("combat");
        t1.RootElement.GetProperty("hpChange").GetInt32().Should().Be(-2);
        t1.RootElement.GetProperty("goldFound").GetInt32().Should().Be(0);
        t1.RootElement.GetProperty("hp").GetInt32().Should().Be(18);
        t1.RootElement.GetProperty("flavorEvent").GetString().Should().NotBeNullOrWhiteSpace();

        using var t2 = await ReadAsync(await client.PostAsync($"/api/parties/{id}/explore", null));
        t2.RootElement.GetProperty("hpChange").GetInt32().Should().Be(-3);
        t2.RootElement.GetProperty("hp").GetInt32().Should().Be(15);
        t2.RootElement.GetProperty("gold").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Explore_ChestRoll_AddsGoldFivePlusTurn()
    {
        var client = ClientWithRoll(1);
        var id = await CreatePartyAsync(client);

        using var t1 = await ReadAsync(await client.PostAsync($"/api/parties/{id}/explore", null));
        t1.RootElement.GetProperty("roomType").GetString().Should().Be("chest");
        t1.RootElement.GetProperty("hpChange").GetInt32().Should().Be(0);
        t1.RootElement.GetProperty("goldFound").GetInt32().Should().Be(5);
        t1.RootElement.GetProperty("gold").GetInt32().Should().Be(5);

        using var t2 = await ReadAsync(await client.PostAsync($"/api/parties/{id}/explore", null));
        t2.RootElement.GetProperty("goldFound").GetInt32().Should().Be(6);
        t2.RootElement.GetProperty("gold").GetInt32().Should().Be(11);
        t2.RootElement.GetProperty("hp").GetInt32().Should().Be(20);

        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("gold").GetInt32().Should().Be(11);
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Explore_CombatRepeatedly_ClampsHpAtZeroAndReportsClampedChange()
    {
        var client = ClientWithRoll(0);
        var id = await CreatePartyAsync(client);

        // Damage per turn: 2,3,4,2,3,4 (=18) leaves 2 HP; turn 7 would deal 3 but clamps to -2.
        var hpChanges = new List<int>();
        for (var i = 0; i < 7; i++)
        {
            using var doc = await ReadAsync(await client.PostAsync($"/api/parties/{id}/explore", null));
            hpChanges.Add(doc.RootElement.GetProperty("hpChange").GetInt32());
        }

        hpChanges.Should().Equal(-2, -3, -4, -2, -3, -4, -2);
        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("hp").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Explore_DefeatedParty_ReturnsConflictAndDoesNotAdvanceTurn()
    {
        var client = ClientWithRoll(0);
        var id = await CreatePartyAsync(client);
        for (var i = 0; i < 7; i++)
        {
            (await client.PostAsync($"/api/parties/{id}/explore", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var response = await client.PostAsync($"/api/parties/{id}/explore", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task Explore_ThirdCombat_DealsFourDamage()
    {
        var client = ClientWithRoll(0);
        var id = await CreatePartyAsync(client);

        JsonDocument? last = null;
        for (var i = 0; i < 3; i++)
        {
            last?.Dispose();
            last = await ReadAsync(await client.PostAsync($"/api/parties/{id}/explore", null));
        }

        using (last)
        {
            last!.RootElement.GetProperty("hpChange").GetInt32().Should().Be(-4);
            last.RootElement.GetProperty("hp").GetInt32().Should().Be(11);
        }
    }

    [Fact]
    public async Task Explore_ConcurrentRequests_DoNotLoseTurns()
    {
        var client = ClientWithRoll(1);
        var id = await CreatePartyAsync(client);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => client.PostAsync($"/api/parties/{id}/explore", null)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(20);
    }
}

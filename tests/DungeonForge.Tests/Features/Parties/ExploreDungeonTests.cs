using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DungeonForge.Tests.Features.Parties;

public class ExploreDungeonTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private sealed class FixedRoller(int value) : IRoller
    {
        public int Next(int max) => value;
    }

    private HttpClient ClientWithRoll(int value) =>
        factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IRoller>(new FixedRoller(value));
        })).CreateClient();

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
        var client = factory.CreateClient();
        var id = await CreatePartyAsync(client);

        var first = await client.PostAsync($"/api/parties/{id}/explore", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        using var firstDoc = await ReadAsync(first);
        firstDoc.RootElement.GetProperty("turn").GetInt32().Should().Be(1);

        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        state.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
        (state.RootElement.GetProperty("hp").GetInt32() != 20 || state.RootElement.GetProperty("gold").GetInt32() != 0)
            .Should().BeTrue();

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
    public async Task Explore_CombatRepeatedly_ClampsHpAtZero()
    {
        var client = ClientWithRoll(0);
        var id = await CreatePartyAsync(client);

        JsonDocument? last = null;
        for (var i = 0; i < 12; i++)
        {
            last?.Dispose();
            last = await ReadAsync(await client.PostAsync($"/api/parties/{id}/explore", null));
        }

        using (last)
        {
            last!.RootElement.GetProperty("hp").GetInt32().Should().Be(0);
        }
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Infrastructure.Narration;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DungeonForge.Tests.Features.Parties;

public sealed class ExploreDungeonNarrationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private sealed class FixedRoller(int value) : IRoller
    {
        public int Next(int max) => value;
    }

    private sealed class FakeNarrator(string text) : IDungeonNarrator
    {
        public NarrationContext? Seen { get; private set; }

        public Task<string> NarrateAsync(NarrationContext ctx, CancellationToken ct)
        {
            Seen = ctx;
            return Task.FromResult(text);
        }
    }

    [Fact]
    public async Task Explore_UsesNarratorTextAsFlavorEvent_AndLeavesMathUnchanged()
    {
        var narrator = new FakeNarrator("The dark hums. Something watches.");
        using var derived = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IRoller>(new FixedRoller(0));
            s.AddSingleton<IDungeonNarrator>(narrator);
        }));
        var client = derived.CreateClient();
        var created = await client.PostAsJsonAsync("/api/parties", new { name = "Heroes" });
        using var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdDoc.RootElement.GetProperty("partyId").GetGuid();

        var response = await client.PostAsync($"/api/parties/{id}/explore", null);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("flavorEvent").GetString().Should().Be("The dark hums. Something watches.");
        root.GetProperty("roomType").GetString().Should().Be("combat");
        root.GetProperty("hpChange").GetInt32().Should().Be(-2);
        root.GetProperty("hp").GetInt32().Should().Be(18);
        root.GetProperty("gold").GetInt32().Should().Be(0);
        narrator.Seen.Should().NotBeNull();
        narrator.Seen!.Room.Should().Be(RoomType.Combat);
        narrator.Seen.PartyName.Should().Be("Heroes");
    }
}

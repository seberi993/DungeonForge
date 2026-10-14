using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Infrastructure.Narration;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using static DungeonForge.Tests.Features.Parties.ExploreTestSupport;

namespace DungeonForge.Tests.Features.Parties;

public sealed class ExploreDungeonFloorTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const int CombatRoom = 0;

    // Success roll is switchable so a test can clear the floor's rooms, then change how the boss round goes.
    private sealed class SwitchRoller : IRoller
    {
        public volatile int Success = Succeed;

        public int Next(int max) => max == 3 ? CombatRoom : Success;
    }

    private sealed class StubNarrator : IDungeonNarrator
    {
        public Task<string> GenerateScenarioAsync(NarrationContext ctx, CancellationToken ct) =>
            Task.FromResult("A cavern yawns. What do you do?");

        public Task<TurnNarration> NarrateTurnAsync(bool success, NarrationContext ctx, CancellationToken ct) =>
            Task.FromResult(new TurnNarration("It ends.", "The way ahead. What do you do?"));
    }

    private (HttpClient Client, SwitchRoller Roller, IDisposable Scope) Arrange()
    {
        var roller = new SwitchRoller();
        var derived = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IRoller>(roller);
            s.AddSingleton<IDungeonNarrator>(new StubNarrator());
            s.AddSingleton<IPartyNotifier>(new CapturingNotifier());
        }));
        return (derived.CreateClient(), roller, derived);
    }

    private static async Task ClearOrdinaryRoomsAsync(HttpClient client, Guid id)
    {
        for (var i = 0; i < Party.RoomsPerFloor; i++)
        {
            using var turn = await TurnAsync(client, id);
            turn.RootElement.GetProperty("floorCleared").GetBoolean().Should().BeFalse();
        }
    }

    [Fact]
    public async Task AfterTheFloorsOrdinaryRooms_TheNextRoomIsTheBoss()
    {
        var (client, _, scope) = Arrange();
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);

        await ClearOrdinaryRoomsAsync(client, id);
        using var prompt = await PromptWhenReadyAsync(client, id);

        prompt.RootElement.GetProperty("roomType").GetString().Should().Be("boss");
    }

    [Fact]
    public async Task Resolve_LastOrdinaryRoom_ReportsAwakeBossHp()
    {
        var (client, _, scope) = Arrange();
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        for (var i = 0; i < Party.RoomsPerFloor - 1; i++) (await TurnAsync(client, id)).Dispose();

        using var last = await TurnAsync(client, id);

        last.RootElement.GetProperty("bossMaxHp").GetInt32().Should().Be(Party.BossHpFor(1));
        last.RootElement.GetProperty("bossHp").GetInt32().Should().Be(Party.BossHpFor(1));
    }

    [Fact]
    public async Task BossRound_WhereEveryoneMisses_LeavesTheBossStandingAndOnTheNextTurn()
    {
        var (client, roller, scope) = Arrange();
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        await ClearOrdinaryRoomsAsync(client, id);
        roller.Success = Fail;

        using var round = await TurnAsync(client, id);

        round.RootElement.GetProperty("roomType").GetString().Should().Be("boss");
        round.RootElement.GetProperty("floorCleared").GetBoolean().Should().BeFalse();
        round.RootElement.GetProperty("bossHp").GetInt32().Should().Be(Party.BossHpFor(1));
        round.RootElement.GetProperty("goldFound").GetInt32().Should().Be(0);
        round.RootElement.GetProperty("floor").GetInt32().Should().Be(1);
        using var next = await PromptWhenReadyAsync(client, id);
        next.RootElement.GetProperty("roomType").GetString().Should().Be("boss");
    }

    [Fact]
    public async Task BossRound_WhereEveryonePasses_FellsTheBossAndDescends()
    {
        var (client, _, scope) = Arrange();
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        await ClearOrdinaryRoomsAsync(client, id);

        using var round = await TurnAsync(client, id);

        round.RootElement.GetProperty("roomType").GetString().Should().Be("boss");
        round.RootElement.GetProperty("floorCleared").GetBoolean().Should().BeTrue();
        round.RootElement.GetProperty("floor").GetInt32().Should().Be(2);
        round.RootElement.GetProperty("goldFound").GetInt32().Should().Be(25);
        round.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        using var next = await PromptWhenReadyAsync(client, id);
        next.RootElement.GetProperty("roomType").GetString().Should().Be("combat");
    }
}

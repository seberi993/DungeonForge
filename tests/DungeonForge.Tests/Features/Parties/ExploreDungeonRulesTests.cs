using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Parties.ExploreDungeon;
using DungeonForge.Application.Features.Parties.Realtime;
using DungeonForge.Application.Narration;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using static DungeonForge.Tests.Features.Parties.ExploreDungeonNarrationTests;
using static DungeonForge.Tests.Support.PartyTestBuilders;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Parties;

// Characterizes the turn math that lives in the explore handler (fail damage, gold, the wounded flag) before it moves to
// Domain/Rules. Expected numbers are written out by hand, not computed with the formulas, so a move or rewrite that changes
// one fails here. A floor is PartyRules.RoomsPerFloor (4) ordinary rooms then a boss: the boss is turn 4 on floor 1 and turn 9 on floor 2.
public sealed class ExploreDungeonRulesTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const int Combat = 0;
    private const int Chest = 1;
    private const int Puzzle = 2;

    private readonly List<WebApplicationFactory<Program>> _derived = [];

    public void Dispose() => _derived.ForEach(f => f.Dispose());

    // Every non-boss room is `Room`; the narrator answers at once  the success roll can be flipped between turns (turns advance whether or not they succeed).
    private sealed class SwitchRoller(int room) : IRoller
    {
        public volatile int Success = Succeed;

        public int Next(int max) => max == 3 ? room : Success;
    }

    private async Task<(HttpClient Client, Guid Id, SwitchRoller Roller)> NewPartyAsync(int room, IDungeonNarrator? narrator = null,
        object[]? members = null)
    {
        var roller = new SwitchRoller(room);
        var derived = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IRoller>(roller);
            s.AddSingleton<IPartyNotifier>(new CapturingNotifier());
            s.AddSingleton(narrator ?? new FakeNarrator());
        }));
        _derived.Add(derived);
        var client = derived.CreateClient();
        return (client, await PartyTestData.CreatePartyAsync(client, members: members), roller);
    }

    // Succeeds every turn before `turn` (clearing floor 1's boss on the way), then plays `turn` with the given roll.
    private async Task<System.Text.Json.JsonDocument> PlayTurnAsync(int room, int turn, int rollAtTurn)
    {
        var (client, id, roller) = await NewPartyAsync(room);
        for (var i = 0; i < turn; i++)
        {
            using var _ = await TurnAsync(client, id);
        }

        roller.Success = rollAtTurn;
        return await TurnAsync(client, id);
    }

    [Theory]
    // Combat 6 + turn % 3 + turn / 3
    [InlineData(Combat, 0, 6)]
    [InlineData(Combat, 1, 7)]
    [InlineData(Combat, 2, 8)]
    [InlineData(Combat, 3, 7)]
    [InlineData(Combat, 5, 9)]
    [InlineData(Combat, 6, 8)]
    [InlineData(Combat, 7, 9)]
    [InlineData(Combat, 8, 10)]
    // Puzzle 7 + turn % 3 + turn / 3
    [InlineData(Puzzle, 0, 7)]
    [InlineData(Puzzle, 1, 8)]
    [InlineData(Puzzle, 2, 9)]
    [InlineData(Puzzle, 3, 8)]
    [InlineData(Puzzle, 5, 10)]
    [InlineData(Puzzle, 6, 9)]
    [InlineData(Puzzle, 7, 10)]
    [InlineData(Puzzle, 8, 11)]
    // Chest 4 + turn / 3 (no turn % 3 term)
    [InlineData(Chest, 0, 4)]
    [InlineData(Chest, 1, 4)]
    [InlineData(Chest, 2, 4)]
    [InlineData(Chest, 3, 5)]
    [InlineData(Chest, 5, 5)]
    [InlineData(Chest, 6, 6)]
    [InlineData(Chest, 7, 6)]
    [InlineData(Chest, 8, 6)]
    public async Task FailedAction_CostsTheRoomsDamageForThatTurn(int room, int turn, int expectedDamage)
    {
        using var played = await PlayTurnAsync(room, turn, Fail);

        played.RootElement.GetProperty("turn").GetInt32().Should().Be(turn + 1);
        played.RootElement.GetProperty("roomType").GetString().Should().NotBe("boss");
        played.RootElement.GetProperty("damage").GetInt32().Should().Be(expectedDamage);
    }

    [Theory]
    // Chest 5 + turn, puzzle 10 + 2 * turn, combat pays nothing.
    [InlineData(Chest, 0, 5)]
    [InlineData(Chest, 1, 6)]
    [InlineData(Chest, 2, 7)]
    [InlineData(Chest, 3, 8)]
    [InlineData(Chest, 5, 10)]
    [InlineData(Chest, 8, 13)]
    [InlineData(Puzzle, 0, 10)]
    [InlineData(Puzzle, 1, 12)]
    [InlineData(Puzzle, 2, 14)]
    [InlineData(Puzzle, 3, 16)]
    [InlineData(Puzzle, 5, 20)]
    [InlineData(Puzzle, 8, 26)]
    [InlineData(Combat, 0, 0)]
    [InlineData(Combat, 5, 0)]
    public async Task SuccessfulRoom_PaysTheRoomsGoldForThatTurn(int room, int turn, int expectedGold)
    {
        using var played = await PlayTurnAsync(room, turn, Succeed);

        played.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        played.RootElement.GetProperty("goldFound").GetInt32().Should().Be(expectedGold);
    }

    [Fact]
    public async Task BossRound_FailedAction_HitsHarderPerFloor_AndPaysGoldOnlyWhenTheBossFalls()
    {
        var (client, id, roller) = await NewPartyAsync(Combat);
        for (var turn = 0; turn <= 4; turn++)
        {
            using var _ = await TurnAsync(client, id); // floor 1: four rooms, then the boss falls to three passes (30 hp)
        }

        for (var turn = 5; turn <= 8; turn++)
        {
            using var _ = await TurnAsync(client, id); // floor 2's four rooms
        }

        roller.Success = Fail;
        using var floorTwoMiss = await TurnAsync(client, id); // boss, turn 9: 8 + 2 * (floor - 1) + turn % 3 = 10

        floorTwoMiss.RootElement.GetProperty("roomType").GetString().Should().Be("boss");
        floorTwoMiss.RootElement.GetProperty("floor").GetInt32().Should().Be(2);
        floorTwoMiss.RootElement.GetProperty("damage").GetInt32().Should().Be(10);
        floorTwoMiss.RootElement.GetProperty("goldFound").GetInt32().Should().Be(0);

        roller.Success = Succeed;
        var rounds = 0;
        while (true)
        {
            using var round = await TurnAsync(client, id); // 40 boss hp, 30 per round of three passes: two rounds
            rounds++;
            if (round.RootElement.GetProperty("floorCleared").GetBoolean())
            {
                round.RootElement.GetProperty("goldFound").GetInt32().Should().Be(50); // 25 * floor
                break;
            }

            round.RootElement.GetProperty("goldFound").GetInt32().Should().Be(0);
            rounds.Should().BeLessThan(5);
        }

        rounds.Should().Be(2);
    }

    [Fact]
    public async Task BossRound_OnFloorOne_FailedAction_CostsNine()
    {
        var (client, id, roller) = await NewPartyAsync(Combat);
        for (var turn = 0; turn <= 3; turn++)
        {
            using var _ = await TurnAsync(client, id);
        }

        roller.Success = Fail;
        using var miss = await TurnAsync(client, id); // boss, turn 4: 8 + 0 + 4 % 3 = 9

        miss.RootElement.GetProperty("roomType").GetString().Should().Be("boss");
        miss.RootElement.GetProperty("damage").GetInt32().Should().Be(9);
    }

    // Wounded means total hp at or below half of total max hp: the flag the narrator is told. A solo mage has 16 hp and a failed
    // chest costs 4 on turns 0 and 1, so the party sits at 12 (not wounded) and then exactly 8 (wounded: the boundary counts).
    [Fact]
    public async Task PartyWounded_IsTrueAtExactlyHalfHp_AndFalseAbove()
    {
        var narrator = new FakeNarrator();
        var (client, id, roller) = await NewPartyAsync(Chest, narrator, members: [new { name = "Merlin", @class = "mage" }]);
        roller.Success = Fail;

        using (await TurnAsync(client, id))
        {
        }

        await WaitForTurnCallsAsync(narrator, 1);
        narrator.SeenTurnContext!.PartyWounded.Should().BeFalse("12 of 16 hp is above half");

        using (await TurnAsync(client, id))
        {
        }

        await WaitForTurnCallsAsync(narrator, 2);
        narrator.SeenTurnContext!.PartyWounded.Should().BeTrue("8 of 16 hp is exactly half");
    }

    [Fact]
    public async Task FirstScenario_OfAFullHpParty_IsNotWounded()
    {
        var narrator = new FakeNarrator();
        var (client, id, _) = await NewPartyAsync(Combat, narrator);

        (await PromptAsync(client, id)).EnsureSuccessStatusCode();

        narrator.SeenScenarioContext!.PartyWounded.Should().BeFalse();
    }

    private static async Task WaitForTurnCallsAsync(FakeNarrator narrator, int calls)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (narrator.TurnCalls < calls)
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the narrator should have been called for the resolved turn");
            await Task.Delay(10);
        }
    }
}

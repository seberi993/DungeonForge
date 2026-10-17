using System.Text.Json;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Scores;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using DungeonForge.Tests.Features.Parties;
using static DungeonForge.Tests.Support.PartyTestBuilders;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Scores;

public sealed class HighScoreTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static RunScore Score(int floor, int gold, int turns, string name = "P") =>
        new(Guid.NewGuid(), name, ["A"], floor, turns, gold, DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task InMemoryBoard_RanksByFloorThenGoldThenTurns()
    {
        var board = new InMemoryScoreBoard();
        await board.RecordAsync(Score(1, 500, 9, "low-floor"));
        await board.RecordAsync(Score(2, 10, 5, "deep-poor"));
        await board.RecordAsync(Score(2, 90, 3, "deep-rich"));
        await board.RecordAsync(Score(2, 90, 8, "deep-rich-long"));

        var top = await board.TopAsync(10);

        top.Select(s => s.PartyName).Should().Equal("deep-rich-long", "deep-rich", "deep-poor", "low-floor");
    }

    [Fact]
    public async Task InMemoryBoard_SameRunTwice_KeepsOneEntry()
    {
        var board = new InMemoryScoreBoard();
        var score = Score(1, 5, 2);
        await board.RecordAsync(score);
        await board.RecordAsync(score with { Gold = 7 });

        (await board.TopAsync(10)).Should().ContainSingle().Which.Gold.Should().Be(7);
    }

    [Fact]
    public async Task InMemoryBoard_OverCapacity_DropsWorstRun()
    {
        var board = new InMemoryScoreBoard();
        for (var i = 0; i <= InMemoryScoreBoard.Capacity; i++) await board.RecordAsync(Score(1, i, 1));

        var all = await board.TopAsync(1000);

        all.Should().HaveCount(InMemoryScoreBoard.Capacity);
        all.Should().NotContain(s => s.Gold == 0);
    }

    [Fact]
    public async Task Get_Scores_RespectsLimitAndClampsIt()
    {
        var board = new InMemoryScoreBoard();
        for (var i = 0; i < 5; i++) await board.RecordAsync(Score(1, i, 1));
        using var derived = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IScoreBoard>(board)));
        var client = derived.CreateClient();

        using var two = JsonDocument.Parse(await client.GetStringAsync("/api/scores?limit=2"));
        using var bad = JsonDocument.Parse(await client.GetStringAsync("/api/scores?limit=-3"));

        two.RootElement.GetProperty("scores").GetArrayLength().Should().Be(2);
        two.RootElement.GetProperty("scores")[0].GetProperty("rank").GetInt32().Should().Be(1);
        bad.RootElement.GetProperty("scores").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task PartyWipe_RecordsScoreOnce()
    {
        var board = new InMemoryScoreBoard();
        using var derived = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IRoller>(new FixedRoller(0, Fail));
            s.AddSingleton<IScoreBoard>(board);
        }));
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client, members: [new { name = "Merlin", @class = "mage" }]);

        for (var i = 0; i < 3; i++)
        {
            (await PromptWhenReadyAsync(client, id)).Dispose();
            await ResolveAsync(client, id, Act("We charge", "Merlin", null));
        }

        var top = await board.TopAsync(10);
        top.Should().ContainSingle();
        top[0].Id.Should().Be(id);
        top[0].Heroes.Should().Equal("Merlin");
        top[0].Floor.Should().Be(1);
    }
}

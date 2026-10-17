using System.Text.Json;
using DungeonForge.Api.Domain.Rules;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using static DungeonForge.Tests.Support.PartyTestBuilders;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Parties;

public sealed class ExploreDungeonRollsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _derived = [];

    public void Dispose() => _derived.ForEach(f => f.Dispose());

    private async Task<JsonDocument> ResolveTurnAsync(int room, int roll)
    {
        var derived = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddSingleton<IRoller>(new FixedRoller(room, roll))));
        _derived.Add(derived);
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);
        return await TurnAsync(client, id);
    }

    [Theory]
    [InlineData(Succeed, false, false)]
    [InlineData(Fail, false, false)]
    [InlineData(2, true, false)]
    [InlineData(97, false, true)]
    public async Task Resolve_ReportsEachMembersRollChanceAndExtremes(int roll, bool crit, bool fumble)
    {
        using var turn = await ResolveTurnAsync(room: 0, roll);

        var rolls = turn.RootElement.GetProperty("rolls");
        rolls.GetArrayLength().Should().Be(3);
        foreach (var r in rolls.EnumerateArray())
        {
            r.GetProperty("roll").GetInt32().Should().Be(roll);
            r.GetProperty("chance").GetInt32().Should().BeInRange(ActionOdds.MinPercent, ActionOdds.MaxPercent);
            r.GetProperty("crit").GetBoolean().Should().Be(crit);
            r.GetProperty("fumble").GetBoolean().Should().Be(fumble);
        }
    }

    [Fact]
    public async Task Resolve_CritsAddBonusGoldOnASuccessfulRoom()
    {
        // Chest, turn 0: 5 gold, plus 5 for each of the three crits.
        using var turn = await ResolveTurnAsync(room: 1, roll: 2);

        turn.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        turn.RootElement.GetProperty("goldFound").GetInt32().Should().Be(5 + 3 * ActionOdds.CritGold);
    }

    [Fact]
    public async Task Resolve_FumblesHitHarderThanAPlainMiss()
    {
        // Combat, turn 0: a plain miss costs 6; a fumble costs half again as much.
        using var plain = await ResolveTurnAsync(room: 0, roll: Fail);
        using var fumbled = await ResolveTurnAsync(room: 0, roll: 97);

        plain.RootElement.GetProperty("damage").GetInt32().Should().Be(6);
        fumbled.RootElement.GetProperty("damage").GetInt32().Should().Be(9);
    }
}

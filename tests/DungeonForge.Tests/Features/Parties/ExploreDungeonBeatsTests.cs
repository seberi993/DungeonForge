using System.Net;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using static DungeonForge.Tests.Support.PartyTestBuilders;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Parties;

public sealed class ActionVerbsTests
{
    [Theory]
    [InlineData(CharacterClass.Warrior, "We press forward", ActionVerb.Strike)]
    [InlineData(CharacterClass.Paladin, "", ActionVerb.Strike)]
    [InlineData(CharacterClass.Mage, "I stand watch", ActionVerb.Cast)]
    [InlineData(CharacterClass.Necromancer, "I stand watch", ActionVerb.Cast)]
    [InlineData(CharacterClass.Priest, "I stand watch", ActionVerb.Heal)]
    [InlineData(CharacterClass.Druid, "I stand watch", ActionVerb.Heal)]
    [InlineData(CharacterClass.Bard, "I stand watch", ActionVerb.Stab)]
    public void Pick_WithoutKeyword_FallsBackToTheClassDefault(CharacterClass characterClass, string text, ActionVerb expected) =>
        ActionVerbs.Pick(characterClass, text).Should().Be(expected);

    [Theory]
    [InlineData(CharacterClass.Warrior, "I hurl a fireball at it", ActionVerb.Cast)]
    [InlineData(CharacterClass.Mage, "I slip behind it and stab its back", ActionVerb.Stab)]
    [InlineData(CharacterClass.Bard, "I mend Brenna's wounds", ActionVerb.Heal)]
    [InlineData(CharacterClass.Priest, "I charge and cleave it in two", ActionVerb.Strike)]
    [InlineData(CharacterClass.Mage, "I PICK the lock", ActionVerb.Stab)]
    public void Pick_KeywordBeatsTheClassDefault(CharacterClass characterClass, string text, ActionVerb expected) =>
        ActionVerbs.Pick(characterClass, text).Should().Be(expected);

    [Fact]
    public void Pick_EarliestKeywordWins()
    {
        ActionVerbs.Pick(CharacterClass.Warrior, "I attack, then bless the party").Should().Be(ActionVerb.Strike);
        ActionVerbs.Pick(CharacterClass.Warrior, "I bless the party, then attack").Should().Be(ActionVerb.Heal);
    }

    [Fact]
    public void Pick_MatchesWordStartsOnly()
    {
        // "white" contains "hit" but is not a strike word.
        ActionVerbs.Pick(CharacterClass.Mage, "a white light").Should().Be(ActionVerb.Cast);
    }
}

public sealed class ExploreDungeonBeatsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _derived = [];

    public void Dispose() => _derived.ForEach(f => f.Dispose());

    private HttpClient ClientWithRoll(int room, int success)
    {
        var derived = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IRoller>(new FixedRoller(room, success))));
        _derived.Add(derived);
        return derived.CreateClient();
    }

    [Fact]
    public async Task Resolve_ReturnsOneBeatPerAction_WithClassAndVerb()
    {
        var client = ClientWithRoll(room: 0, success: Succeed);
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await ResolveAsync(client, id,
            Act("I charge the goblin", "Conan", ConanId),
            Act("A fireball at its face", "Merlin", MerlinId),
            Act("I mend the wounded", "Mercy", MercyId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await ReadAsync(response);
        var root = doc.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        var beats = root.GetProperty("beats").EnumerateArray().ToList();
        beats.Select(b => b.GetProperty("name").GetString()).Should().Equal("Conan", "Merlin", "Mercy");
        beats.Select(b => b.GetProperty("verb").GetString()).Should().Equal("strike", "cast", "heal");
        beats[0].GetProperty("text").GetString().Should().Be("I charge the goblin");
        beats.Select(b => b.GetProperty("class").GetString()).Should().OnlyContain(c => !string.IsNullOrEmpty(c));
    }

    [Fact]
    public async Task Resolve_FailedRoll_ReportsNoSuccess()
    {
        var client = ClientWithRoll(room: 0, success: Fail);
        var id = await PartyTestData.CreatePartyAsync(client);
        await PromptAsync(client, id);

        using var doc = await ReadAsync(await ResolveAsync(client, id));

        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("beats").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task Prompt_HasNoBeats()
    {
        var client = ClientWithRoll(room: 0, success: Succeed);
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await ReadAsync(await PromptAsync(client, id));

        doc.RootElement.TryGetProperty("beats", out _).Should().BeFalse();
    }
}

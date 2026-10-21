using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Parties.Chaos;
using DungeonForge.Application.Features.Social.Abilities;
using DungeonForge.Application.Features.Social.Core;
using DungeonForge.Tests.Features.Parties;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using static DungeonForge.Tests.Support.PartyTestBuilders;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Social;

public class AbilityInPlaySliceTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    // Combat room, every roll lands on 80: a plain 50% chance fails it, a 90% chance passes it.
    private static SocialHarness Harness(WebApplicationFactory<Program> f) =>
        SocialHarness.Create(f, roller: new FixedRoller(room: 0, success: 80));

    private static Ability Give(Party party, string member, string id, int cooldown, params AbilityEffect[] effects)
    {
        var ability = new Ability(id, "TX", "Test Gift", "utility", "common", [], "A test.", "Steadies your hand; steady to return.",
            new AbilityStats(AbilityBudget.Of(effects, cooldown), cooldown, effects, 1));
        lock (party.SyncRoot)
        {
            party.Social().Profile(member).Abilities.Add(ability);
        }

        return ability;
    }

    private static async Task<JsonDocument> ResolveAsync(SocialHarness h, Guid id, params AbilityIntent[] intents)
    {
        (await PromptWhenReadyAsync(h.Client, id)).Dispose();
        var response = await h.Client.PostAsJsonAsync($"/api/parties/{id}/explore", new { actions = WholeParty(), abilities = intents });
        response.EnsureSuccessStatusCode();
        return await ReadAsync(response);
    }

    private static JsonElement RollOf(JsonDocument turn, string name) =>
        turn.RootElement.GetProperty("rolls").EnumerateArray().Single(r => r.GetProperty("name").GetString() == name);

    [Fact]
    public async Task ARollBonus_LiftsOnlyTheUsersChance_AndTurnsAMissIntoAHit()
    {
        using var h = Harness(factory);
        var id = await PartyTestData.CreatePartyAsync(h.Client);
        var gift = Give(h.Party(id), "Conan", "ab_t", 2, new AbilityEffect(EffectTypes.RollBonus, 40));

        using var turn = await ResolveAsync(h, id, new AbilityIntent(ConanId, gift.Id));

        RollOf(turn, "Conan").GetProperty("chance").GetInt32().Should().Be(90);
        RollOf(turn, "Merlin").GetProperty("chance").GetInt32().Should().Be(50);
        RollOf(turn, "Conan").GetProperty("roll").GetInt32().Should().BeLessThan(90);
    }

    [Fact]
    public async Task ATeamBonus_LiftsEveryoneElse()
    {
        using var h = Harness(factory);
        var id = await PartyTestData.CreatePartyAsync(h.Client);
        var gift = Give(h.Party(id), "Conan", "ab_t", 2, new AbilityEffect(EffectTypes.TeamBonus, 12));

        using var turn = await ResolveAsync(h, id, new AbilityIntent(ConanId, gift.Id));

        RollOf(turn, "Conan").GetProperty("chance").GetInt32().Should().Be(50);
        RollOf(turn, "Merlin").GetProperty("chance").GetInt32().Should().Be(62);
        RollOf(turn, "Mercy").GetProperty("chance").GetInt32().Should().Be(62);
    }

    [Fact]
    public async Task AShield_SoftensTheUsersOwnFailureDamage()
    {
        using var h = Harness(factory);
        var id = await PartyTestData.CreatePartyAsync(h.Client);
        var gift = Give(h.Party(id), "Conan", "ab_t", 2, new AbilityEffect(EffectTypes.Shield, 50));

        using var turn = await ResolveAsync(h, id, new AbilityIntent(ConanId, gift.Id));

        // Combat, turn 0: a plain miss costs 6; half of that is 3. The others are unshielded.
        var hits = turn.RootElement.GetProperty("hits").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!, x => x.GetProperty("damage").GetInt32());
        hits["Conan"].Should().Be(3);
        hits["Merlin"].Should().Be(6);
    }

    [Fact]
    public async Task AGift_GoesOnCooldown_AndTheNextTurnItsBonusIsGone()
    {
        using var h = Harness(factory);
        var id = await PartyTestData.CreatePartyAsync(h.Client);
        var gift = Give(h.Party(id), "Conan", "ab_t", 3, new AbilityEffect(EffectTypes.RollBonus, 40));
        using var first = await ResolveAsync(h, id, new AbilityIntent(ConanId, gift.Id));

        // Wait for the next scenario, then try again on turn 1: cooldown 3 means it is not ready until turn 3.
        using var second = await ResolveAsync(h, id, new AbilityIntent(ConanId, gift.Id));

        RollOf(first, "Conan").GetProperty("chance").GetInt32().Should().Be(90);
        RollOf(second, "Conan").GetProperty("chance").GetInt32().Should().Be(50);
        h.PrivateTexts("Conan", PrivateKinds.Narrator).Should().Contain("Test Gift is still recovering.");
    }

    [Fact]
    public async Task OnlyTheOwnerCanUseAGift_AndUnknownGiftsAreIgnored()
    {
        using var h = Harness(factory);
        var id = await PartyTestData.CreatePartyAsync(h.Client);
        var gift = Give(h.Party(id), "Conan", "ab_t", 2, new AbilityEffect(EffectTypes.RollBonus, 40));

        using var turn = await ResolveAsync(h, id, new AbilityIntent(MerlinId, gift.Id), new AbilityIntent(MercyId, "ab_nope"));

        turn.RootElement.GetProperty("rolls").EnumerateArray().Should().OnlyContain(r => r.GetProperty("chance").GetInt32() == 50);
    }

    [Fact]
    public async Task TheTurnBroadcast_NeverNamesTheGiftThatWasUsed()
    {
        using var h = Harness(factory);
        var id = await PartyTestData.CreatePartyAsync(h.Client);
        var gift = Give(h.Party(id), "Conan", "ab_secret_gift", 2, new AbilityEffect(EffectTypes.RollBonus, 40));

        using var turn = await ResolveAsync(h, id, new AbilityIntent(ConanId, gift.Id));

        turn.RootElement.GetRawText().Should().NotContain("ab_secret_gift");
        h.Notifier.PublicTraffic.Should().NotContain("ab_secret_gift").And.NotContain("Test Gift");
    }

    [Fact]
    public async Task AGoldBonus_IsPaidOnASuccessfulRoom()
    {
        using var h = SocialHarness.Create(factory, roller: new FixedRoller(room: 1, success: Succeed));
        var id = await PartyTestData.CreatePartyAsync(h.Client);
        var gift = Give(h.Party(id), "Conan", "ab_t", 2, new AbilityEffect(EffectTypes.GoldBonus, 7));

        using var turn = await ResolveAsync(h, id, new AbilityIntent(ConanId, gift.Id));

        // Chest, turn 0: 5 gold plus the bonus.
        turn.RootElement.GetProperty("goldFound").GetInt32().Should().Be(5 + 7);
    }

    [Fact]
    public async Task AGrantTokenGift_AddsAWhisperShard_AndTellsOnlyItsOwner()
    {
        using var h = Harness(factory);
        var id = await PartyTestData.CreatePartyAsync(h.Client);
        var gift = Give(h.Party(id), "Conan", "ab_t", 2, new AbilityEffect(EffectTypes.GrantToken, 1));

        using var turn = await ResolveAsync(h, id, new AbilityIntent(ConanId, gift.Id));

        h.Profile(id, "Conan").AskTokens.Should().Be(PlayerProfile.StartingTokens + 1);
        h.PrivateTexts("Conan", PrivateKinds.Narrator).Should().Contain(t => t.Contains("whisper-shard"));
        h.PrivateTexts("Merlin", PrivateKinds.Narrator).Should().BeEmpty();
    }

    [Fact]
    public async Task AbilitiesOff_TheGiftIsIgnoredAndTheTurnPlaysAsBefore()
    {
        using var h = SocialHarness.Create(factory, new() { ["Social:Abilities"] = "false" }, new FixedRoller(room: 0, success: 80));
        var id = await PartyTestData.CreatePartyAsync(h.Client);
        var gift = Give(h.Party(id), "Conan", "ab_t", 2, new AbilityEffect(EffectTypes.RollBonus, 40));

        using var turn = await ResolveAsync(h, id, new AbilityIntent(ConanId, gift.Id));

        RollOf(turn, "Conan").GetProperty("chance").GetInt32().Should().Be(50);
    }

    [Fact]
    public async Task ATurnWithoutAnyGifts_IsExactlyTheOldTurn()
    {
        using var h = Harness(factory);
        var id = await PartyTestData.CreatePartyAsync(h.Client);

        using var turn = await ResolveAsync(h, id);

        turn.RootElement.GetProperty("rolls").EnumerateArray().Should().OnlyContain(r => r.GetProperty("chance").GetInt32() == 50);
        turn.RootElement.GetProperty("hits").GetArrayLength().Should().Be(3);
    }
}

public class TurnPlanTests
{
    [Fact]
    public void Chance_IsClampedToTheAbilityCeiling_AndNeverBelowTheFloor()
    {
        var plan = new TurnPlan([50, 50]);
        plan.Chances[0] += 200;
        plan.Chances[1] -= 200;

        plan.Chance(0).Should().Be(TurnPlan.MaxChance);
        plan.Chance(1).Should().Be(DungeonForge.Domain.Rules.ActionOdds.MinPercent);
    }

    [Theory]
    [InlineData(0, 6, 6)]
    [InlineData(50, 6, 3)]
    [InlineData(40, 7, 5)]
    [InlineData(100, 8, 2)]
    public void Reduce_RoundsUp_AndCapsTheShield(int shield, int damage, int expected)
    {
        var plan = new TurnPlan([50]);
        plan.ShieldPercent[0] = shield;

        plan.Reduce(0, damage).Should().Be(expected);
    }

    [Fact]
    public void AbilityUse_RespectsTheCooldownInTurns()
    {
        var profile = new PlayerProfile("Conan");
        var ability = new Ability("a", "T", "n", "x", "common", [], "f", "h", new AbilityStats(16, 3, [], 1));

        AbilityUse.IsReady(profile, ability, 0).Should().BeTrue();
        profile.LastUsed["a"] = 2;
        AbilityUse.IsReady(profile, ability, 4).Should().BeFalse();
        AbilityUse.IsReady(profile, ability, 5).Should().BeTrue();
    }
}

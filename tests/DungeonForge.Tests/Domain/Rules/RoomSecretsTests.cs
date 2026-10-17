using DungeonForge.Api.Domain;
using DungeonForge.Api.Domain.Rules;
using DungeonForge.Tests.Features.Parties.Chaos;
using FluentAssertions;

namespace DungeonForge.Tests.Domain.Rules;

public class RoomSecretsTests
{
    [Fact]
    public void Generate_GivesEachSpecialistTheirOwnKindOfSecret()
    {
        var secrets = RoomSecrets.Generate(ChaosTestSupport.Roster(), RoomType.Chest, 2, new ScriptedRoller(0));

        secrets.Select(s => (s.Target, s.Kind)).Should().BeEquivalentTo(new[]
        {
            ("Merlin", SecretKind.CurseWarning),
            ("Lyra", SecretKind.HiddenCoinPocket),
            ("Dru", SecretKind.TrapTruth),
        });
        secrets.Should().OnlyContain(s => s.Text.Length > 0);
        secrets.Should().NotContain(s => s.Target == "Conan");
    }

    [Fact]
    public void Generate_SkipsDefeatedMembers()
    {
        var members = ChaosTestSupport.Roster();
        var party = ChaosTestSupport.NewParty(null, members);
        var merlin = members[1];
        // 16 max HP mage: kill via targeted outcome.
        party.ApplyMemberOutcomes([new DamageResult("Merlin", 99)], 0);

        var secrets = RoomSecrets.Generate(party.Members, RoomType.Combat, 1, new ScriptedRoller(0));

        merlin.IsDefeated.Should().BeTrue();
        secrets.Should().NotContain(s => s.Target == "Merlin");
    }

    [Fact]
    public void Generate_IsDeterministicForTheSameRollerScript()
    {
        var a = RoomSecrets.Generate(ChaosTestSupport.Roster(), RoomType.Puzzle, 3, new ScriptedRoller(1, 2, 0, 1));
        var b = RoomSecrets.Generate(ChaosTestSupport.Roster(), RoomType.Puzzle, 3, new ScriptedRoller(1, 2, 0, 1));

        a.Should().Equal(b);
    }

    [Fact]
    public void HiddenCoinPocket_CarriesAConcreteGoldAmountFromTheRoller()
    {
        var secret = RoomSecrets.Generate(ChaosTestSupport.Roster(), RoomType.Chest, 1, new ScriptedRoller(0))
            .Single(s => s.Kind == SecretKind.HiddenCoinPocket);

        secret.Text.Should().MatchRegex(@"\d+ gold");
    }
}

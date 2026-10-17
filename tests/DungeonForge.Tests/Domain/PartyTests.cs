using DungeonForge.Api.Domain;
using FluentAssertions;

namespace DungeonForge.Tests.Domain;

public class PartyTests
{
    private static Party CreateParty(params PartyMember[] members) =>
        Party.Create(Guid.NewGuid(), "Heroes", members, DateTimeOffset.UtcNow, "ABCD-EFGH");

    [Fact]
    public void Create_ValidRoster_ExposesMembersInOrder()
    {
        var id = Guid.NewGuid();
        var created = DateTimeOffset.UtcNow;
        var mage = new PartyMember("Merlin", CharacterClass.Mage);
        var warrior = new PartyMember("Conan", CharacterClass.Warrior);

        var party = Party.Create(id, "Heroes", [mage, warrior], created, "ABCD-EFGH");

        party.Id.Should().Be(id);
        party.Name.Should().Be("Heroes");
        party.CreatedAt.Should().Be(created);
        party.InviteCode.Should().Be("ABCD-EFGH");
        party.Gold.Should().Be(0);
        party.Turn.Should().Be(0);
        party.Members.Should().Equal(mage, warrior);
        party.IsPartyDefeated.Should().BeFalse();
    }

    [Fact]
    public void Create_EmptyRoster_Throws()
    {
        var act = () => Party.Create(Guid.NewGuid(), "Heroes", [], DateTimeOffset.UtcNow, "");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TakeDamage_MixedRoster_HitsWarriorFirst()
    {
        var party = CreateParty(
            new PartyMember("Merlin", CharacterClass.Mage),
            new PartyMember("Mercy", CharacterClass.Priest),
            new PartyMember("Conan", CharacterClass.Warrior));

        var result = party.TakeDamage(5);

        result.TargetName.Should().Be("Conan");
        result.Damage.Should().Be(5);
        party.Members.Single(m => m.Name == "Conan").Hp.Should().Be(25);
        party.Members.Where(m => m.Name != "Conan").Should().OnlyContain(m => m.Hp == m.MaxHp);
    }

    [Fact]
    public void TakeDamage_PaladinAndMage_HitsPaladinFirst()
    {
        var party = CreateParty(
            new PartyMember("Merlin", CharacterClass.Mage),
            new PartyMember("Lance", CharacterClass.Paladin));

        var result = party.TakeDamage(3, ElementType.Fire);

        result.TargetName.Should().Be("Lance");
        result.Damage.Should().Be(3);
    }

    [Fact]
    public void TakeDamage_WarriorAndPaladin_TiesBreakByRosterOrder()
    {
        var party = CreateParty(
            new PartyMember("Lance", CharacterClass.Paladin),
            new PartyMember("Conan", CharacterClass.Warrior));

        party.TakeDamage(1).TargetName.Should().Be("Lance");
    }

    [Fact]
    public void TakeDamage_TankStillAlive_KeepsHittingTank()
    {
        var party = CreateParty(
            new PartyMember("Merlin", CharacterClass.Mage),
            new PartyMember("Conan", CharacterClass.Warrior));

        party.TakeDamage(10);
        var second = party.TakeDamage(10);

        second.TargetName.Should().Be("Conan");
        party.Members[1].Hp.Should().Be(10);
        party.Members[0].Hp.Should().Be(16);
    }

    [Fact]
    public void TakeDamage_TanksDefeated_FallsThroughToNextMember()
    {
        var party = CreateParty(
            new PartyMember("Merlin", CharacterClass.Mage),
            new PartyMember("Conan", CharacterClass.Warrior));
        party.TakeDamage(30);
        party.Members[1].IsDefeated.Should().BeTrue();

        var result = party.TakeDamage(4);

        result.TargetName.Should().Be("Merlin");
        result.Damage.Should().Be(4);
        party.Members[0].Hp.Should().Be(12);
        party.IsPartyDefeated.Should().BeFalse();
    }

    [Fact]
    public void TakeDamage_OnlySquishyMembers_HitsFirstLivingInRosterOrder()
    {
        var party = CreateParty(
            new PartyMember("Merlin", CharacterClass.Mage),
            new PartyMember("Mercy", CharacterClass.Priest));
        party.TakeDamage(16);

        var result = party.TakeDamage(2);

        result.TargetName.Should().Be("Mercy");
    }

    [Fact]
    public void TakeDamage_NegativeAmount_Throws()
    {
        var party = CreateParty(new PartyMember("Conan", CharacterClass.Warrior));

        var act = () => party.TakeDamage(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void IsPartyDefeated_SomeSurvivors_IsFalse()
    {
        var party = CreateParty(
            new PartyMember("Merlin", CharacterClass.Mage),
            new PartyMember("Conan", CharacterClass.Warrior));

        party.Defeat("Conan");

        party.IsPartyDefeated.Should().BeFalse();
    }

    [Fact]
    public void IsPartyDefeated_AllMembersAtZero_IsTrue()
    {
        var party = CreateParty(
            new PartyMember("Merlin", CharacterClass.Mage),
            new PartyMember("Conan", CharacterClass.Warrior));

        party.Defeat("Conan");
        party.TakeDamage(16);

        party.IsPartyDefeated.Should().BeTrue();
        party.Members.Should().OnlyContain(m => m.Hp == 0);
    }

    [Fact]
    public void Heal_NamedWoundedMember_RestoresHp()
    {
        var party = CreateParty(
            new PartyMember("Merlin", CharacterClass.Mage),
            new PartyMember("Conan", CharacterClass.Warrior));
        party.Hurt("Conan", 10);

        var healed = party.Heal("Conan", 6);

        healed.Should().Be(6);
        party.Members[1].Hp.Should().Be(26);
    }

    [Fact]
    public void Heal_UnknownMember_Throws()
    {
        var party = CreateParty(new PartyMember("Conan", CharacterClass.Warrior));

        var act = () => party.Heal("Nobody", 3);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_BlankName_Throws(string? name)
    {
        var act = () => Party.Create(Guid.NewGuid(), name!, [new PartyMember("Conan", CharacterClass.Warrior)], DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Members_CannotBeCastToMutableCollection()
    {
        var party = CreateParty(new PartyMember("Conan", CharacterClass.Warrior));

        party.Members.Should().NotBeAssignableTo<PartyMember[]>();
        var add = () => ((IList<PartyMember>)party.Members).Add(new PartyMember("X", CharacterClass.Mage));
        add.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void TakeDamage_ElementIsNotYetModeled_FireAndPhysicalHitSameTargetForSameDamage()
    {
        // Pins current behavior: resistances are not modeled yet (see TODO in Party.TakeDamage).
        var physical = CreateParty(new PartyMember("Merlin", CharacterClass.Mage), new PartyMember("Conan", CharacterClass.Warrior));
        var fire = CreateParty(new PartyMember("Merlin", CharacterClass.Mage), new PartyMember("Conan", CharacterClass.Warrior));

        var p = physical.TakeDamage(7, ElementType.Physical);
        var f = fire.TakeDamage(7, ElementType.Fire);

        f.Should().Be(p);
        f.Should().Be(new DamageResult("Conan", 7));
    }

    [Fact]
    public void Snapshot_CapturesCurrentStateAsImmutableCopy()
    {
        var party = CreateParty(new PartyMember("Conan", CharacterClass.Warrior));
        party.Hurt("Conan", 4);
        party.AdvanceTurn(9);

        var snap = party.Snapshot();
        party.Hurt("Conan", 5);
        party.AdvanceTurn(1);

        snap.Id.Should().Be(party.Id);
        snap.Name.Should().Be("Heroes");
        snap.InviteCode.Should().Be("ABCD-EFGH");
        snap.Turn.Should().Be(1);
        snap.Gold.Should().Be(9);
        snap.IsPartyDefeated.Should().BeFalse();
        snap.Members.Should().ContainSingle().Which.Should().Be(new MemberSnapshot("Conan", CharacterClass.Warrior, 26, 30, false));
    }

    [Fact]
    public async Task Snapshot_UnderConcurrentMutation_NeverObservesTornState()
    {
        var party = CreateParty(new PartyMember("Conan", CharacterClass.Warrior));
        using var cts = new CancellationTokenSource();
        var writer = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                lock (party.SyncRoot)
                {
                    party.Hurt("Conan", 1);
                    party.AdvanceTurn(1); // invariant while alive: hp + turn == 30
                }
            }
        });

        for (var i = 0; i < 20000; i++)
        {
            var s = party.Snapshot();
            if (s.Members[0].Hp > 0)
            {
                (s.Members[0].Hp + s.Turn).Should().Be(30);
            }
        }

        cts.Cancel();
        await writer;
    }
}

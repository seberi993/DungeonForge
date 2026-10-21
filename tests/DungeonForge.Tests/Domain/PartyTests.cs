using DungeonForge.Domain;
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
        party.Defeat("Merlin");

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

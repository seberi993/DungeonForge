using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using FluentAssertions;

namespace DungeonForge.Tests.Domain;

public class PartyFloorTests
{
    private static Party CreateParty() =>
        Party.Create(Guid.NewGuid(), "Heroes",
            [new PartyMember("Conan", CharacterClass.Warrior), new PartyMember("Merlin", CharacterClass.Mage)],
            DateTimeOffset.UtcNow, "ABCD-EFGH");

    private static void ClearOrdinaryRooms(Party party, int count)
    {
        for (var i = 0; i < count; i++) party.AdvanceProgress(RoomType.Combat, 0);
    }

    [Fact]
    public void NewParty_StartsOnFloorOne_WithNoBoss()
    {
        var party = CreateParty();

        party.Floor.Should().Be(1);
        party.RoomsCleared.Should().Be(0);
        party.BossMaxHp.Should().Be(0);
    }

    [Fact]
    public void AdvanceProgress_BeforeTheLastRoom_DoesNotWakeTheBoss()
    {
        var party = CreateParty();

        ClearOrdinaryRooms(party, Party.RoomsPerFloor - 1);

        party.BossMaxHp.Should().Be(0);
        party.RoomsCleared.Should().Be(Party.RoomsPerFloor - 1);
    }

    [Fact]
    public void AdvanceProgress_LastOrdinaryRoom_WakesTheBossAtFullHp()
    {
        var party = CreateParty();
        ClearOrdinaryRooms(party, Party.RoomsPerFloor - 1);

        var progress = party.AdvanceProgress(RoomType.Puzzle, 0);

        progress.Should().Be(new FloorProgress(BossNext: true, FloorCleared: false));
        party.BossHp.Should().Be(Party.BossHpFor(1));
        party.BossMaxHp.Should().Be(Party.BossHpFor(1));
    }

    [Fact]
    public void AdvanceProgress_BossHit_WearsItDown_AndKeepsBossNext()
    {
        var party = CreateParty();
        ClearOrdinaryRooms(party, Party.RoomsPerFloor);

        var progress = party.AdvanceProgress(RoomType.Boss, 10);

        progress.Should().Be(new FloorProgress(BossNext: true, FloorCleared: false));
        party.BossHp.Should().Be(Party.BossHpFor(1) - 10);
        party.Floor.Should().Be(1);
    }

    [Fact]
    public void AdvanceProgress_OverkillingTheBoss_DescendsAndResetsTheFloor()
    {
        var party = CreateParty();
        ClearOrdinaryRooms(party, Party.RoomsPerFloor);

        var progress = party.AdvanceProgress(RoomType.Boss, 999);

        progress.Should().Be(new FloorProgress(BossNext: false, FloorCleared: true));
        party.Floor.Should().Be(2);
        party.RoomsCleared.Should().Be(0);
        party.BossMaxHp.Should().Be(0);
        party.BossHp.Should().Be(0);
    }

    [Fact]
    public void AdvanceProgress_FloorCleared_HealsAWoundedHero()
    {
        var party = CreateParty();
        party.TakeDamage(20); // the Warrior draws the hit
        var conan = party.Members[0];
        var before = conan.Hp;
        ClearOrdinaryRooms(party, Party.RoomsPerFloor);

        party.AdvanceProgress(RoomType.Boss, 999);

        conan.Hp.Should().Be(Math.Min(conan.MaxHp, before + (int)Math.Ceiling(conan.MaxHp * Party.FloorHealRatio)));
    }

    [Fact]
    public void AdvanceProgress_FloorCleared_DoesNotReviveAFallenHero()
    {
        var party = CreateParty();
        party.TakeDamage(1000); // Conan falls
        var conan = party.Members[0];
        conan.IsDefeated.Should().BeTrue();
        ClearOrdinaryRooms(party, Party.RoomsPerFloor);

        party.AdvanceProgress(RoomType.Boss, 999);

        conan.Hp.Should().Be(0);
    }

    [Fact]
    public void BossHpFor_GrowsWithEachFloor() =>
        Party.BossHpFor(3).Should().Be(Party.BossHpFor(1) + 2 * Party.BossHpPerFloor);

    [Fact]
    public void Snapshot_CarriesFloorAndBossState()
    {
        var party = CreateParty();
        ClearOrdinaryRooms(party, Party.RoomsPerFloor);

        var snapshot = party.Snapshot();

        snapshot.Floor.Should().Be(1);
        snapshot.RoomsCleared.Should().Be(Party.RoomsPerFloor);
        snapshot.BossHp.Should().Be(Party.BossHpFor(1));
        snapshot.BossMaxHp.Should().Be(Party.BossHpFor(1));
    }
}

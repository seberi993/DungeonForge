using DungeonForge.Web.Models;
using DungeonForge.Web.Services;
using FluentAssertions;

namespace DungeonForge.Web.Tests;

public class GameSessionManaTests
{
    private static readonly Guid Id = Guid.NewGuid();

    private static GameSession Started(CharacterClass cls = CharacterClass.Mage)
    {
        var sut = new GameSession();
        sut.Start(new PartyResponse(Id, "Heroes", [new MemberDto("A", cls, 20, 20, false)], DateTimeOffset.UtcNow));
        return sut;
    }

    private static SpellDto Spell(string name, int cost) => new(name, CharacterClass.Mage, ElementType.Fire, cost, "d");

    private static ExploreResponse Resolved(int turn) => new(Id, turn, RoomType.Combat, null, 0, 0, "x", 0, false);

    [Theory]
    [InlineData(CharacterClass.Mage)]
    [InlineData(CharacterClass.Warrior)]
    [InlineData(CharacterClass.Bard)]
    public void NewParty_ManaStartsFull(CharacterClass cls)
    {
        var sut = Started(cls);

        sut.MaxMana.Should().Be(GameSession.MaxManaFor(cls)).And.BeGreaterThan(0);
        sut.Mana.Should().Be(sut.MaxMana);
    }

    [Fact]
    public void ReserveMana_ActionNamesSpell_SpendsItsCost_CaseInsensitive()
    {
        var sut = Started();
        sut.SetSpells([Spell("Fireball", 12)]);

        var spent = sut.ReserveMana("I hurl a FIREBALL at the ogre");

        spent.Should().Be(12);
        sut.Mana.Should().Be(sut.MaxMana - 12);
    }

    [Fact]
    public void ReserveMana_NoSpellNamed_SpendsNothing()
    {
        var sut = Started();
        sut.SetSpells([Spell("Fireball", 12)]);

        sut.ReserveMana("I swing my staff").Should().Be(0);
        sut.Mana.Should().Be(sut.MaxMana);
    }

    [Fact]
    public void ReserveMana_NeverGoesBelowZero()
    {
        var sut = Started();
        sut.SetSpells([Spell("Apocalypse", 9999)]);

        sut.ReserveMana("cast apocalypse");

        sut.Mana.Should().Be(0);
    }

    [Fact]
    public void ReserveMana_Twice_ReplacesEarlierReservation()
    {
        var sut = Started();
        sut.SetSpells([Spell("Fireball", 12), Spell("Frost", 5)]);

        sut.ReserveMana("fireball");
        sut.ReserveMana("frost");

        sut.Mana.Should().Be(sut.MaxMana - 5);
    }

    [Fact]
    public void ReleaseMana_RefundsReservation()
    {
        var sut = Started();
        sut.SetSpells([Spell("Fireball", 12)]);
        sut.ReserveMana("fireball");

        sut.ReleaseMana();

        sut.Mana.Should().Be(sut.MaxMana);
    }

    [Fact]
    public void Apply_ResolvedTurn_KeepsSpendAndRegenerates()
    {
        var sut = Started();
        sut.SetSpells([Spell("Fireball", 30)]);
        sut.ReserveMana("fireball");

        sut.Apply(Resolved(1));

        sut.Mana.Should().Be(sut.MaxMana - 30 + GameSession.ManaRegenPerTurn);
    }

    [Fact]
    public void Apply_Regen_ClampsAtMax()
    {
        var sut = Started();

        sut.Apply(Resolved(1));

        sut.Mana.Should().Be(sut.MaxMana);
    }

    [Fact]
    public void ReleaseAfterResolve_DoesNotRefund()
    {
        var sut = Started();
        sut.SetSpells([Spell("Fireball", 30)]);
        sut.ReserveMana("fireball");
        sut.Apply(Resolved(1));
        var after = sut.Mana;

        sut.ReleaseMana();

        sut.Mana.Should().Be(after);
    }

    [Fact]
    public void Clear_ResetsMana()
    {
        var sut = Started();
        sut.SetSpells([Spell("Fireball", 30)]);
        sut.ReserveMana("fireball");

        sut.Clear();
        sut.Start(new PartyResponse(Id, "Heroes", [new MemberDto("A", CharacterClass.Mage, 20, 20, false)], DateTimeOffset.UtcNow));

        sut.Mana.Should().Be(sut.MaxMana);
    }
}

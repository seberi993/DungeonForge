using DungeonForge.Domain;
using DungeonForge.Application.Features.Parties.ExploreDungeon;
using DungeonForge.Tests.Features.Parties.Chaos;
using FluentAssertions;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Parties;

// The three shapes an explore answer takes, built by name so a new field cannot silently shift the others.
public class ExploreResponsesTests
{
    private static Party NewParty() => PartyTestBuilders.NewParty();

    [Fact]
    public void Prompt_CarriesTheStoredScenarioAndLockStatus_AndNoDeltas()
    {
        var party = NewParty();
        var pending = new PendingEncounter(RoomType.Puzzle, "A riddle. What do you do?");

        var response = ExploreResponses.Prompt(party, pending);

        response.PartyId.Should().Be(party.Id);
        response.Turn.Should().Be(0);
        response.RoomType.Should().Be(RoomType.Puzzle);
        response.Scenario.Should().Be("A riddle. What do you do?");
        response.FlavorEvent.Should().Be("A riddle. What do you do?");
        response.Gold.Should().Be(0);
        response.IsPartyDefeated.Should().BeFalse();
        response.LockStatus.Should().HaveCount(4);
        response.ScenarioPending.Should().BeFalse();
        (response.TargetName, response.Damage, response.GoldFound, response.Discussion).Should().Be((null, 0, 0, null));
    }

    [Fact]
    public void Awaiting_SaysTheScenarioIsStillBeingWritten()
    {
        var response = ExploreResponses.Awaiting(NewParty(), RoomType.Chest);

        response.ScenarioPending.Should().BeTrue();
        response.RoomType.Should().Be(RoomType.Chest);
        response.FlavorEvent.Should().BeEmpty();
        response.Scenario.Should().BeNull();
        response.LockStatus.Should().BeNull();
    }

    [Fact]
    public void Resolved_MirrorsTheFirstHit_AndReportsTheRoundFromTheParty()
    {
        var party = NewParty();
        var pending = new PendingEncounter(RoomType.Combat, "A troll.");
        PlayerAction[] discussion = [new(Guid.NewGuid(), "Conan", "hit it")];
        DamageResult[] hits = [new("Merlin", 7), new("Lyra", 3)];
        RollDto[] rolls = [new("Conan", 40, 50, false, false)];
        ActionBeat[] beats = [new("Conan", CharacterClass.Warrior, ActionVerb.Strike, "hit it")];

        var response = ExploreResponses.Resolved(
            party, pending, discussion, hits, rolls, beats, gold: 5, success: true, floorCleared: true, combatLog: ["a log line"]);

        response.RoomType.Should().Be(RoomType.Combat);
        response.TargetName.Should().Be("Merlin");
        response.Damage.Should().Be(7);
        response.Hits.Should().Equal(new HitDto("Merlin", 7), new HitDto("Lyra", 3));
        response.GoldFound.Should().Be(5);
        response.Success.Should().BeTrue();
        response.FloorCleared.Should().BeTrue();
        response.Discussion.Should().BeSameAs(discussion);
        response.Rolls.Should().BeSameAs(rolls);
        response.Beats.Should().BeSameAs(beats);
        response.CombatLog.Should().Equal("a log line");
        response.FlavorEvent.Should().Be("A troll.");
        response.Scenario.Should().BeNull();
        response.Floor.Should().Be(1);
        response.NarrationPending.Should().BeFalse("the handler sets that once the turn is announced");
    }

    [Fact]
    public void Resolved_WithNoHits_HasNoTarget()
    {
        var response = ExploreResponses.Resolved(
            NewParty(), new PendingEncounter(RoomType.Chest, "A chest."), [], [], [], [], gold: 0, success: true, floorCleared: false, combatLog: null);

        response.TargetName.Should().BeNull();
        response.Damage.Should().Be(0);
        response.Hits.Should().BeEmpty();
        response.CombatLog.Should().BeNull();
    }
}

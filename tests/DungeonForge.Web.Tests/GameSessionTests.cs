using DungeonForge.Web.Models;
using DungeonForge.Web.Services;
using FluentAssertions;

namespace DungeonForge.Web.Tests;

public class GameSessionTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private static MemberDto Member(string name, CharacterClass cls, int hp, int maxHp) => new(name, cls, hp, maxHp, hp == 0);
    private static PartyResponse Party() => new(Id, "Heroes", [Member("A", CharacterClass.Warrior, 30, 30), Member("B", CharacterClass.Mage, 16, 16)], DateTimeOffset.UtcNow);
    private static ExploreResponse Turn(int turn, RoomType room, string? target, int damage, int gold, int totalGold, bool defeated = false, string flavor = "x") =>
        new(Id, turn, room, target, damage, gold, flavor, totalGold, defeated);

    private static ExploreResponse Pending(int turn) =>
        new(Id, turn, RoomType.Combat, "A", 4, 0, "Clash", 0, false, NarrationPending: true);

    [Fact]
    public void Apply_NarrationPending_AppliesMechanicsAndMarksPending()
    {
        var sut = new GameSession();
        sut.Start(Party());

        sut.Apply(Pending(1)).Should().BeTrue();

        sut.Members[0].Hp.Should().Be(26);
        sut.Turn.Should().Be(1);
        sut.NarrationPending.Should().BeTrue();
        sut.LastOutcome.Should().BeNull();
    }

    [Fact]
    public void ApplyNarration_MatchingTurn_SetsOutcomeAndLogEntry()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Pending(1));

        sut.ApplyNarration(Id, 1, "You win.").Should().BeTrue();

        sut.NarrationPending.Should().BeFalse();
        sut.LastOutcome.Should().Be("You win.");
        sut.Log[0].Outcome.Should().Be("You win.");
    }

    [Fact]
    public void ApplyNarration_EmptyOutcome_FirstTurn_SetsScenarioWithoutLoggingBlankOutcome()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyPrompt(new ExploreResponse(Id, 0, RoomType.Combat, null, 0, 0, "", 0, false, ScenarioPending: true));

        sut.ApplyNarration(Id, 0, "", "A door looms.").Should().BeTrue();

        sut.Scenario.Should().Be("A door looms.");
        sut.ScenarioPending.Should().BeFalse();
        sut.LastOutcome.Should().BeNull();
        sut.Log.Should().BeEmpty();
    }

    [Fact]
    public void ApplyNarration_EmptyOutcome_DoesNotOverwriteExistingOutcome()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Pending(1));
        sut.ApplyNarration(Id, 1, "You win.", "Next room.");

        sut.ApplyNarration(Id, 1, "", "Other.").Should().BeFalse();

        sut.LastOutcome.Should().Be("You win.");
        sut.Log[0].Outcome.Should().Be("You win.");
    }

    [Fact]
    public void ApplyNarration_OtherTurnOrParty_IsIgnored()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Pending(2));

        sut.ApplyNarration(Id, 1, "old").Should().BeFalse();
        sut.ApplyNarration(Guid.NewGuid(), 2, "x").Should().BeFalse();

        sut.NarrationPending.Should().BeTrue();
        sut.LastOutcome.Should().BeNull();
    }

    [Fact]
    public void ApplyNarration_ArrivingBeforeResponse_IsAppliedWhenTurnLands()
    {
        var sut = new GameSession();
        sut.Start(Party());

        sut.ApplyNarration(Id, 1, "early");
        sut.Apply(Pending(1));

        sut.NarrationPending.Should().BeFalse();
        sut.LastOutcome.Should().Be("early");
        sut.Log[0].Outcome.Should().Be("early");
    }

    [Fact]
    public void ExpireNarration_UsesPlaceholderOnlyIfStillPending_AndLateNarrationStillWins()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Pending(1));

        sut.ExpireNarration(1).Should().BeTrue();
        sut.NarrationPending.Should().BeFalse();
        sut.LastOutcome.Should().NotBeNullOrWhiteSpace();

        sut.ApplyNarration(Id, 1, "late").Should().BeTrue();
        sut.LastOutcome.Should().Be("late");
        sut.ExpireNarration(1).Should().BeFalse();
    }

    [Fact]
    public void ApplyDiscussion_ReplacesList_ClearedWhenTurnResolves()
    {
        var sut = new GameSession();
        sut.Start(Party());
        var p = new PlayerAction(Guid.NewGuid(), "A", "hit it");

        sut.ApplyDiscussion(Id, [p]).Should().BeTrue();
        sut.Discussion.Should().Equal(p);
        sut.ApplyDiscussion(Guid.NewGuid(), []).Should().BeFalse();

        sut.Apply(Turn(1, RoomType.Chest, null, 0, 1, 1));
        sut.Discussion.Should().BeEmpty();
    }

    [Fact]
    public void ApplyPrompt_WithDiscussion_StoresIt()
    {
        var sut = new GameSession();
        sut.Start(Party());
        var p = new PlayerAction(Guid.NewGuid(), "A", "hit it");

        sut.ApplyPrompt(new ExploreResponse(Id, 0, RoomType.Puzzle, null, 0, 0, "r", 0, false, Discussion: [p], Scenario: "r"));

        sut.Discussion.Should().Equal(p);
    }

    [Fact]
    public void PlayerId_IsStableAcrossClear()
    {
        var sut = new GameSession();
        var id = sut.PlayerId;
        sut.Start(Party());
        sut.Clear();
        sut.PlayerId.Should().Be(id).And.NotBe(Guid.Empty);
    }

    [Fact]
    public void ApplyPartyState_NewerTurn_ReplacesStateAndRaisesChanged()
    {
        var sut = new GameSession();
        sut.Start(Party());
        var changed = 0;
        sut.Changed += () => changed++;

        var applied = sut.ApplyPartyState(new PartyStateDto(Id, "Heroes", [Member("A", CharacterClass.Warrior, 7, 30)], DateTimeOffset.UtcNow, 12, 3, false));

        applied.Should().BeTrue();
        sut.Turn.Should().Be(3);
        sut.Gold.Should().Be(12);
        sut.Members[0].Hp.Should().Be(7);
        changed.Should().Be(1);
    }

    [Fact]
    public void ApplyPartyState_StaleOrEqualTurnOrOtherParty_IsIgnored()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Turn(2, RoomType.Chest, null, 0, 5, 5));
        var stale = new PartyStateDto(Id, "Heroes", [Member("A", CharacterClass.Warrior, 1, 30)], DateTimeOffset.UtcNow, 0, 1, false);

        sut.ApplyPartyState(stale).Should().BeFalse();
        sut.ApplyPartyState(stale with { Turn = 2 }).Should().BeFalse();
        sut.ApplyPartyState(stale with { Turn = 9, PartyId = Guid.NewGuid() }).Should().BeFalse();

        sut.Turn.Should().Be(2);
        sut.Gold.Should().Be(5);
        sut.Members[0].Hp.Should().Be(30);
    }

    [Fact]
    public void New_HasNoParty()
    {
        var sut = new GameSession();
        sut.HasParty.Should().BeFalse();
    }

    [Fact]
    public void Start_SetsPartyAndInitialStats()
    {
        var sut = new GameSession();
        sut.Start(Party());

        sut.HasParty.Should().BeTrue();
        sut.PartyId.Should().Be(Id);
        sut.Name.Should().Be("Heroes");
        sut.Members.Select(m => m.Hp).Should().Equal(30, 16);
        sut.TotalHp.Should().Be(46);
        sut.TotalMaxHp.Should().Be(46);
        sut.Gold.Should().Be(0);
        sut.Turn.Should().Be(0);
        sut.Log.Should().BeEmpty();
        sut.IsDefeated.Should().BeFalse();
    }

    [Fact]
    public void Apply_UpdatesStatsAndAppendsLog()
    {
        var sut = new GameSession();
        sut.Start(Party());

        sut.Apply(Turn(1, RoomType.Combat, "A", 4, 0, 0, flavor: "Fight"));
        sut.Apply(Turn(2, RoomType.Chest, null, 0, 9, 9, flavor: "Loot"));

        sut.Turn.Should().Be(2);
        sut.Members[0].Hp.Should().Be(26);
        sut.Members[1].Hp.Should().Be(16);
        sut.Gold.Should().Be(9);
        sut.Log.Should().HaveCount(2);
        sut.Log[0].FlavorEvent.Should().Be("Fight");
        sut.Log[1].RoomType.Should().Be(RoomType.Chest);
    }

    [Fact]
    public void Apply_PartyDefeated_MarksDefeated()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Turn(1, RoomType.Combat, "A", 20, 0, 0, defeated: true));
        sut.IsDefeated.Should().BeTrue();
    }

    [Fact]
    public void MarkDefeated_SetsDefeated()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.MarkDefeated();
        sut.IsDefeated.Should().BeTrue();
    }

    [Fact]
    public void ApplyRemote_NewTurn_AppliesAndRaisesChanged()
    {
        var sut = new GameSession();
        sut.Start(Party());
        var changed = 0;
        sut.Changed += () => changed++;

        var applied = sut.ApplyRemote(Turn(1, RoomType.Chest, null, 0, 5, 5));

        applied.Should().BeTrue();
        changed.Should().Be(1);
        sut.Turn.Should().Be(1);
        sut.Gold.Should().Be(5);
        sut.Log.Should().HaveCount(1);
    }

    [Fact]
    public void ApplyRemote_StaleOrDuplicateTurn_IsIgnored()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Turn(2, RoomType.Chest, null, 0, 5, 5));
        var changed = 0;
        sut.Changed += () => changed++;

        sut.ApplyRemote(Turn(2, RoomType.Chest, null, 0, 5, 5)).Should().BeFalse();
        sut.ApplyRemote(Turn(1, RoomType.Chest, null, 0, 5, 5)).Should().BeFalse();

        sut.Log.Should().HaveCount(1);
        changed.Should().Be(0);
    }

    [Fact]
    public void ApplyRemote_OtherParty_IsIgnored()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyRemote(Turn(1, RoomType.Chest, null, 0, 5, 5) with { PartyId = Guid.NewGuid() }).Should().BeFalse();
        sut.Log.Should().BeEmpty();
    }

    [Fact]
    public void ApplyRemote_PartyDefeated_MarksDefeated()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyRemote(Turn(1, RoomType.Combat, "A", 20, 0, 0, defeated: true));
        sut.IsDefeated.Should().BeTrue();
    }

    [Fact]
    public void Join_LoadsStateFromDto()
    {
        var sut = new GameSession();
        sut.Join(new PartyStateDto(Id, "Heroes", [Member("A", CharacterClass.Warrior, 7, 30)], DateTimeOffset.UtcNow, 30, 4, false));

        sut.HasParty.Should().BeTrue();
        sut.Name.Should().Be("Heroes");
        sut.TotalHp.Should().Be(7);
        sut.Gold.Should().Be(30);
        sut.Turn.Should().Be(4);
        sut.IsDefeated.Should().BeFalse();
        sut.Join(new PartyStateDto(Id, "Heroes", [Member("A", CharacterClass.Warrior, 0, 30)], DateTimeOffset.UtcNow, 30, 5, true));
        sut.IsDefeated.Should().BeTrue();
    }

    private static ExploreResponse Prompt(int turn, string scenario = "A troll\nblocks the bridge.") =>
        new(Id, turn, RoomType.Combat, null, 0, 0, scenario, 0, false, Scenario: scenario);

    [Fact]
    public void ApplyPrompt_SameTurn_BypassesTurnGuard_AndSetsScenario()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Turn(2, RoomType.Chest, null, 0, 5, 5));
        sut.ApplyNarration(Id, 2, "done");
        var changed = 0;
        sut.Changed += () => changed++;

        sut.ApplyPrompt(Prompt(2)).Should().BeTrue();

        sut.HasScenario.Should().BeTrue();
        sut.Scenario.Should().Be("A troll\nblocks the bridge.");
        sut.ScenarioPending.Should().BeFalse();
        sut.Turn.Should().Be(2);
        sut.Log.Should().HaveCount(1);
        changed.Should().Be(1);
    }

    [Fact]
    public void ApplyPrompt_FromFreshSession_Works()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyPrompt(Prompt(0)).Should().BeTrue();
        sut.HasScenario.Should().BeTrue();
    }

    [Fact]
    public void ApplyPrompt_ScenarioPending_SetsPendingWithoutText()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Pending(1));

        sut.ApplyPrompt(new ExploreResponse(Id, 1, RoomType.Combat, null, 0, 0, "x", 0, false, ScenarioPending: true)).Should().BeTrue();

        sut.ScenarioPending.Should().BeTrue();
        sut.HasScenario.Should().BeFalse();
    }

    [Fact]
    public void ApplyPrompt_StaleTurnOtherPartyOrNoScenario_IsIgnored()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Turn(2, RoomType.Chest, null, 0, 5, 5));
        sut.ApplyNarration(Id, 2, "done");

        sut.ApplyPrompt(Prompt(1)).Should().BeFalse();
        sut.ApplyPrompt(Prompt(2) with { PartyId = Guid.NewGuid() }).Should().BeFalse();
        sut.ApplyPrompt(Prompt(2) with { Scenario = null }).Should().BeFalse();
        sut.HasScenario.Should().BeFalse();
        sut.ScenarioPending.Should().BeFalse();
    }

    [Fact]
    public void Apply_ClearsScenario_AwaitsNext_AndRecordsLastOutcome()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyPrompt(Prompt(0));

        sut.Apply(Turn(1, RoomType.Combat, "A", 3, 0, 0) with { LastOutcome = "You won." }).Should().BeTrue();

        sut.HasScenario.Should().BeFalse();
        sut.ScenarioPending.Should().BeTrue();
        sut.LastOutcome.Should().Be("You won.");
        sut.Log[0].Outcome.Should().Be("You won.");
    }

    [Fact]
    public void Apply_PartyDefeated_DoesNotAwaitScenario()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Turn(1, RoomType.Combat, "A", 99, 0, 0, defeated: true));
        sut.ScenarioPending.Should().BeFalse();
    }

    [Fact]
    public void ApplyNarration_WithNextScenario_BecomesCurrentScenario()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Pending(1));

        sut.ApplyNarration(Id, 1, "You win.", "A new hall\nopens.");

        sut.Scenario.Should().Be("A new hall\nopens.");
        sut.ScenarioPending.Should().BeFalse();
        sut.NarrationPending.Should().BeFalse();
    }

    [Fact]
    public void ApplyNarration_WithoutNextScenario_ClearsScenarioPending()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Pending(1));

        sut.ApplyNarration(Id, 1, "The end.");

        sut.HasScenario.Should().BeFalse();
        sut.ScenarioPending.Should().BeFalse();
    }

    [Fact]
    public void ApplyNarration_OldTurn_DoesNotOverrideCurrentScenario()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Pending(1));
        sut.Apply(Pending(2));

        sut.ApplyNarration(Id, 1, "old", "stale scenario");

        sut.Scenario.Should().BeNull();
        sut.ScenarioPending.Should().BeTrue();
    }

    [Fact]
    public void ApplyNarration_ArrivingBeforeResponse_KeepsNextScenario()
    {
        var sut = new GameSession();
        sut.Start(Party());

        sut.ApplyNarration(Id, 1, "early", "Next room.");
        sut.Apply(Pending(1));

        sut.Scenario.Should().Be("Next room.");
        sut.ScenarioPending.Should().BeFalse();
    }

    [Fact]
    public void ExpireNarration_AlsoStopsWaitingForScenario()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Pending(1));

        sut.ExpireNarration(1).Should().BeTrue();

        sut.ScenarioPending.Should().BeFalse();
    }

    [Fact]
    public void ExpireNarration_ScenarioPendingOnly_Expires()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Turn(1, RoomType.Chest, null, 0, 1, 1));
        sut.ScenarioPending.Should().BeTrue();

        sut.ExpireNarration(1).Should().BeTrue();

        sut.ScenarioPending.Should().BeFalse();
        sut.LastOutcome.Should().BeNull();
    }

    [Fact]
    public void ApplyNarration_RemembersWhereTheOutcomeCameFrom_UntilTheNextTurn()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Turn(1, RoomType.Chest, null, 0, 5, 5));

        sut.ApplyNarration(Id, 1, "Canned.", "Next. What do you do?", RoomType.Combat, null, "fallback");

        sut.NarrationSource.Should().Be("fallback");
        sut.Apply(Turn(2, RoomType.Chest, null, 0, 5, 10));
        sut.NarrationSource.Should().BeNull();
    }

    [Fact]
    public void ApplyPartyState_SameTurnWithNewMember_AddsThemWithoutTouchingTheScene()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyPrompt(Prompt(0));
        var changed = 0;
        sut.Changed += () => changed++;

        var applied = sut.ApplyPartyState(new PartyStateDto(Id, "Heroes",
            [Member("A", CharacterClass.Warrior, 30, 30), Member("B", CharacterClass.Mage, 16, 16), Member("C", CharacterClass.Bard, 20, 20)],
            DateTimeOffset.UtcNow, 0, 0, false));

        applied.Should().BeTrue();
        sut.Members.Select(m => m.Name).Should().Equal("A", "B", "C");
        sut.HasScenario.Should().BeTrue();
        changed.Should().Be(1);
    }

    [Fact]
    public void ApplyPartyState_Newer_ClearsScenario()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyPrompt(Prompt(0));
        sut.ApplyPartyState(new PartyStateDto(Id, "Heroes", [Member("A", CharacterClass.Warrior, 7, 30)], DateTimeOffset.UtcNow, 1, 1, false));
        sut.HasScenario.Should().BeFalse();
    }

    [Fact]
    public void BuildActions_OwnTextPlusTeammates_ReplacesOwnSubmitted()
    {
        var sut = new GameSession();
        sut.Start(Party());
        var mate = new PlayerAction(Guid.NewGuid(), "B", "Read glyphs");
        sut.ApplyDiscussion(Id, [new PlayerAction(sut.PlayerId, "A", "old idea"), mate]);

        var actions = sut.BuildActions("  New idea ");

        actions.Should().HaveCount(2);
        actions.Should().Contain(mate);
        actions.Should().Contain(new PlayerAction(sut.PlayerId, sut.PlayerName, "New idea"));
    }

    [Fact]
    public void BuildActions_BlankOwnText_KeepsOwnSubmittedAction()
    {
        var sut = new GameSession();
        sut.Start(Party());
        var own = new PlayerAction(sut.PlayerId, "A", "old idea");
        sut.ApplyDiscussion(Id, [own]);

        sut.BuildActions("   ").Should().Equal(own);
    }

    [Fact]
    public void BuildActions_NothingAnywhere_IsEmpty_AndIgnoresBlankTeammateText()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyDiscussion(Id, [new PlayerAction(Guid.NewGuid(), "B", "  ")]);

        sut.BuildActions(null).Should().BeEmpty();
    }

    [Fact]
    public void Clear_ResetsEverything()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyPrompt(Prompt(0));
        sut.Apply(Turn(1, RoomType.Chest, null, 0, 5, 5));
        sut.Clear();

        sut.HasParty.Should().BeFalse();
        sut.Log.Should().BeEmpty();
        sut.Gold.Should().Be(0);
        sut.HasScenario.Should().BeFalse();
        sut.ScenarioPending.Should().BeFalse();
    }
}

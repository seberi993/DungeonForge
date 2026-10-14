using DungeonForge.Web.Components.Stage;
using DungeonForge.Web.Models;
using FluentAssertions;

namespace DungeonForge.Web.Tests;

public class StageDirectorTests
{
    private static MemberDto Hero(string name, CharacterClass cls = CharacterClass.Warrior, int hp = 20) => new(name, cls, hp, 20, hp == 0);

    private static ActionBeat Beat(string name, ActionVerb verb = ActionVerb.Strike, CharacterClass cls = CharacterClass.Warrior, string text = "I act") =>
        new(name, cls, verb, text);

    private static StageShow Show(int turn = 1, RoomType room = RoomType.Combat, bool success = true, string? target = null, int damage = 0, int gold = 0, params ActionBeat[] beats) =>
        new(turn, room, room == RoomType.Combat ? "goblin" : "chest", beats.Length == 0 ? [Beat("Conan"), Beat("Merlin", ActionVerb.Cast, CharacterClass.Mage), Beat("Kest", ActionVerb.Stab, CharacterClass.Bard), Beat("Mara", ActionVerb.Heal, CharacterClass.Priest)] : beats,
            success, target, damage, gold);

    private static StageDirector Director(params MemberDto[] heroes)
    {
        var d = new StageDirector();
        d.SetHeroes(heroes.Length == 0 ? [Hero("Conan"), Hero("Merlin", CharacterClass.Mage), Hero("Kest", CharacterClass.Bard), Hero("Mara", CharacterClass.Priest)] : heroes);
        return d;
    }

    private static void Run(StageDirector d, double ms, double step = 100)
    {
        for (double t = 0; t < ms; t += step) d.Advance(step);
    }

    [Fact]
    public void Idle_ShowsNamesAndHealthBars_OnA66By30Grid()
    {
        var d = Director();
        d.SetRoom(0, RoomType.Combat, "goblin");
        Run(d, StageDirector.IntroMs + 100);

        var frame = d.Frame();

        var lines = frame.Text.TrimEnd('\n').Split('\n');
        lines.Should().HaveCount(Corridor.Height).And.OnlyContain(l => l.Length == Corridor.Width);
        frame.Text.Should().Contain("Conan").And.Contain("Merlin").And.Contain("Kest").And.Contain("Mara");
        frame.Text.Should().Contain(new string('█', 8));
    }

    [Fact]
    public void Room_FadesIn_FoeAppearsAfterTheCameraPushIn()
    {
        var d = Director();
        d.SetRoom(0, RoomType.Combat, "skeleton");

        d.Frame().Text.Should().NotContain("|=|");
        Run(d, StageDirector.IntroMs + 100);
        d.Frame().Text.Should().Contain("|=|");
    }

    [Fact]
    public void StartShow_AcceptsEachTurnOnce_AndIgnoresEmptyShows()
    {
        var d = Director();

        d.StartShow(Show(turn: 1)).Should().BeTrue();
        d.StartShow(Show(turn: 1)).Should().BeFalse();
        d.StartShow(Show(turn: 0)).Should().BeFalse();
        new StageDirector().StartShow(new StageShow(5, RoomType.Combat, null, [], true, null, 0, 0)).Should().BeFalse();
    }

    [Fact]
    public void ShowFinished_FiresOnce_WhenTheLastBeatAndTheResultAreDone()
    {
        var d = Director();
        var finished = new List<int>();
        d.ShowFinished += finished.Add;
        var show = Show(turn: 4);
        d.StartShow(show);

        Run(d, StageDirector.ShowMs(show) - 200);
        finished.Should().BeEmpty();
        d.IsShowing.Should().BeTrue();

        Run(d, 400);
        finished.Should().Equal(4);
        d.IsShowing.Should().BeFalse();
        Run(d, 5000);
        finished.Should().Equal(4);
    }

    [Fact]
    public void Beat_QuotesThePlayersPlan_AndShowsTheOutcomeTagAfterImpact()
    {
        var d = Director();
        d.StartShow(Show(beats: [Beat("Conan", text: "I charge the goblin"), Beat("Merlin", ActionVerb.Cast, CharacterClass.Mage, "A fireball")]));
        var beatMs = StageDirector.BeatMs(2);

        Run(d, beatMs * 0.3);
        var early = d.Frame().Caption;
        early.Who.Should().Contain("Conan").And.Contain("Warrior");
        early.Say.Should().Contain("I charge the goblin");
        early.Tag.Should().BeEmpty();

        Run(d, beatMs * 0.5);
        d.Frame().Caption.Tag.Should().Be("HIT");

        Run(d, beatMs * 0.5);
        var second = d.Frame().Caption;
        second.Who.Should().Contain("Merlin");
        second.Say.Should().Contain("A fireball");
    }

    [Theory]
    [InlineData(ActionVerb.Strike, true, "HIT")]
    [InlineData(ActionVerb.Strike, false, "MISS")]
    [InlineData(ActionVerb.Cast, true, "BURN")]
    [InlineData(ActionVerb.Cast, false, "FIZZLE")]
    [InlineData(ActionVerb.Stab, true, "CRIT")]
    [InlineData(ActionVerb.Stab, false, "GRAZE")]
    public void CombatTags_FollowTheVerbAndTheRoll(ActionVerb verb, bool success, string tag)
    {
        var d = Director();
        d.StartShow(Show(success: success, beats: [Beat("Conan", verb)]));

        Run(d, StageDirector.BeatMs(1) * 0.8);

        d.Frame().Caption.Tag.Should().Be(tag);
    }

    [Theory]
    [InlineData(ActionVerb.Strike, "FORCED")]
    [InlineData(ActionVerb.Cast, "TRAP SPOTTED")]
    [InlineData(ActionVerb.Stab, "CLICK")]
    [InlineData(ActionVerb.Heal, "WARDED")]
    public void ChestRooms_ReadTheSameVerbsAsLocksAndTraps(ActionVerb verb, string tag)
    {
        var d = Director();
        d.StartShow(Show(room: RoomType.Chest, beats: [Beat("Conan", verb)]));

        Run(d, StageDirector.BeatMs(1) * 0.8);

        d.Frame().Caption.Tag.Should().Be(tag);
    }

    [Fact]
    public void Victory_LeavesTheFoeBeaten()
    {
        var d = Director();
        d.StartShow(Show(success: true));
        Run(d, StageDirector.ShowMs(Show()) + 200);

        var text = d.Frame().Text;

        text.Should().Contain("_.xX._");
        d.Frame().Caption.Who.Should().Be("Outcome");
    }

    [Fact]
    public void Failure_ReportsTheDamageAndWhoTookIt()
    {
        var d = Director();
        var show = Show(success: false, target: "Conan", damage: 6);
        d.StartShow(show);
        Run(d, StageDirector.BeatMs(4) * 4 + StageDirector.ResultMs * 0.9);

        var caption = d.Frame().Caption;

        caption.Say.Should().Contain("Conan takes 6");
        d.Frame().Text.Should().Contain("-6");
    }

    [Fact]
    public void ChestSuccess_ShowsTheGoldFound()
    {
        var d = Director();
        d.StartShow(Show(room: RoomType.Chest, success: true, gold: 12));
        Run(d, StageDirector.BeatMs(4) * 4 + StageDirector.ResultMs * 0.6);

        d.Frame().Caption.Say.Should().Contain("+12 gold");
    }

    [Fact]
    public void ANewRoom_WaitsForTheShowToFinish_ThenPushesIn()
    {
        var d = Director();
        d.SetRoom(0, RoomType.Combat, "goblin");
        Run(d, StageDirector.IntroMs + 100);
        var show = Show();
        d.StartShow(show);
        d.SetRoom(1, RoomType.Chest, "chest");

        Run(d, StageDirector.ShowMs(show) * 0.5);
        d.Frame().Text.Should().NotContain("|[=]|");

        Run(d, StageDirector.ShowMs(show) * 0.5 + 100);
        Run(d, StageDirector.IntroMs + 200);
        d.Frame().Text.Should().Contain("[]");
        d.Frame().Caption.Who.Should().Be("Treasure");
    }

    [Fact]
    public void AfterTheShow_WhileTheNarratorWrites_TheCaptionSaysSo()
    {
        var d = Director();
        var show = Show();
        d.StartShow(show);
        Run(d, StageDirector.ShowMs(show) + 200);
        d.NarrationPending = true;

        var caption = d.Frame().Caption;

        caption.Who.Should().Be("The dungeon master");
        caption.Say.Should().Contain("writing");
    }

    [Fact]
    public void AHeroDroppedThisTurn_StaysUpUntilTheBlowLands()
    {
        var d = Director(Hero("Conan"), Hero("Merlin", CharacterClass.Mage, hp: 0));
        d.StartShow(Show(success: false, target: "Merlin", damage: 8, beats: [Beat("Conan")]));

        Run(d, 500);
        d.Frame().Text.Should().NotContain("(x_x)");

        Run(d, StageDirector.BeatMs(1) + StageDirector.ResultMs * 0.7);
        d.Frame().Text.Should().Contain("(x_x)");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(8)]
    public void AnyPartySize_RendersTheWholeShow_WithEveryoneOnScreen(int size)
    {
        var classes = Enum.GetValues<CharacterClass>();
        var heroes = Enumerable.Range(0, size).Select(i => Hero("Hero" + i, classes[i % classes.Length])).ToArray();
        var d = Director(heroes);
        d.SetRoom(0, RoomType.Combat, "dragon");
        Run(d, StageDirector.IntroMs + 100);
        var beats = heroes.Select((h, i) => Beat(h.Name, (ActionVerb)(i % 4), h.Class, "plan " + i)).ToArray();
        var show = Show(success: false, target: "Hero0", damage: 3, beats: beats);
        d.StartShow(show);

        var seen = new HashSet<string>();
        for (double t = 0; t < StageDirector.ShowMs(show) + 1000; t += 90)
        {
            d.Advance(90);
            var frame = d.Frame();
            frame.Text.TrimEnd('\n').Split('\n').Should().OnlyContain(l => l.Length == Corridor.Width);
            foreach (var h in heroes.Where(h => frame.Text.Contains(h.Name))) seen.Add(h.Name);
        }

        seen.Should().BeEquivalentTo(heroes.Select(h => h.Name));
    }

    [Fact]
    public void EveryRoomAndSceneTag_DrawsWithoutError()
    {
        foreach (var room in Enum.GetValues<RoomType>())
        {
            foreach (var scene in new[] { "goblin", "skeleton", "rat", "dragon", "ghost", "chest", "door", "lever", "trap", "altar", "generic", "unknown", null })
            {
                var d = Director();
                d.SetRoom(0, room, scene);
                Run(d, StageDirector.IntroMs + 100);
                var show = new StageShow(1, room, scene, [Beat("Conan"), Beat("Merlin", ActionVerb.Cast, CharacterClass.Mage)], true, null, 0, 5);
                d.StartShow(show);
                Run(d, StageDirector.ShowMs(show) + 300, 120);
                d.Frame().Html.Should().NotBeNullOrEmpty();
            }
        }
    }

    [Fact]
    public void NoHeroesAndNoRoom_StillDraws()
    {
        var d = new StageDirector();

        d.Frame().Text.Should().NotBeNullOrEmpty();
        d.Frame().Caption.Who.Should().Be("The hall");
    }

    private static StageDirector DirectorShowing(RollDto roll, bool success)
    {
        var d = Director(Hero("Conan"));
        d.SetRoom(0, RoomType.Combat, "goblin");
        Run(d, StageDirector.IntroMs + 100);
        d.StartShow(new StageShow(1, RoomType.Combat, "goblin", [Beat("Conan")], success, null, 0, 0, [roll]));
        return d;
    }

    [Fact]
    public void Beat_TumblesTheDie_ThenLandsOnTheRealRollAgainstTheChance()
    {
        var d = DirectorShowing(new RollDto("Conan", 37, 60, false, false), success: true);

        Run(d, 600);
        d.Frame().Text.Should().Contain("d100");
        d.Frame().Text.Should().NotContain("37 vs 60");

        Run(d, 1000);
        d.Frame().Text.Should().Contain("37 vs 60");
    }

    [Theory]
    [InlineData(2, 50, true, false, "CRIT!")]
    [InlineData(97, 50, false, true, "FUMBLE!")]
    public void Beat_CritAndFumbleOverrideTheResultTag(int roll, int chance, bool crit, bool fumble, string tag)
    {
        var d = DirectorShowing(new RollDto("Conan", roll, chance, crit, fumble), success: crit);

        Run(d, 1700);

        d.Frame().Caption.Tag.Should().Be(tag);
    }

    [Fact]
    public void Beat_WithoutRolls_KeepsTheVerbTagAndDrawsNoDie()
    {
        var d = Director(Hero("Conan"));
        d.SetRoom(0, RoomType.Combat, "goblin");
        Run(d, StageDirector.IntroMs + 100);
        d.StartShow(new StageShow(1, RoomType.Combat, "goblin", [Beat("Conan")], true, null, 0, 0));

        Run(d, 1700);

        d.Frame().Caption.Tag.Should().Be("HIT");
        d.Frame().Text.Should().NotContain("d100");
    }
}

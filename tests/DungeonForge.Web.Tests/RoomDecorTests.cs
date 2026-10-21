using FluentAssertions;
using DungeonForge.Web.Features.Dungeon;
using DungeonForge.Web.Features.Dungeon.Stage;
using DungeonForge.Web.Shared;

namespace DungeonForge.Web.Tests;

public class RoomDecorTests
{
    private const string Text = "A **torch** gutters while a **rat** darts past **dripping** pipes and a **zorblax** watches. What do you do?";

    [Fact]
    public void Compose_IsDeterministic()
    {
        var a = RoomDecor.Compose(Text, 3);
        var b = RoomDecor.Compose(Text, 3);

        a.Props.Select(p => (p.Word, p.X, p.Y)).Should().Equal(b.Props.Select(p => (p.Word, p.X, p.Y)));
        (a.Frame, a.Floor, a.Ceiling, a.Door).Should().Be((b.Frame, b.Floor, b.Ceiling, b.Door));
    }

    [Fact]
    public void Compose_DifferentEncounters_LookDifferent()
    {
        var looks = Enumerable.Range(0, 12)
            .Select(i => RoomDecor.Compose($"Room {i} with a **door** and **chains**.", i))
            .Select(d => (d.Frame, d.Floor, d.Ceiling, d.Door, string.Join(",", d.TorchDepths), string.Join(";", d.Props.Select(p => (p.X, p.Y)))))
            .Distinct();

        looks.Should().HaveCountGreaterThan(5);
    }

    [Fact]
    public void Compose_GivesEveryHighlightAProp_AndUnknownWordsSparkle()
    {
        var decor = RoomDecor.Compose(Text, 1);

        decor.Props.Select(p => p.Kind.Id).Should().Equal("torch", "rat", "drip", "sparkle");
    }

    [Fact]
    public void Compose_NeverStacksTwoPropsOnOneSpot()
    {
        var decor = RoomDecor.Compose("**fire** **torch** **lantern** **candle** **brazier** **ember** **lamp**", 7);

        decor.Props.Should().HaveCount(RoomDecor.MaxProps);
        decor.Props.Select(p => (p.X, p.Y)).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("No highlights here.")]
    [InlineData("Unbalanced **markers")]
    public void Compose_WithoutHighlights_StillDressesTheHall(string? text)
    {
        var decor = RoomDecor.Compose(text, 2);

        decor.Props.Should().BeEmpty();
        decor.TorchDepths.Should().NotBeNull();
    }

    [Theory]
    [InlineData("bat", "bat")]
    [InlineData("battle", "sparkle")]
    [InlineData("Rats", "rat")]
    [InlineData("rather", "sparkle")]
    [InlineData("rusty chains", "chain")]
    [InlineData("trapdoor", "door")]
    [InlineData("", "sparkle")]
    public void Resolve_MatchesWholeWordsForShortStems(string word, string expected) =>
        PropArt.Resolve(word).Id.Should().Be(expected);

    [Fact]
    public void EveryProp_AnimatesOverTime_AndStaysOnTheGrid()
    {
        foreach (var kind in PropArt.Kinds.Append(PropArt.Sparkle))
        {
            var prop = new PlacedProp(kind.Id, kind, 20, 10, 0, 0);
            var seen = new HashSet<string>();
            for (double age = PropArt.BurstMs; age < PropArt.BurstMs + 8000; age += 90)
            {
                var g = new CharGrid(Corridor.Width, Corridor.Height);
                PropArt.Draw(g, prop, age, age);
                seen.Add(g.ToHtml());
            }

            seen.Count.Should().BeGreaterThan(1, $"{kind.Id} should visibly move");
        }
    }

    [Fact]
    public void Prop_IsHiddenBeforeItsTime_ThenPopsIn()
    {
        var prop = new PlacedProp("torch", PropArt.Kinds[0], 20, 10, 0, 0);
        var before = new CharGrid(Corridor.Width, Corridor.Height);
        var burst = new CharGrid(Corridor.Width, Corridor.Height);
        PropArt.Draw(before, prop, 0, -1);
        PropArt.Draw(burst, prop, 0, 10);

        before.ToPlainText().Trim().Should().BeEmpty();
        burst.ToPlainText().Should().Contain("*");
    }

    [Fact]
    public void Director_PaintsTheNarrationIntoTheHall_WithoutReplayingThePushIn()
    {
        var d = new StageDirector();
        d.SetRoom(1, RoomType.Combat, "goblin");
        for (var i = 0; i < 40; i++) d.Advance(100);
        var bare = d.Frame().Text;

        d.SetRoom(1, RoomType.Combat, "goblin", "A **spider** hangs overhead.");
        for (var i = 0; i < 30; i++) d.Advance(100);

        d.Props.Should().ContainSingle().Which.Kind.Id.Should().Be("spider");
        d.Frame().Text.Should().NotBe(bare);
        d.Frame().Text.Should().MatchRegex(@"[/\\]o[/\\]");
    }

    [Fact]
    public void Director_NewNarrationWaitsForTheShow()
    {
        var d = new StageDirector();
        d.SetHeroes([new MemberDto("Conan", CharacterClass.Warrior, 20, 20, false)]);
        d.SetRoom(1, RoomType.Combat, "goblin", "A **rat** scurries.");
        d.StartShow(new StageShow(1, RoomType.Combat, "goblin", [new ActionBeat("Conan", CharacterClass.Warrior, ActionVerb.Strike, "hit")], true, null, 0, 0));

        d.SetRoom(2, RoomType.Chest, "chest", "A **door** creaks.");

        d.Props.Single().Kind.Id.Should().Be("rat");
        d.Advance(StageDirector.ShowMs(new StageShow(1, RoomType.Combat, null, [new ActionBeat("Conan", CharacterClass.Warrior, ActionVerb.Strike, "hit")], true, null, 0, 0)) + 1);
        d.Props.Single().Kind.Id.Should().Be("door");
    }

    [Fact]
    public void Typewriter_ColoursAHighlightLikeItsProp()
    {
        TypewriterText.KeyClass("torch").Should().Be("key st-p-fire");
        TypewriterText.KeyClass("zorblax").Should().Be("key st-p-arcane");
    }
}

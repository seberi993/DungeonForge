using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Infrastructure.Narration;
using FluentAssertions;

namespace DungeonForge.Tests.Infrastructure.Narration;

public class PsychologicalPromptTests
{
    private static IEnumerable<string> SystemPrompts() =>
    [
        NarrationPrompts.Scenario(RoomType.Combat),
        NarrationPrompts.Turn(RoomType.Chest),
        NarrationPrompts.Turn(null),
        NarrationPrompts.ScenarioCompact(RoomType.Puzzle),
        NarrationPrompts.TurnCompact(RoomType.Boss),
        NarrationPrompts.TurnCompact(null),
    ];

    [Fact]
    public void EverySystemPrompt_EnforcesTwoToThreeSentences()
    {
        foreach (var prompt in SystemPrompts())
        {
            prompt.Should().Contain("2 to 3 sentences");
            prompt.Should().NotContain("3 to 5").And.NotContain("3 or 4 sentences").And.NotContain("120 to 180");
        }
    }

    [Fact]
    public void EverySystemPrompt_BansTropes_AndNoTropeLeaksOutsideTheBanClause()
    {
        foreach (var prompt in SystemPrompts())
        {
            prompt.Should().Contain(NarrationPrompts.TropeBan);
            var rest = prompt.Replace(NarrationPrompts.TropeBan, string.Empty);
            foreach (var trope in GrotesqueVocabulary.ForbiddenTropes)
            {
                rest.Should().NotContainEquivalentOf(trope);
            }
        }
    }

    [Fact]
    public void EverySystemPrompt_IsSadisticGossiperPushingDistrust()
    {
        foreach (var prompt in SystemPrompts())
        {
            prompt.Should().ContainEquivalentOf("sadistic").And.ContainEquivalentOf("distrust");
        }
    }

    [Fact]
    public void TurnPrompts_AskForWhispersInTheStableFormat()
    {
        NarrationPrompts.Turn(RoomType.Combat).Should().Contain("whispers").And.Contain(TurnNarrationParser.WhisperFormat);
        NarrationPrompts.Turn(null).Should().Contain("whispers").And.Contain(TurnNarrationParser.WhisperFormat);
        NarrationPrompts.TurnCompact(RoomType.Combat).Should().Contain("whispers");
        NarrationPrompts.TurnJsonSchema(true).Should().Contain("whispers");
    }

    private static NarrationContext Ctx() => new(RoomType.Combat, "Heroes", 3, false)
    {
        Roster = [new("Conan", CharacterClass.Warrior), new("Merlin", CharacterClass.Mage)],
        Motifs = ["bleeding mosaic tiles", "mercury pools"],
        Treachery = ["Conan backstabbed Merlin and it landed", "Merlin's spell was sabotaged by Conan"],
        NextRoom = RoomType.Chest,
        Actions = [new(Guid.NewGuid(), "Conan", "hold the line")],
    };

    [Fact]
    public void Build_InjectsMotifsRosterAndRevealedTreachery()
    {
        var prompt = MasterPromptBuilder.Build(true, Ctx());

        prompt.Should().Contain("bleeding mosaic tiles").And.Contain("mercury pools")
            .And.Contain("Conan (Warrior)").And.Contain("Merlin (Mage)")
            .And.Contain("Conan backstabbed Merlin").And.Contain("sabotaged");
        prompt.Length.Should().BeLessThanOrEqualTo(MasterPromptBuilder.MaxLength);
    }

    [Fact]
    public void Build_WithoutExtras_StaysFreeOfMotifAndTreacheryClauses()
    {
        var prompt = MasterPromptBuilder.Build(true, new NarrationContext(RoomType.Combat, "Heroes", 1, false));

        prompt.Should().NotContain("Weave in").And.NotContain("Revealed treachery").And.NotContain("Roster");
    }

    [Fact]
    public void BuildScenario_InjectsMotifs() =>
        MasterPromptBuilder.BuildScenario(Ctx()).Should().Contain("bleeding mosaic tiles");

    [Fact]
    public void Build_MotifsAreSanitizedToOneLine()
    {
        var prompt = MasterPromptBuilder.Build(true, Ctx() with { Motifs = ["a\nb"] });

        prompt.Should().NotContain("\n").And.Contain("a b");
    }
}

using DungeonForge.Domain;
using DungeonForge.Application.Narration;
using FluentAssertions;

namespace DungeonForge.Tests.Infrastructure.Narration;

public class StoryPromptTests
{
    private static NarrationContext Ctx() => new(RoomType.Combat, "Heroes", 0, false)
    {
        SetupAnswers = [new SetupAnswer("Conan", "A drowned chapel that smells of lamp oil"), new SetupAnswer("Merlin", "I owe the ferryman a debt")],
    };

    [Fact]
    public void BuildScenario_CarriesEveryPlayersOpeningAnswer()
    {
        var prompt = MasterPromptBuilder.BuildScenario(Ctx());

        prompt.Should().Contain("Conan: A drowned chapel that smells of lamp oil").And.Contain("Merlin: I owe the ferryman a debt");
    }

    [Fact]
    public void Build_CarriesSetupAnswersAndRecentOutcomes_WithinTheLengthCap()
    {
        var prompt = MasterPromptBuilder.Build(true, new NarrationContext(RoomType.Combat, "Heroes", 4, false)
        {
            SetupAnswers = Ctx().SetupAnswers,
            RecentOutcomes = ["The lamps guttered out.", "A ferry bell tolled."],
            NextRoom = RoomType.Chest,
        });

        prompt.Should().Contain("drowned chapel").And.Contain("The lamps guttered out.").And.Contain("A ferry bell tolled.");
        prompt.Length.Should().BeLessThanOrEqualTo(MasterPromptBuilder.MaxLength);
    }

    [Fact]
    public void NoSetup_AddsNoPlayerCanonSection()
    {
        var prompt = MasterPromptBuilder.BuildScenario(new NarrationContext(RoomType.Chest, "Heroes", 0, false));

        prompt.Should().NotContain("painted");
    }

    [Fact]
    public void SystemPrompts_TreatPlayerWordsAsCanon_AndDemandAChallenge()
    {
        foreach (var prompt in new[]
                 {
                     NarrationPrompts.Scenario(RoomType.Combat),
                     NarrationPrompts.Turn(RoomType.Puzzle),
                     NarrationPrompts.ScenarioCompact(RoomType.Chest),
                     NarrationPrompts.TurnCompact(RoomType.Boss),
                 })
        {
            prompt.Should().ContainEquivalentOf("canon").And.ContainEquivalentOf("challenge");
        }
    }
}

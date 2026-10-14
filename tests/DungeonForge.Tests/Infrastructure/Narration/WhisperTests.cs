using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Infrastructure.Narration;
using FluentAssertions;

namespace DungeonForge.Tests.Infrastructure.Narration;

public class WhisperTests
{
    [Theory]
    [InlineData("Whisper to Mage: 'The Priest took 10 extra gold while you were looking away'", "Mage", "The Priest took 10 extra gold while you were looking away")]
    [InlineData("Whisper to Merlin: \"Lyra smiled too widely.\"", "Merlin", "Lyra smiled too widely.")]
    [InlineData("whisper to Bard: no quotes needed", "Bard", "no quotes needed")]
    [InlineData("  - Whisper to Dru:   'padded'  ", "Dru", "padded")]
    public void ParseWhisper_ReadsTargetAndText(string line, string target, string text)
    {
        var whisper = TurnNarrationParser.ParseWhisper(line);

        whisper.Should().Be(new Whisper(target, text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Just a normal sentence.")]
    [InlineData("Whisper to : 'nobody'")]
    [InlineData("Whisper to Mage:   ''  ")]
    public void ParseWhisper_RejectsNonWhispers(string? line) =>
        TurnNarrationParser.ParseWhisper(line).Should().BeNull();

    [Fact]
    public void Format_RoundTripsThroughParse()
    {
        var whisper = new Whisper("Mage", "Trust no one.");

        TurnNarrationParser.ParseWhisper(whisper.Format()).Should().Be(whisper);
        whisper.Format().Should().Be("Whisper to Mage: 'Trust no one.'");
    }

    [Fact]
    public void ParseWhisper_CapsTextLength()
    {
        var whisper = TurnNarrationParser.ParseWhisper($"Whisper to Mage: '{new string('x', 500)}'");

        whisper!.Text.Length.Should().BeLessThanOrEqualTo(TurnNarrationParser.MaxWhisperLength);
    }

    [Fact]
    public void Parse_ReadsWhispersArray_DropsMalformedAndCapsCount()
    {
        const string json = """
            {"outcome":"Ugh.","whispers":["Whisper to Mage: 'one'","garbage","Whisper to Bard: 'two'","Whisper to Dru: 'three'","Whisper to Conan: 'four'"]}
            """;

        var parsed = TurnNarrationParser.Parse(json)!;

        parsed.Whispers.Should().Equal(new Whisper("Mage", "one"), new Whisper("Bard", "two"), new Whisper("Dru", "three"));
    }

    [Fact]
    public void Parse_AcceptsObjectWhispers()
    {
        var parsed = TurnNarrationParser.Parse("""{"outcome":"x","whispers":[{"to":"Mage","text":"psst"}]}""")!;

        parsed.Whispers.Should().Equal(new Whisper("Mage", "psst"));
    }

    [Fact]
    public void Parse_WithoutWhispers_YieldsNone() =>
        TurnNarrationParser.Parse("""{"outcome":"x"}""")!.Whispers.Should().BeEmpty();

    private static NarrationContext Ctx(int members, int turn = 4) => new(RoomType.Combat, "Heroes", turn, false)
    {
        Roster = [.. new[]
        {
            new RosterEntry("Conan", CharacterClass.Warrior), new RosterEntry("Merlin", CharacterClass.Mage),
            new RosterEntry("Mercy", CharacterClass.Priest),
        }.Take(members)],
    };

    [Fact]
    public void FallbackWhispers_TargetOnePlayerAboutAnother_InParsableFormat()
    {
        var whispers = FallbackFlavor.Whispers(Ctx(3).Roster, 4);

        whispers.Should().NotBeEmpty();
        foreach (var w in whispers)
        {
            TurnNarrationParser.ParseWhisper(w.Format()).Should().Be(w);
            Ctx(3).Roster.Select(r => r.Name).Should().Contain(w.Target);
            w.Text.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void FallbackWhispers_NeedAtLeastTwoMembers() =>
        FallbackFlavor.Whispers(Ctx(1).Roster, 4).Should().BeEmpty();

    [Fact]
    public async Task StaticNarrator_AttachesFallbackWhispers_ForARoster()
    {
        var result = await new StaticDungeonNarrator().NarrateTurnAsync(true, Ctx(3), CancellationToken.None);

        result.Whispers.Should().NotBeEmpty();
    }

    [Fact]
    public void Complete_KeepsModelWhispers_AndOtherwiseFillsFallback()
    {
        var model = new TurnNarration("o", "n") { Whispers = [new Whisper("Mage", "mine")] };

        FallbackFlavor.Complete(model, true, Ctx(3) with { NextRoom = RoomType.Chest }).Whispers
            .Should().Equal(new Whisper("Mage", "mine"));
        FallbackFlavor.Complete(new TurnNarration("o", "n"), true, Ctx(3) with { NextRoom = RoomType.Chest }).Whispers
            .Should().NotBeEmpty();
    }

    [Fact]
    public void FallbackFlavor_NoLongerUsesBannedTropes()
    {
        var all = Enum.GetValues<RoomType>().SelectMany(r => Enumerable.Range(0, 2).SelectMany(t => new[]
        {
            FallbackFlavor.Scenario(r, t), FallbackFlavor.Outcome(r, true, t), FallbackFlavor.Outcome(r, false, t),
        }));

        foreach (var text in all)
        {
            foreach (var trope in GrotesqueVocabulary.ForbiddenTropes)
            {
                text.Should().NotContainEquivalentOf(trope);
            }
        }
    }
}

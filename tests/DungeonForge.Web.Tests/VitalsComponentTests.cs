using Bunit;
using FluentAssertions;
using DungeonForge.Web.Features.Dungeon;
using DungeonForge.Web.Shared;

namespace DungeonForge.Web.Tests;

public class VitalsComponentTests : TestContext
{
    [Fact]
    public void StatBar_RendersValueWidthAndAria()
    {
        var cut = RenderComponent<StatBar>(p => p.Add(c => c.Kind, "mana").Add(c => c.Label, "MP").Add(c => c.Value, 20).Add(c => c.Max, 80));

        var bar = cut.Find(".mana-bar");
        bar.GetAttribute("aria-valuenow").Should().Be("20");
        bar.GetAttribute("aria-valuemax").Should().Be("80");
        cut.Find(".mana-fill").GetAttribute("style").Should().Contain("width:25%");
        cut.Find(".stat-text").TextContent.Should().Contain("MP").And.Contain("20/80");
    }

    [Theory]
    [InlineData(30, 100, true)]
    [InlineData(31, 100, false)]
    [InlineData(0, 100, true)]
    public void StatBar_LowState_AtThirtyPercentOrLess(int value, int max, bool low)
    {
        var cut = RenderComponent<StatBar>(p => p.Add(c => c.Kind, "hp").Add(c => c.Value, value).Add(c => c.Max, max));

        cut.Find(".hp-fill").ClassList.Contains("hp-low").Should().Be(low);
    }

    [Fact]
    public void StatBar_ZeroMax_DoesNotDivideByZero()
    {
        var cut = RenderComponent<StatBar>(p => p.Add(c => c.Kind, "hp").Add(c => c.Value, 5).Add(c => c.Max, 0));

        cut.Find(".hp-fill").GetAttribute("style").Should().Contain("width:0%");
    }

    private static readonly IReadOnlyList<MemberDto> Heroes =
        [new("Conan", CharacterClass.Warrior, 30, 30, false), new("Merlin", CharacterClass.Mage, 0, 16, true)];

    [Fact]
    public void PartyPanel_ShowsEachHeroWithHp_AndNoClassOrMana()
    {
        var cut = RenderComponent<PartyPanel>(p => p
            .Add(c => c.Members, Heroes).Add(c => c.MyName, "Conan"));

        cut.FindAll("#members li").Should().HaveCount(2);
        cut.Find("#member-0").TextContent.Should().Contain("Conan").And.Contain("30/30");
        cut.Find("#member-0").ClassList.Should().Contain("me");
        cut.FindAll(".mana-bar, .member-class, .member-icon").Should().BeEmpty();
        cut.Find("#member-0").TextContent.Should().NotContain("Warrior");
    }

    [Fact]
    public void PartyPanel_DefeatedHero_IsMarked()
    {
        var cut = RenderComponent<PartyPanel>(p => p.Add(c => c.Members, Heroes).Add(c => c.MyName, "Conan"));

        cut.Find("#member-1").ClassList.Should().Contain("defeated-member");
        cut.Find("#member-1 .member-ko").TextContent.Should().Be("KO");
        cut.FindAll("#member-0 .member-ko").Should().BeEmpty();
    }
}

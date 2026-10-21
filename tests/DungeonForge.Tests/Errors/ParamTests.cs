using DungeonForge.Application.Errors;
using FluentAssertions;

namespace DungeonForge.Tests.Errors;

// Parameters can only be built through Param, so the allow-list is the type system: no free text, no secrets.
public class ParamTests
{
    [Fact]
    public void PlayerName_IsCutToAShortPreview()
    {
        var param = Param.PlayerName(new string('x', 200));

        param.Key.Should().Be("playerName");
        param.Value.Should().HaveLength(40);
    }

    [Fact]
    public void Code_IsCutToAShortPreview()
    {
        Param.Code(new string('A', 100)).Value.Should().HaveLength(16);
    }

    [Fact]
    public void EveryFactoryUsesAKeyFromTheAllowList()
    {
        Param[] all =
        [
            Param.PartyId(Guid.Empty), Param.MemberId(Guid.Empty), Param.Turn(1), Param.Floor(1), Param.PlayerName("a"),
            Param.Room("combat"), Param.Locked(1), Param.Required(1), Param.Code("A"),
        ];

        all.Select(p => p.Key).Should().BeEquivalentTo(Param.AllowedKeys).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Create_BuildsTheParametersDictionary()
    {
        var error = new ErrorSpec("test.failed", ErrorKind.NotFound, "Missing.")
            .Create("Query:Test", Param.Locked(2), Param.Required(3));

        error.Parameters.Should().BeEquivalentTo(new Dictionary<string, string> { ["locked"] = "2", ["required"] = "3" });
    }
}

using System.Reflection;
using DungeonForge.Api.Errors;
using FluentAssertions;

namespace DungeonForge.Tests.Errors;

// docs/errors.md is generated from the error catalog, the [CoversError] markers and the operation constants.
// A stale file fails the golden test; rewrite it with `UPDATE_ERRORS_DOC=1 dotnet test --filter ErrorsDocTests`.
public class ErrorsDocTests
{
    private static readonly ErrorSpec Spec = new("area.thing_missing", ErrorKind.NotFound, "The thing is missing.", DeclaredIn: "/x/src/DungeonForge.Api/Features/A/B/Errors.cs");

    [Fact]
    public void Render_ListsEachCodeWithKindStatusDefinitionAndCoveringTests()
    {
        var doc = ErrorsDoc.Render(
            [Spec, new ErrorSpec("area.other", ErrorKind.Conflict, "Other.", 400, DeclaredIn: "/x/src/DungeonForge.Api/Features/A/Errors.cs")],
            [
                new CoveredBy("area.thing_missing", "/x/tests/DungeonForge.Tests/Features/BTests.cs", "Missing_Returns404"),
                new CoveredBy("area.thing_missing", "/x/tests/DungeonForge.Tests/Features/ATests.cs", "Another_Case"),
            ],
            [new("Query:GetThing", "DungeonForge.Api.Features.A.B")]);

        doc.Should().Contain(
            "| `area.thing_missing` | NotFound | 404 | The thing is missing. | `src/DungeonForge.Api/Features/A/B/Errors.cs` | " +
            "`tests/DungeonForge.Tests/Features/ATests.cs#Another_Case`, `tests/DungeonForge.Tests/Features/BTests.cs#Missing_Returns404` |");
        doc.Should().Contain("| `area.other` | Conflict | 400 | Other. | `src/DungeonForge.Api/Features/A/Errors.cs` | none |");
        doc.Should().Contain("| `Query:GetThing` | `Features/A/B` |");
    }

    [Fact]
    public void Render_SortsCodesAlphabetically()
    {
        var doc = ErrorsDoc.Render(
            [new("b.two", ErrorKind.Conflict, "Two."), new("a.one", ErrorKind.Conflict, "One.")], [], []);

        doc.IndexOf("`a.one`", StringComparison.Ordinal).Should().BeLessThan(doc.IndexOf("`b.two`", StringComparison.Ordinal));
    }

    [Fact]
    public void Operations_AreFoundOnHandlersAndOperationClasses()
    {
        var operations = ErrorCatalog.Operations(typeof(ErrorCodes).Assembly).Select(o => o.Operation).ToList();

        operations.Should().Contain(["Command:UnlockTurn", "Query:GetScenario", "Command:ResolveTurn", "Hub:JoinParty", "Chaos:Send"]);
    }

    [Fact]
    public void ErrorsMd_MatchesTheCatalog()
    {
        var path = Path.Combine(ErrorsDoc.RepoRoot(), "docs", "errors.md");
        var expected = ErrorsDoc.RenderCurrent(typeof(ErrorCodes).Assembly, typeof(ErrorsDocTests).Assembly);

        if (Environment.GetEnvironmentVariable("UPDATE_ERRORS_DOC") == "1")
        {
            File.WriteAllText(path, expected);
        }

        File.ReadAllText(path).ReplaceLineEndings("\n").Should().Be(
            expected.ReplaceLineEndings("\n"),
            "docs/errors.md is generated; run UPDATE_ERRORS_DOC=1 dotnet test --filter ErrorsDocTests to refresh it");
    }
}

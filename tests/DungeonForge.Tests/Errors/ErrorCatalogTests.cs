using System.Reflection;
using DungeonForge.Api.Errors;
using FluentAssertions;

namespace DungeonForge.Tests.Errors;

public class ErrorCatalogTests
{
    // Fixtures the catalog rules are checked against. The real catalog is checked in the last test.
    private static class FakeCodes
    {
        public const string Covered = "fake.covered";
        public const string NoSpec = "fake.no_spec";
        public const string NoTest = "fake.no_test";
        public const string Malformed = "Fake-Malformed";
    }

    private static class FakeSpecs
    {
        public static readonly ErrorSpec Covered = new(FakeCodes.Covered, ErrorKind.NotFound, "Covered.");
        public static readonly ErrorSpec NoTest = new(FakeCodes.NoTest, ErrorKind.Conflict, "No test.");
        public static readonly ErrorSpec Malformed = new(FakeCodes.Malformed, ErrorKind.Conflict, "Malformed.");
        public static readonly ErrorSpec Orphan = new("fake.orphan", ErrorKind.Conflict, "Not a constant.");
        public static readonly ErrorSpec Duplicate = new(FakeCodes.Covered, ErrorKind.Conflict, "Twice.");
    }

    [Fact]
    public void Codes_ListsEveryConstant()
    {
        ErrorCatalog.Codes(typeof(FakeCodes)).Should().BeEquivalentTo(
            FakeCodes.Covered, FakeCodes.NoSpec, FakeCodes.NoTest, FakeCodes.Malformed);
    }

    [Fact]
    public void Specs_ListsStaticSpecFields()
    {
        ErrorCatalog.Specs(typeof(ErrorCatalogTests).Assembly).Select(s => s.Code).Should().Contain(FakeCodes.NoTest);
    }

    [Fact]
    public void Problems_ReportsEachBrokenRule()
    {
        var problems = ErrorCatalog.Problems(typeof(FakeCodes), typeof(ErrorCatalogTests).Assembly, [FakeCodes.Covered]);

        problems.Should().Contain(p => p.Contains(FakeCodes.NoSpec) && p.Contains("no spec"));
        problems.Should().Contain(p => p.Contains(FakeCodes.NoTest) && p.Contains("[CoversError]"));
        problems.Should().Contain(p => p.Contains(FakeCodes.Malformed) && p.Contains("<area>.<reason>"));
        problems.Should().Contain(p => p.Contains("fake.orphan") && p.Contains("not an ErrorCodes constant"));
        problems.Should().Contain(p => p.Contains(FakeCodes.Covered) && p.Contains("more than one spec"));
    }

    [Fact]
    public void EveryRealErrorCode_HasOneSpecAndAtLeastOneCoveringTest()
    {
        var api = typeof(ErrorCodes).Assembly;
        var covered = typeof(ErrorCatalogTests).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .SelectMany(m => m.GetCustomAttributes<CoversErrorAttribute>())
            .Select(a => a.Code)
            .ToHashSet();

        ErrorCatalog.Problems(typeof(ErrorCodes), api, covered).Should().BeEmpty();
    }
}

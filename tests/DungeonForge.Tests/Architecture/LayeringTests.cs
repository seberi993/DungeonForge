using System.Xml.Linq;
using DungeonForge.Tests.Errors;
using FluentAssertions;

namespace DungeonForge.Tests.Architecture;

// Dependencies point inward only:   Api -> Infrastructure -> Application -> Domain -> Shared   (Api also -> Application)
//   Shared          no references.
//   Domain          Shared only; no framework beyond the BCL (game rules and state stand alone).
//   Application     Domain only; no ASP.NET (HTTP lives at the Api edge) and no concrete adapters.
//   Infrastructure  Application only (adapters implement Application ports).
//   Api             Application and Infrastructure (composition root).
// Project references are checked from the csproj files; "no ASP.NET" from the source text, because reflection cannot see
// a `using`.
public class LayeringTests
{
    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        ["DungeonForge.Shared"] = [],
        ["DungeonForge.Domain"] = ["DungeonForge.Shared"],
        ["DungeonForge.Application"] = ["DungeonForge.Domain"],
        ["DungeonForge.Infrastructure"] = ["DungeonForge.Application"],
        ["DungeonForge.Api"] = ["DungeonForge.Application", "DungeonForge.Infrastructure"],
    };

    private static string Src(string project) => Path.Combine(ErrorsDoc.RepoRoot(), "src", project);

    public static TheoryData<string> Projects => [.. Allowed.Keys];

    [Theory]
    [MemberData(nameof(Projects))]
    public void ProjectReferences_PointInward(string project)
    {
        var csproj = XDocument.Load(Path.Combine(Src(project), project + ".csproj"));
        var references = csproj.Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value.Replace('\\', '/')))
            .ToArray();

        references.Should().BeSubsetOf(Allowed[project], $"{project} may only depend inward");
    }

    private static IEnumerable<string> Offences(string project, params string[] forbidden)
    {
        var root = Src(project);
        var files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToArray();
        files.Should().NotBeEmpty($"{root} should hold the {project} sources");

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var name in forbidden.Where(n => lines[i].Contains(n, StringComparison.Ordinal)))
                {
                    yield return $"{Path.GetRelativePath(ErrorsDoc.RepoRoot(), file).Replace('\\', '/')}:{i + 1} names {name}";
                }
            }
        }
    }

    [Fact]
    public void Domain_NeverNamesAspNetOrOuterLayers() =>
        Offences("DungeonForge.Domain", "Microsoft.AspNetCore", "DungeonForge.Application", "DungeonForge.Infrastructure", "DungeonForge.Api")
            .Should().BeEmpty();

    [Fact]
    public void Application_NeverNamesAspNetOrOuterLayers() =>
        Offences("DungeonForge.Application", "Microsoft.AspNetCore", "DungeonForge.Infrastructure", "DungeonForge.Api")
            .Should().BeEmpty();
}

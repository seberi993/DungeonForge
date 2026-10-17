using DungeonForge.Tests.Errors;
using FluentAssertions;

namespace DungeonForge.Tests.Architecture;

// Layering rules, checked on the source text: reflection cannot see a `using` or a call inside a method body, and
// a new package for a one-line rule is not worth it. The rule is "this folder never names that namespace".
//   Domain          knows nothing of Features or Infrastructure (game rules and state stand alone).
//   Errors          knows nothing of Features or Domain (it is the neutral layer every slice reports through).
public class LayeringTests
{
    private static string ApiFolder(string name) => Path.Combine(ErrorsDoc.RepoRoot(), "src", "DungeonForge.Api", name);

    private static IEnumerable<string> Offences(string folder, params string[] forbiddenNamespaces)
    {
        var root = ApiFolder(folder);
        var files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
        files.Should().NotBeEmpty($"{root} should hold the {folder} sources");

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var forbidden in forbiddenNamespaces)
                {
                    if (lines[i].Contains($"DungeonForge.Api.{forbidden}", StringComparison.Ordinal))
                    {
                        yield return $"{Path.GetRelativePath(ErrorsDoc.RepoRoot(), file).Replace('\\', '/')}:{i + 1} names DungeonForge.Api.{forbidden}";
                    }
                }
            }
        }
    }

    [Fact]
    public void Domain_NeverReferencesFeaturesOrInfrastructure() =>
        Offences("Domain", "Features", "Infrastructure").Should().BeEmpty();

    [Fact]
    public void Errors_NeverReferencesFeaturesOrDomain() =>
        Offences("Errors", "Features", "Domain").Should().BeEmpty();
}

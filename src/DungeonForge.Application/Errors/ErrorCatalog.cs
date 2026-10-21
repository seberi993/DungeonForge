using System.Reflection;
using System.Text.RegularExpressions;

namespace DungeonForge.Application.Errors;

// Reflection view of the error definitions, used by the catalog test and the docs/errors.md generator.
public static partial class ErrorCatalog
{
    private const BindingFlags AllStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static IReadOnlyList<string> Codes(Type codesRoot) =>
    [
        .. Flatten(codesRoot)
            .SelectMany(t => t.GetFields(AllStatic))
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!),
    ];

    public static IReadOnlyList<ErrorSpec> Specs(Assembly assembly) =>
    [
        .. assembly.GetTypes()
            .SelectMany(t => t.GetFields(AllStatic))
            .Where(f => f.FieldType == typeof(ErrorSpec))
            .Select(f => (ErrorSpec)f.GetValue(null)!),
    ];

    // Human-readable rule violations; empty means the catalog is consistent.
    public static IReadOnlyList<string> Problems(Type codesRoot, Assembly specAssembly, IEnumerable<string> coveredCodes)
    {
        var codes = Codes(codesRoot);
        var specs = Specs(specAssembly);
        var covered = coveredCodes.ToHashSet();
        var problems = new List<string>();

        foreach (var code in codes)
        {
            if (!CodeFormat().IsMatch(code))
            {
                problems.Add($"{code}: must look like <area>.<reason> in lower case.");
            }

            switch (specs.Count(s => s.Code == code))
            {
                case 0:
                    problems.Add($"{code}: has no spec (declare a static readonly ErrorSpec).");
                    break;
                case > 1:
                    problems.Add($"{code}: has more than one spec.");
                    break;
            }

            if (!covered.Contains(code))
            {
                problems.Add($"{code}: no test is marked [CoversError].");
            }
        }

        problems.AddRange(specs.Where(s => !codes.Contains(s.Code)).Select(s => $"{s.Code}: spec is not an ErrorCodes constant."));
        return problems;
    }

    // Every operation string (`Command:X`, `Query:X`, `Hub:X`, `Chaos:X`, `Background:X`) declared as a constant on a handler,
    // an `*Operations` class or the hub, with the namespace of the slice that owns it.
    public static IReadOnlyList<OperationInfo> Operations(params Assembly[] assemblies) =>
    [
        .. assemblies.SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetFields(AllStatic | BindingFlags.Instance).Where(f => f.DeclaringType == t).Select(f => (Type: t, Field: f)))
            .Where(x => x.Field is { IsLiteral: true } && x.Field.FieldType == typeof(string)
                && (x.Field.Name.EndsWith("Operation", StringComparison.Ordinal) || x.Type.Name.EndsWith("Operations", StringComparison.Ordinal)))
            .Select(x => new OperationInfo((string)x.Field.GetRawConstantValue()!, x.Type.Namespace ?? string.Empty))
            .Where(o => OperationFormat().IsMatch(o.Operation))
            .DistinctBy(o => o.Operation)
            .OrderBy(o => o.Operation, StringComparer.Ordinal),
    ];

    private static IEnumerable<Type> Flatten(Type root) =>
        new[] { root }.Concat(root.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(Flatten));

    [GeneratedRegex(@"^[a-z][a-z0-9]*\.[a-z][a-z0-9_]*$")]
    private static partial Regex CodeFormat();

    [GeneratedRegex(@"^(Command|Query|Hub|Chaos|Background):.+$")]
    private static partial Regex OperationFormat();
}

// Namespace of the slice that owns an operation constant; Folder is where that slice lives inside its project.
public sealed record OperationInfo(string Operation, string Namespace)
{
    public string Folder => string.Join('/', Namespace.Split('.').Skip(2));
}

using System.Runtime.CompilerServices;

namespace DungeonForge.Tests.Errors;

// Marks a test that asserts the error with this code. ErrorCatalogTests fails when a code has no covering test.
// The file is captured at compile time so docs/errors.md can name the test file.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class CoversErrorAttribute(string code, [CallerFilePath] string file = "") : Attribute
{
    public string Code { get; } = code;

    public string File { get; } = file;
}

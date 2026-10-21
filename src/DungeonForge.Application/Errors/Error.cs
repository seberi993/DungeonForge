namespace DungeonForge.Application.Errors;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Unexpected,
}

// One failure. Built only through ErrorSpec.Create so the code, status and description are defined once.
// Fields is the `errors` dictionary of a validation-style 400 (the Web client reads it); null for every other error.
public sealed record Error(
    string Code,
    ErrorKind Kind,
    string Operation,
    string Description,
    IReadOnlyDictionary<string, string> Parameters,
    int Status,
    IReadOnlyDictionary<string, string[]>? Fields = null);

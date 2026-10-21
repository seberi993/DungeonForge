using System.Runtime.CompilerServices;

namespace DungeonForge.Application.Errors;

// The single definition of one error: its code, kind, one-sentence description and, where today's status differs from the
// kind's, the status to keep. Field makes the response carry `errors:{field:[description]}` like a validation 400 does.
// Declare specs as static readonly fields; ErrorCatalog finds them by reflection. DeclaredIn is filled by the compiler (the file
// that declares the spec) and only feeds docs/errors.md.
public sealed record ErrorSpec(
    string Code, ErrorKind Kind, string Description, int? Status = null, string? Field = null,
    [CallerFilePath] string DeclaredIn = "")
{
    public int StatusCode => Status ?? DefaultStatus(Kind);

    public Error Create(string operation, params Param[] parameters) => new(
        Code,
        Kind,
        operation,
        Description,
        parameters.ToDictionary(p => p.Key, p => p.Value),
        StatusCode,
        Field is null ? null : new Dictionary<string, string[]> { [Field] = [Description] });

    public static int DefaultStatus(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => ErrorStatus.Status400BadRequest,
        ErrorKind.NotFound => ErrorStatus.Status404NotFound,
        ErrorKind.Conflict => ErrorStatus.Status409Conflict,
        _ => ErrorStatus.Status500InternalServerError,
    };
}

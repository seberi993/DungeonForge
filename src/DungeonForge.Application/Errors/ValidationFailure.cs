namespace DungeonForge.Application.Errors;

// The one validation error: a 400 whose `errors` dictionary names each bad field. Endpoint filters (Api) return it.
public static class ValidationFailure
{
    private static readonly ErrorSpec InvalidRequestSpec = new(
        ErrorCodes.Validation.InvalidRequest, ErrorKind.Validation, "The request failed validation; see errors for each field.");

    public static Error Create(string operation, IReadOnlyDictionary<string, string[]> fields) =>
        InvalidRequestSpec.Create(operation) with { Fields = fields };
}

namespace DungeonForge.Api.Errors;

// The one validation error endpoint filters return: a 400 whose `errors` dictionary names each bad field.
public static class ValidationErrors
{
    private static readonly ErrorSpec InvalidRequestSpec = new(
        ErrorCodes.Validation.InvalidRequest, ErrorKind.Validation, "The request failed validation; see errors for each field.");

    public static Error Create(string operation, IReadOnlyDictionary<string, string[]> fields) =>
        InvalidRequestSpec.Create(operation) with { Fields = fields };

    // For a filter: `errors.Count > 0 ? ValidationErrors.ToProblem(op, errors, ctx.HttpContext) : await next(ctx)`.
    public static IResult ToProblem(string operation, IReadOnlyDictionary<string, string[]> fields, HttpContext http) =>
        ErrorMapping.ToProblem(Create(operation, fields), http);
}

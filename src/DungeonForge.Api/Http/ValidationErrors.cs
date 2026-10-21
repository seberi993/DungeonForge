using DungeonForge.Application.Errors;

namespace DungeonForge.Api.Http;

// For an endpoint filter: `errors.Count > 0 ? ValidationErrors.ToProblem(op, errors, ctx.HttpContext) : await next(ctx)`.
public static class ValidationErrors
{
    public static Error Create(string operation, IReadOnlyDictionary<string, string[]> fields) =>
        ValidationFailure.Create(operation, fields);

    public static IResult ToProblem(string operation, IReadOnlyDictionary<string, string[]> fields, HttpContext http) =>
        ErrorMapping.ToProblem(Create(operation, fields), http);
}

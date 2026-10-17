using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace DungeonForge.Api.Errors;

// The one place an Error becomes an HTTP response. The waiting 409 is a success variant and never comes through here.
public static class ErrorMapping
{
    // 200 with the value, or the mapped problem. Endpoints with another success shape call Match themselves.
    public static IResult ToHttp<T>(this Result<T> result, HttpContext http) =>
        result.Match<IResult>(value => TypedResults.Ok(value), error => ToProblem(error, http));

    public static ProblemHttpResult ToProblem(Error error, HttpContext http)
    {
        ProblemDetails problem = error.Fields is { } fields
            ? new HttpValidationProblemDetails(new Dictionary<string, string[]>(fields))
            : new ProblemDetails();
        problem.Status = error.Status;
        problem.Detail = error.Description;
        problem.Extensions["code"] = error.Code;
        problem.Extensions["operation"] = error.Operation;
        problem.Extensions["parameters"] = error.Parameters;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? http.TraceIdentifier;
        return TypedResults.Problem(problem);
    }
}

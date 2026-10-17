using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace DungeonForge.Api.Errors;

// The one place an Error becomes an HTTP response. The waiting 409 is a success variant and never comes through here.
public static class ErrorMapping
{
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

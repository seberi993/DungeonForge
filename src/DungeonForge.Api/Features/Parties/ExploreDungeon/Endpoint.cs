using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Errors;
using Microsoft.AspNetCore.Mvc;

namespace DungeonForge.Api.Features.Parties.ExploreDungeon;

public sealed class ExploreDungeonEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/parties/{id:guid}/explore", async Task<IResult> (
                Guid id, ExploreDungeonHandler handler, HttpContext http, CancellationToken ct, [FromBody] ExploreRequest? request = null) =>
                (await handler.HandleAsync(id, request, ct)).Match<IResult>(
                    // Waiting is a success variant: the 409 body the Web client reads as "waiting".
                    outcome => outcome.Waiting is { } waiting ? TypedResults.Conflict(waiting) : TypedResults.Ok(outcome.Response),
                    error => ErrorMapping.ToProblem(error, http)))
           .AddEndpointFilter(async (ctx, next) =>
           {
               var request = ctx.Arguments.OfType<ExploreRequest>().FirstOrDefault();
               return ExploreRequestLimits.Validate(request?.Actions) is { } error
                   ? ValidationErrors.ToProblem(
                       ExploreOperations.For(request), new Dictionary<string, string[]> { ["actions"] = [error] }, ctx.HttpContext)
                   : await next(ctx);
           })
           .WithName("ExploreDungeon");
}
